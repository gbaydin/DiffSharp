#!/usr/bin/env -S dotnet fsi

#I "../tests/DiffSharp.Tests/bin/Debug/net5.0"
#r "DiffSharp.Core.dll"
#r "DiffSharp.Backends.Torch.dll"
// #r "nuget: libtorch-cuda-10.2-linux-x64, 1.7.0.1"
System.Runtime.InteropServices.NativeLibrary.Load("/home/gunes/anaconda3/lib/python3.8/site-packages/torch/lib/libtorch.so")


open DiffSharp
open DiffSharp.Compose
open DiffSharp.Model
open DiffSharp.Data
open DiffSharp.Optim
open DiffSharp.Util

dsharp.config(backend=Backend.Torch, device=Device.CPU)
dsharp.seed(2)


let nz = 128

let generator =
    dsharp.view([-1;nz])
    --> Linear(nz, 256)
    --> dsharp.leakyRelu(0.2)
    --> Linear(256, 512)
    --> dsharp.leakyRelu(0.2)
    --> Linear(512, 1024)
    --> dsharp.leakyRelu(0.2)
    --> Linear(1024, 28*28)
    --> dsharp.tanh

let discriminator =
    dsharp.view([-1; 28*28])
    --> Linear(28*28, 1024)
    --> dsharp.leakyRelu(0.2)
    --> dsharp.dropout(0.3)
    --> Linear(1024, 512)
    --> dsharp.leakyRelu(0.2)
    --> dsharp.dropout(0.3)
    --> Linear(512, 256)
    --> dsharp.leakyRelu(0.2)
    --> dsharp.dropout(0.3)
    --> Linear(256, 1)
    --> dsharp.sigmoid

print "Generator"
print generator

print "Discriminator"
print discriminator

let epochs = 10
let batchSize = 64
let validInterval = 32

let mnist = MNIST("../data", train=true, transform=fun t -> (t - 0.5) / 0.5)
let loader = mnist.loader(batchSize=batchSize, shuffle=true)

let gopt = Adam(generator, lr=dsharp.tensor(0.0001), beta1=dsharp.tensor(0.5))
let dopt = Adam(discriminator, lr=dsharp.tensor(0.0001), beta1=dsharp.tensor(0.5))

let fixedNoise = dsharp.randn([batchSize; nz])

let start = System.DateTime.Now

for epoch = 1 to epochs do
    for i, x, _ in loader.epoch() do
        // update discriminator
        generator.noDiff()
        // generator.reverseDiff()
        discriminator.reverseDiff()

        let doutput = x --> discriminator
        let dx = doutput.mean() |> float
        let dlabelReal = dsharp.ones([batchSize; 1])
        let dlossReal = dsharp.bceLoss(doutput, dlabelReal)
        // dlossReal.reverse()

        let z = dsharp.randn([batchSize; nz])
        let goutput = z --> generator
        let doutput = goutput --> discriminator
        let dgz1 = doutput.mean() |> float
        let dlabelFake = dsharp.zeros([batchSize; 1])
        let dlossFake = dsharp.bceLoss(doutput, dlabelFake)
        // dlossFake.reverse(zeroDerivatives=false)

        let dloss = dlossReal + dlossFake
        dloss.reverse()
        dopt.step()

        // update generator
        generator.reverseDiff()
        discriminator.noDiff()

        let goutput = z --> generator
        let doutput = goutput --> discriminator
        let dgz2 = doutput.mean() |> float
        let dlabelReal = dsharp.ones([batchSize; 1])
        let gloss = dsharp.bceLoss(doutput, dlabelReal)
        gloss.reverse()
        gopt.step()

        printfn "%A Epoch: %A/%A minibatch: %A/%A gloss: %A dloss: %A d(x): %A d(g(z)): %A / %A" (System.DateTime.Now - start) epoch epochs (i+1) loader.length (float gloss) (float dloss) dx dgz1 dgz2

        if i % validInterval = 0 then
            let realFileName = sprintf "gan_real_samples_epoch_%A_minibatch_%A.png" epoch (i+1)
            printfn "Saving real samples to %A" realFileName
            x.saveImage(realFileName, normalize=true)
            let fakeFileName = sprintf "gan_fake_samples_epoch_%A_minibatch_%A.png" epoch (i+1)
            printfn "Saving fake samples to %A" fakeFileName
            let goutput = fixedNoise --> generator
            goutput.view([-1;1;28;28]).saveImage(fakeFileName, normalize=true)
