namespace Euclid

open Euclid.EuclidErrors

/// <summary>One of the two input segments that cross at a new vertex of a boolean result, as handed to the
/// crossing function of KonturEngine.ExecuteWithData. Start and End are the values the caller gave to the
/// two end points of the segment, Parameter is where the crossing lies between them.</summary>
[<Sealed; NoEquality; NoComparison>]
type CrossedSegment<'T> internal (start: 'T, end': 'T, parameter: float, isSubject: bool, pathIndex: int, startIndex: int, endIndex: int) =

    /// The value of the start point of the segment, in path direction.
    member _.Start : 'T = start

    /// The value of the end point of the segment, in path direction.
    member _.End : 'T = end'

    /// Where the crossing lies along the segment: 0.0 at its start point, 1.0 at its end point.
    member _.Parameter : float = parameter

    /// TRUE if the segment belongs to the subject, FALSE if it belongs to the clip.
    member _.IsSubject : bool = isSubject

    /// The index of the segment's path in the Paths of its Kontur.
    member _.PathIndex : int = pathIndex

    /// The index of the segment's start point in its Polyline2D.
    member _.StartIndex : int = startIndex

    /// The index of the segment's end point in its Polyline2D. It is 0 for the closing segment of a path.
    /// It is more than StartIndex + 1 when the points in between were closer than the tolerance to the start point
    /// and were dropped, then the segment spans several segments of the Polyline2D.
    member _.EndIndex : int = endIndex

    /// Format this CrossedSegment into a string with its path, its point indices and its parameter.
    member _.AsString : string =
        let group = if isSubject then "subject" else "clip"
        $"CrossedSegment({group} path {pathIndex}, points {startIndex} to {endIndex}, at {parameter})"

    override s.ToString () = s.AsString


/// <summary>A Kontur together with one value of type 'T for every point of every path, for tracking where the vertices
/// of a boolean result come from. Data.[p].[i] belongs to Kontur.Paths.[p].Points.[i].
/// Like its Polyline2D, each array has the closing point too: its last item belongs to the repeated first point.
/// The engine reads only the first item for that point, and writes the same value to both ends of a result array.
/// The values can be anything: an index into the caller's own tables, a height, a color, a reference to an object.
/// KonturEngine.ExecuteWithData hands the value of every input point on to the result vertex it becomes, and calls a
/// function of the caller for every new vertex where two segments cross.
/// Like a Kontur, a KonturData does not copy the arrays it is given.</summary>
[<Sealed; NoEquality; NoComparison>]
type KonturData<'T> private (kontur: Kontur, data: ResizeArray<'T[]>) =

    /// Fails unless there is exactly one array per path and one value per point of that path, the closing point included.
    static member internal checkData (methodName: string) (kontur: Kontur) (data: ResizeArray<'T[]>) : unit =
        if data.Count <> kontur.PathCount then
            fail $"{methodName}: there are {data.Count} data arrays for {kontur.PathCount} paths, one array per path is needed."
        for p = 0 to data.Count - 1 do
            let d = data.[p]
            if isNull (box d) then fail $"{methodName}: the data array of path {p} is null."
            let n = kontur.Paths.[p].PointCount
            if d.Length <> n then
                fail $"{methodName}: the data array of path {p} has {d.Length} items but its Polyline2D has {n} points. One value per point is needed, the closing point included."

    /// The Kontur the values belong to.
    member _.Kontur : Kontur = kontur

    /// One array of values per path of the Kontur, with one value per point of that path, the closing point included.
    /// This is the live internal list.
    member _.Data : ResizeArray<'T[]> = data

    /// Format this KonturData into a string with its Kontur.
    member _.AsString : string = $"KonturData({kontur.AsString})"

    override s.ToString () = s.AsString

    /// <summary>Creates a KonturData from a Kontur and one array of values per path.
    /// The list is copied, the arrays are not.</summary>
    /// <param name="kontur">The Kontur the values belong to.</param>
    /// <param name="data">One array per path of the Kontur, each with one value per point of its Polyline2D, the closing point included.
    /// The value of the closing point is not used, the value of the first point stands for both.</param>
    /// <returns>A new KonturData.</returns>
    static member create (kontur: Kontur, data: seq<'T[]>) : KonturData<'T> =
        if isNull (box kontur) then fail "KonturData.create: kontur is null."
        if isNull (box data) then fail "KonturData.create: data is null."
        let ds = ResizeArray<'T[]> data
        KonturData<'T>.checkData "KonturData.create" kontur ds
        KonturData (kontur, ds)

    /// <summary>Creates a KonturData from one closed path and its values.</summary>
    /// <param name="path">The closed Polyline2D. It must have its first point repeated as last point and at least three distinct points.</param>
    /// <param name="data">One value per point of the Polyline2D, the closing point included.
    /// The value of the closing point is not used, the value of the first point stands for both.</param>
    /// <param name="fillRule">Optional, FillRule.NonZero by default. How the winding number decides what is inside.</param>
    /// <returns>A new KonturData with one path.</returns>
    static member createSingleton (path: Polyline2D, data: 'T[], [<OPT;DEF(FillRule.NonZero)>] fillRule: FillRule) : KonturData<'T> =
        let k = Kontur.createSingleton (path, fillRule)
        let ds = ResizeArray<'T[]> 1
        ds.Add data
        KonturData<'T>.checkData "KonturData.createSingleton" k ds
        KonturData (k, ds)

    /// <summary>Creates a KonturData by calling a function for every point of every path.
    /// The closing point of each path gets the value of its first point, the function is not called for it.</summary>
    /// <param name="kontur">The Kontur the values belong to.</param>
    /// <param name="valueOf">Gets the index of the path in kontur.Paths and the index of the point in its Polyline2D and returns the value of that point.</param>
    /// <returns>A new KonturData.</returns>
    static member init (kontur: Kontur, valueOf: int -> int -> 'T) : KonturData<'T> =
        if isNull (box kontur) then fail "KonturData.init: kontur is null."
        if isNull (box valueOf) then fail "KonturData.init: valueOf is null."
        let ds = ResizeArray<'T[]> kontur.PathCount
        for p = 0 to kontur.PathCount - 1 do
            let n = kontur.Paths.[p].PointCount
            let d = Array.zeroCreate<'T> n
            for i = 0 to n - 2 do
                d.[i] <- valueOf p i
            if n > 0 then d.[n - 1] <- d.[0]
            ds.Add d
        KonturData (kontur, ds)

    /// Creates a KonturData without checking the arrays. For results of the engine, whose arrays match by construction.
    static member internal createUnchecked (kontur: Kontur, data: ResizeArray<'T[]>) : KonturData<'T> =
        KonturData (kontur, data)
