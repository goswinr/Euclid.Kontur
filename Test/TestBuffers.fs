module TestBuffers

open System
open BoolOps

#if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
open Fable.Mocha
#else
open Expecto
#endif

let private rand = Random 1234

let private isSortedBy (keys: float[]) (idx: int[]) first last =
    let mutable ok = true
    for i = first to last - 1 do
        if keys.[idx.[i]] > keys.[idx.[i + 1]] then ok <- false
    ok

let tests =
    testList "Buffers" [

        testCase "ensure keeps the array when big enough" <| fun _ ->
            let a = Array.zeroCreate<float> 8
            a.[3] <- 3.0
            let b = Buffers.ensureFloat a 4 8
            Expect.isTrue (obj.ReferenceEquals (a, b)) "same array"
            Expect.equal b.[3] 3.0 "content"

        testCase "ensure grows at least to the minimum and keeps the first count items" <| fun _ ->
            let a = Array.zeroCreate<int> 4
            a.[0] <- 7
            a.[1] <- 8
            a.[2] <- 9
            let b = Buffers.ensureInt a 2 100
            Expect.isTrue (b.Length >= 100) "big enough"
            Expect.equal b.[0] 7 "kept 0"
            Expect.equal b.[1] 8 "kept 1"
            Expect.equal b.[2] 0 "not kept beyond count"
            let c = Buffers.ensureInt (Array.zeroCreate<int> 0) 0 1
            Expect.isTrue (c.Length >= 1) "from empty"

        testCase "sortByKeys sorts by a key array indexed by item id" <| fun _ ->
            for n in [ 0; 1; 2; 3; 15; 16; 17; 100; 1000; 5000 ] do
                let keys = Array.init n (fun _ -> Math.Floor (rand.NextDouble () * 50.0)) // many equal keys
                let idx = Array.init n id
                Buffers.sortByKeys idx 0 (n - 1) keys
                Expect.isTrue (isSortedBy keys idx 0 (n - 1)) $"sorted for n = {n}"
                Expect.equal (Array.sort idx) (Array.init n id) $"still a permutation for n = {n}"

        testCase "sortByKeys sorts only the given range" <| fun _ ->
            let keys = [| 5.0; 4.0; 3.0; 2.0; 1.0; 0.0; 9.0; 8.0; 7.0; 6.0 |]
            let idx = Array.init keys.Length id
            Buffers.sortByKeys idx 2 7 keys
            Expect.equal idx.[0] 0 "untouched before"
            Expect.equal idx.[1] 1 "untouched before"
            Expect.isTrue (isSortedBy keys idx 2 7) "sorted inside"
            Expect.equal idx.[8] 8 "untouched after"
            Expect.equal idx.[9] 9 "untouched after"

        testCase "sortByKeys handles sorted, reversed and constant input" <| fun _ ->
            let n = 2000
            for keys in [ Array.init n float; Array.init n (fun i -> float (n - i)); Array.create n 1.0 ] do
                let idx = Array.init n id
                Buffers.sortByKeys idx 0 (n - 1) keys
                Expect.isTrue (isSortedBy keys idx 0 (n - 1)) "sorted"

        testCase "selectNth puts the k-th item in place" <| fun _ ->
            for n in [ 1; 2; 3; 10; 101; 1000 ] do
                for _ in 1 .. 3 do
                    let keys = Array.init n (fun _ -> Math.Floor (rand.NextDouble () * 20.0))
                    let idx = Array.init n id
                    let k = rand.Next n
                    let sortedKeys = Array.sort keys
                    Buffers.selectNth idx keys 0 (n - 1) k
                    Expect.equal keys.[k] sortedKeys.[k] $"k-th key for n = {n}, k = {k}"
                    for i = 0 to k - 1 do
                        Expect.isTrue (keys.[i] <= keys.[k]) "smaller or equal before"
                    for i = k + 1 to n - 1 do
                        Expect.isTrue (keys.[i] >= keys.[k]) "bigger or equal after"
                    Expect.equal (Array.sort idx) (Array.init n id) "still a permutation"
    ]
