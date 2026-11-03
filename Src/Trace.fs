namespace Euclid

open Euclid.EuclidErrors

/// <summary>After a run of the pipeline with EngineState.Track set: maps the values of the input points onto the
/// result vertices. No value of type 'T passes through the pipeline. Vertex ids do, through every phase, so the
/// engine only records which input point each input vertex was copied from and which vertex each result vertex is.
/// The two segments of every crossing vertex are already in its two split events. The values are looked up here,
/// once per result vertex, and the crossing function runs only for crossings that are part of the result.</summary>
module internal Trace =

    /// The path of an input vertex, by binary search in PathStart.
    let private pathOfVertex (s: EngineState) (v: int) : int =
        let mutable lo = 0
        let mutable hi = s.PathCount - 1
        while lo < hi do
            let mid = (lo + hi + 1) >>> 1
            if Arr.get s.PathStart mid <= v then lo <- mid else hi <- mid - 1
        lo

    /// The value the caller gave to the input point that the input vertex v was copied from.
    let private inputValue (s: EngineState) (subjectData: ResizeArray<'T[]>) (clipData: ResizeArray<'T[]>) (v: int) : 'T =
        let p = pathOfVertex s v
        let data = if Arr.get s.PathGroup p = 0 then subjectData else clipData
        Arr.get (Rarr.get data (Arr.get s.PathSource p)) (Arr.get s.VertPoint v)

    /// The input segment seg with the values of its end points, for the crossing function.
    let private crossedSegment (s: EngineState) (subjectData: ResizeArray<'T[]>) (clipData: ResizeArray<'T[]>) (seg: int) (t: float) : CrossedSegment<'T> =
        let a = Arr.get s.SegA seg
        let b = Arr.get s.SegB seg
        let p = pathOfVertex s a
        CrossedSegment (
            inputValue s subjectData clipData a,
            inputValue s subjectData clipData b,
            t,
            Arr.get s.PathGroup p = 0,
            Arr.get s.PathSource p,
            Arr.get s.VertPoint a,
            Arr.get s.VertPoint b)

    /// Fills CrossEvent with the first of the two split events of every crossing vertex.
    /// Intersect.intersectPair appends every crossing vertex directly before its two events, the one on the segment
    /// with the lower id first, and the crossing vertices in increasing order. Every other event refers to an input
    /// vertex. The event arrays are never reordered after that, the split phase sorts only EvOrder.
    let private findCrossingEvents (s: EngineState) : unit =
        let first = s.InputVertexCount
        let n = s.VertexCount - first
        s.CrossEvent <- Buffers.ensureInt s.CrossEvent 0 n
        let mutable k = 0
        let mutable e = 0
        while e < s.EvCount do
            let v = Arr.get s.EvVert e
            if v >= first then
                if v <> first + k || e + 1 >= s.EvCount || Arr.get s.EvVert (e + 1) <> v then
                    fail $"Kontur.Trace: the split events of crossing vertex {v} are not the pair recorded with it."
                Arr.set s.CrossEvent k e
                k <- k + 1
                e <- e + 2
            else
                e <- e + 1
        if k <> n then fail $"Kontur.Trace: found split events for {k} of {n} crossing vertices."

    /// <summary>One array of values per result contour, with one value per point, the closing point included.
    /// An input vertex gets the value of its input point. A crossing vertex gets the value of the crossing function,
    /// called once per crossing vertex, with the segment of the lower id first. That is the subject segment when
    /// a subject segment crosses a clip segment. A crossing that appears in two contours gets the same value in both.</summary>
    let mapData (s: EngineState) (results: ResizeArray<Polyline2D>) (subjectData: ResizeArray<'T[]>) (clipData: ResizeArray<'T[]>)
                (onCrossing: CrossedSegment<'T> -> CrossedSegment<'T> -> 'T) : ResizeArray<'T[]> =
        let first = s.InputVertexCount
        let crossCount = s.VertexCount - first
        let cache = Array.zeroCreate<'T> crossCount
        if crossCount > 0 then findCrossingEvents s
        let out = ResizeArray<'T[]> results.Count
        let mutable pos = 0
        for r = 0 to results.Count - 1 do
            let count = results.[r].PointCount - 1 // without the closing duplicate
            let d = Array.zeroCreate<'T> (count + 1)
            for i = 0 to count - 1 do
                let v = Arr.get s.OutVert (pos + i)
                if v < first then
                    Arr.set d i (inputValue s subjectData clipData v)
                else
                    let k = v - first
                    let e = Arr.get s.CrossEvent k
                    if e >= 0 then
                        let a = crossedSegment s subjectData clipData (Arr.get s.EvSeg e) (Arr.get s.EvT e)
                        let b = crossedSegment s subjectData clipData (Arr.get s.EvSeg (e + 1)) (Arr.get s.EvT (e + 1))
                        Arr.set cache k (onCrossing a b)
                        Arr.set s.CrossEvent k (-1)
                    Arr.set d i (Arr.get cache k)
            Arr.set d count (Arr.get d 0)
            pos <- pos + count
            out.Add d
        if pos <> s.OutCount then fail $"Kontur.Trace: the result contours have {pos} vertices but the link phase recorded {s.OutCount}."
        out
