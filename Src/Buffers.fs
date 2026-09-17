namespace BoolOps

open System

/// Growable flat arrays and in place sorting helpers for the internal buffers of the engine.
/// All functions work on raw arrays with a separate count, so that Fable compiles them to typed arrays
/// and no per element object is ever allocated.
module internal Buffers =

    /// Returns a float array with at least minCapacity items, keeping the first count items.
    /// Returns the same array if it is already big enough, otherwise a new array of at least twice the old size.
    /// Not generic, so that Fable emits a Float64Array.
    let ensureFloat (arr: float[]) (count: int) (minCapacity: int) : float[] =
        if arr.Length >= minCapacity then
            arr
        else
            let newCapacity = max minCapacity (max 16 (arr.Length * 2))
            let bigger = Array.zeroCreate<float> newCapacity
            if count > 0 then Array.blit arr 0 bigger 0 count
            bigger

    /// Returns an int array with at least minCapacity items, keeping the first count items.
    /// Returns the same array if it is already big enough, otherwise a new array of at least twice the old size.
    /// Not generic, so that Fable emits an Int32Array.
    let ensureInt (arr: int[]) (count: int) (minCapacity: int) : int[] =
        if arr.Length >= minCapacity then
            arr
        else
            let newCapacity = max minCapacity (max 16 (arr.Length * 2))
            let bigger = Array.zeroCreate<int> newCapacity
            if count > 0 then Array.blit arr 0 bigger 0 count
            bigger

    /// Sorts idx.[first..last] in place, using the given strict "less than" comparison on the item ids stored in idx.
    /// A quicksort with a median of three pivot and an insertion sort for short ranges.
    /// Recursion goes into the smaller part only, so the stack depth is at most logarithmic.
    /// Not stable, items comparing equal may end up in any order.
    let inline sortIndices (idx: int[]) (first: int) (last: int) ([<InlineIfLambda>] less: int -> int -> bool) : unit =
        let inline swap i j =
            let t = idx.[i] in idx.[i] <- idx.[j] ; idx.[j] <- t
        let inline insertionSort lo hi =
            for i = lo + 1 to hi do
                let v = idx.[i]
                let mutable j = i - 1
                while j >= lo && less v idx.[j] do
                    idx.[j + 1] <- idx.[j]
                    j <- j - 1
                idx.[j + 1] <- v
        let rec quickSort lo0 hi0 =
            let mutable lo = lo0
            let mutable hi = hi0
            while hi - lo > 16 do
                // put the median of first, middle and last item into the middle, which also places sentinels at both ends:
                let mid = lo + (hi - lo) / 2
                if less idx.[mid] idx.[lo]  then swap mid lo
                if less idx.[hi]  idx.[lo]  then swap hi  lo
                if less idx.[hi]  idx.[mid] then swap hi  mid
                let pivot = idx.[mid]
                // Hoare partition:
                let mutable i = lo
                let mutable j = hi
                while i <= j do
                    while less idx.[i] pivot do i <- i + 1
                    while less pivot idx.[j] do j <- j - 1
                    if i <= j then
                        swap i j
                        i <- i + 1
                        j <- j - 1
                // recurse into the smaller part, loop on the bigger one:
                if j - lo < hi - i then
                    quickSort lo j
                    lo <- i
                else
                    quickSort i hi
                    hi <- j
            insertionSort lo hi
        if last > first then quickSort first last

    /// Sorts idx.[first..last] in place by ascending key, where the keys array is indexed by the item ids stored in idx.
    /// Not inline, so that it can be called from the tests. Inside the library use sortIndices directly.
    let sortByKeys (idx: int[]) (first: int) (last: int) (keys: float[]) : unit =
        sortIndices idx first last (fun a b -> keys.[a] < keys.[b])

    /// Reorders idx.[first..last], together with the parallel keys, such that position k holds the item
    /// that a full sort by key would put there. All items before k have a smaller or equal key,
    /// all items after k a bigger or equal one.
    /// The keys array is parallel to idx by position, not indexed by item id.
    /// This is a quickselect with a three way partition. It runs in place, in linear time on average,
    /// and does not allocate. Only the median is needed for a BVH split, so a full sort would be wasted work.
    /// Ported from Euclid.BVH.
    let selectNth (idx: int[]) (keys: float[]) (first: int) (last: int) (k: int) : unit =
        let inline swap i j =
            let ti = idx.[i] in idx.[i] <- idx.[j] ; idx.[j] <- ti
            let tk = keys.[i] in keys.[i] <- keys.[j] ; keys.[j] <- tk
        let mutable lo = first
        let mutable hi = last
        let mutable go = true
        while go && lo < hi do
            // the median of the first, middle and last key as the pivot,
            // so that sorted or reversed input does not degenerate to quadratic time:
            let a = keys.[lo]
            let b = keys.[lo + (hi - lo) / 2]
            let c = keys.[hi]
            let pivot =
                if a < b then (if b < c then b elif a < c then c else a)
                else          (if a < c then a elif b < c then c else b)
            // partition lo..hi into three parts: smaller than the pivot, equal to it, bigger than it.
            // The equal part is never empty, so each iteration shrinks the range and the loop terminates.
            let mutable lt = lo // keys.[lo   .. lt-1] are smaller than the pivot
            let mutable gt = hi // keys.[gt+1 .. hi  ] are bigger than the pivot
            let mutable i  = lo // keys.[lt   .. i-1 ] are equal to the pivot
            while i <= gt do
                let v = keys.[i]
                if   v < pivot then swap i lt ; lt <- lt + 1 ; i <- i + 1
                elif v > pivot then swap i gt ; gt <- gt - 1 // i is not advanced, the swapped in key is still unseen
                else                            i <- i + 1
            if   k < lt then hi <- lt - 1 // the k-th item is in the smaller part
            elif k > gt then lo <- gt + 1 // the k-th item is in the bigger part
            else             go <- false  // the k-th item is in the equal part, it is already in place
