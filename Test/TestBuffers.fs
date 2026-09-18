module TestBuffers

open System
open BoolOps
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

/// Shared by all tests in this file, so they run sequentially to stay deterministic.
let private rand = Random 1234

let private isSortedBy (keys: float[]) (idx: int[]) first last =
    let mutable ok = true
    for i = first to last - 1 do
        if keys.[idx.[i]] > keys.[idx.[i + 1]] then ok <- false
    ok

let tests =
    testSequenced ("Buffers", [

        test ("ensure keeps the array when big enough", fun _ ->
            let a = Array.zeroCreate<float> 8
            a.[3] <- 3.0
            let b = Buffers.ensureFloat a 4 8
            assertThat (obj.ReferenceEquals (a, b)) (tag "same array" >> isTrue)
            assertThat b.[3] (tag "content" >> isEqualTo 3.0)
        )

        test ("ensure grows at least to the minimum and keeps the first count items", fun _ ->
            let a = Array.zeroCreate<int> 4
            a.[0] <- 7
            a.[1] <- 8
            a.[2] <- 9
            let b = Buffers.ensureInt a 2 100
            assertThat b.Length (tag "big enough" >> isGreaterOrEqual 100)
            assertThat b.[0] (tag "kept 0" >> isEqualTo 7)
            assertThat b.[1] (tag "kept 1" >> isEqualTo 8)
            assertThat b.[2] (tag "not kept beyond count" >> isEqualTo 0)
            let c = Buffers.ensureInt (Array.zeroCreate<int> 0) 0 1
            assertThat c.Length (tag "from empty" >> isGreaterOrEqual 1)
        )

        test ("sortByKeys sorts by a key array indexed by item id", fun _ ->
            for n in [ 0; 1; 2; 3; 15; 16; 17; 100; 1000; 5000 ] do
                let keys = Array.init n (fun _ -> Math.Floor (rand.NextDouble () * 50.0)) // many equal keys
                let idx = Array.init n id
                Buffers.sortByKeys idx 0 (n - 1) keys
                assertThat (isSortedBy keys idx 0 (n - 1)) (tag $"sorted for n = {n}" >> isTrue)
                assertThat (Array.sort idx) (tag $"still a permutation for n = {n}" >> isEqualTo (Array.init n id))
        )

        test ("sortByKeys sorts only the given range", fun _ ->
            let keys = [| 5.0; 4.0; 3.0; 2.0; 1.0; 0.0; 9.0; 8.0; 7.0; 6.0 |]
            let idx = Array.init keys.Length id
            Buffers.sortByKeys idx 2 7 keys
            assertThat idx.[0] (tag "untouched before" >> isEqualTo 0)
            assertThat idx.[1] (tag "untouched before" >> isEqualTo 1)
            assertThat (isSortedBy keys idx 2 7) (tag "sorted inside" >> isTrue)
            assertThat idx.[8] (tag "untouched after" >> isEqualTo 8)
            assertThat idx.[9] (tag "untouched after" >> isEqualTo 9)
        )

        test ("sortByKeys handles sorted, reversed and constant input", fun _ ->
            let n = 2000
            for keys in [ Array.init n float; Array.init n (fun i -> float (n - i)); Array.create n 1.0 ] do
                let idx = Array.init n id
                Buffers.sortByKeys idx 0 (n - 1) keys
                assertThat (isSortedBy keys idx 0 (n - 1)) (tag "sorted" >> isTrue)
        )

        test ("selectNth puts the k-th item in place", fun _ ->
            for n in [ 1; 2; 3; 10; 101; 1000 ] do
                for _ in 1 .. 3 do
                    let keys = Array.init n (fun _ -> Math.Floor (rand.NextDouble () * 20.0))
                    let idx = Array.init n id
                    let k = rand.Next n
                    let sortedKeys = Array.sort keys
                    Buffers.selectNth idx keys 0 (n - 1) k
                    assertThat keys.[k] (tag $"k-th key for n = {n}, k = {k}" >> isEqualTo sortedKeys.[k])
                    for i = 0 to k - 1 do
                        assertThat keys.[i] (tag "smaller or equal before" >> isLessOrEqual keys.[k])
                    for i = k + 1 to n - 1 do
                        assertThat keys.[i] (tag "bigger or equal after" >> isGreaterOrEqual keys.[k])
                    assertThat (Array.sort idx) (tag "still a permutation" >> isEqualTo (Array.init n id))
        )
    ])
