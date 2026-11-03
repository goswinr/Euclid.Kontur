module TestKonturData

open System
open Euclid
open TestUtil
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

/// A closed Polyline2D from points. The first point is repeated at the end.
let private poly (pts: (float * float) list) =
    let p = Polyline2D (pts.Length + 1)
    for (x, y) in pts do p.AddXY (x, y)
    let (x0, y0) = pts.Head
    p.AddXY (x0, y0)
    p

/// A closed counter clockwise square from (x, y) with the given size.
let private square x y size =
    poly [ (x, y); (x + size, y); (x + size, y + size); (x, y + size) ]

/// A random polygon of random points, usually self intersecting.
let private randomTangle (rand: Random) cx cy radius corners =
    poly [ for _ in 1 .. corners do (cx + radius * (2.0 * rand.NextDouble () - 1.0), cy + radius * (2.0 * rand.NextDouble () - 1.0)) ]

let private tolerance = 1e-6

/// Where a result vertex comes from: an input point, or the crossing of two input segments.
type private Origin =
    | At of isSubject: bool * path: int * point: int
    | Crossing of a: CrossingSide * b: CrossingSide

/// One segment of a crossing, as the crossing function saw it.
and private CrossingSide = {
    IsSubject: bool
    Path: int
    StartIndex: int
    EndIndex: int
    Parameter: float
    }

/// Every point tagged with its own origin.
let private tagged (isSubject: bool) (k: Kontur) : KonturData<Origin> =
    KonturData.init (k, fun p i -> At (isSubject, p, i))

/// The crossing function for Origin values. It also checks that the values of the end points of each segment are
/// the ones belonging to its indices, which tests the lookup of input values from the other side.
let private crossing (a: CrossedSegment<Origin>) (b: CrossedSegment<Origin>) : Origin =
    for c in [ a; b ] do
        assertThat c.Start (tag $"start value of {c.AsString}" >> isEqualTo (At (c.IsSubject, c.PathIndex, c.StartIndex)))
        assertThat c.End (tag $"end value of {c.AsString}" >> isEqualTo (At (c.IsSubject, c.PathIndex, c.EndIndex)))
        assertThat (c.Parameter >= 0.0 && c.Parameter <= 1.0) (tag $"parameter of {c.AsString} is in 0..1" >> isTrue)
    if a.IsSubject <> b.IsSubject then
        assertThat a.IsSubject (tag "the subject segment comes first" >> isTrue)
    let side (c: CrossedSegment<Origin>) = { IsSubject = c.IsSubject; Path = c.PathIndex; StartIndex = c.StartIndex; EndIndex = c.EndIndex; Parameter = c.Parameter }
    Crossing (side a, side b)

/// The point at parameter t on the segment of the input described by a crossing side.
let private pointOn (subject: Kontur) (clip: Kontur) (c: CrossingSide) : Pt =
    let pl = (if c.IsSubject then subject else clip).Paths.[c.Path]
    let a = pl.GetPt c.StartIndex
    let b = pl.GetPt c.EndIndex
    Pt (a.X + (b.X - a.X) * c.Parameter, a.Y + (b.Y - a.Y) * c.Parameter)

/// The distance between two points.
let private dist (a: Pt) (b: Pt) = sqrt ((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y))

/// Checks that every result vertex has the value of where it comes from: an input vertex is bit for bit the input
/// point its value names, a crossing vertex lies on both segments its value names, at their parameters.
/// Returns the count of crossing vertices.
let private checkOrigins (subject: Kontur) (clip: Kontur) (result: KonturData<Origin>) : int =
    let paths = result.Kontur.Paths
    assertThat result.Data.Count (tag "one data array per result path" >> isEqualTo paths.Count)
    let mutable crossings = 0
    for r = 0 to paths.Count - 1 do
        let pl = paths.[r]
        let d = result.Data.[r]
        assertThat d.Length (tag $"result path {r} has one value per point" >> isEqualTo pl.PointCount)
        assertThat d.[d.Length - 1] (tag $"result path {r} closes with the value of its first point" >> isEqualTo d.[0])
        for i = 0 to pl.PointCount - 2 do
            let pt = pl.GetPt i
            match d.[i] with
            | At (isSubject, p, j) ->
                let input = (if isSubject then subject else clip).Paths.[p]
                assertThat (pl.GetX i, pl.GetY i) (tag $"result vertex {i} of path {r} is input point {j} of path {p} bit for bit" >> isEqualTo (input.GetX j, input.GetY j))
            | Crossing (a, b) ->
                crossings <- crossings + 1
                assertThat (dist pt (pointOn subject clip a)) (tag $"crossing {i} of path {r} lies on its first segment" >> isLessOrEqual tolerance)
                assertThat (dist pt (pointOn subject clip b)) (tag $"crossing {i} of path {r} lies on its second segment" >> isLessOrEqual tolerance)
    crossings

/// A height that is a linear function of the position, so linear interpolation along any segment reproduces it.
let private height (x: float) (y: float) = 3.0 * x - 2.0 * y + 7.0

/// Every point gets the height of its position.
let private withHeights (k: Kontur) : KonturData<float> =
    KonturData.init (k, fun p i -> height (k.Paths.[p].GetX i) (k.Paths.[p].GetY i))

/// The crossing function for heights: interpolates along the first segment.
let private lerpA (a: CrossedSegment<float>) (_: CrossedSegment<float>) : float =
    a.Start + (a.End - a.Start) * a.Parameter

/// A crossing within tolerance of another vertex merges into it, so its position moves by up to about the tolerance
/// and its height by that times the gradient of the height, which is below 4.
let private snapAccuracy = { Absolute = 1e-5; Relative = 0.0 }

/// Checks that every result vertex carries the height of its position.
let private checkHeights (result: KonturData<float>) : unit =
    for r = 0 to result.Kontur.PathCount - 1 do
        let pl = result.Kontur.Paths.[r]
        for i = 0 to pl.PointCount - 1 do
            assertThat result.Data.[r].[i] (tag $"height of vertex {i} of path {r}" >> isCloseTo snapAccuracy (height (pl.GetX i) (pl.GetY i)))

/// The coordinates of all result paths, for comparing two results.
let private allXYs (k: Kontur) =
    [ for p in k.Paths do [ for i in 0 .. p.PointCount - 1 do (p.GetX i, p.GetY i) ] ]

let private ops = [ ClipType.Union; ClipType.Intersection; ClipType.Difference; ClipType.Xor ]

let tests =
    testList ("KonturData", [

        test ("input points keep their values and crossings get the value of the crossing function", fun _ ->
            let a = Kontur.createSingleton (square 0.0 0.0 10.0)
            let b = Kontur.createSingleton (square 5.0 5.0 10.0)
            let engine = KonturEngine tolerance
            let uni = engine.ExecuteWithData (tagged true a, tagged false b, ClipType.Union, crossing)
            assertThat (checkOrigins a b uni) (tag "the union has two crossings" >> isEqualTo 2)
            let unionCorners = [ for d in uni.Data.[0] do match d with At (isSubject, _, j) -> Some (isSubject, j) | _ -> None ] |> List.choose id |> List.distinct |> List.sort
            assertThat unionCorners (tag "the union keeps three corners of each square" >> isEqualTo [ (false, 1); (false, 2); (false, 3); (true, 0); (true, 1); (true, 3) ])
            let inter = engine.ExecuteWithData (tagged true a, tagged false b, ClipType.Intersection, crossing)
            assertThat (checkOrigins a b inter) (tag "the intersection has two crossings" >> isEqualTo 2)
            let corners = [ for d in inter.Data.[0] do match d with At (isSubject, _, j) -> Some (isSubject, j) | _ -> None ] |> List.choose id |> List.distinct |> List.sort
            assertThat corners (tag "the intersection keeps one corner of each square" >> isEqualTo [ (false, 0); (true, 2) ])
        )

        test ("random tangles under every operation", fun _ ->
            let rand = Random 7
            let engine = KonturEngine tolerance
            for _ in 1 .. 20 do
                let a = Kontur.create ([ randomTangle rand 0.0 0.0 10.0 9; randomTangle rand 3.0 1.0 6.0 5 ], FillRule.EvenOdd)
                let b = Kontur.create ([ randomTangle rand 2.0 2.0 10.0 7 ], FillRule.NonZero)
                for op in ops do
                    let r = engine.ExecuteWithData (tagged true a, tagged false b, op, crossing)
                    checkOrigins a b r |> ignore
                    // tracking does not change the geometry:
                    assertThat (allXYs r.Kontur) (tag $"{op} with data has the same contours as without" >> isEqualTo (allXYs (engine.Execute (a, b, op))))
                let s = engine.SimplifyWithData (tagged true a, crossing)
                let crossings = checkOrigins a a s
                assertThat crossings (tag "a tangle has self crossings" >> isGreaterThan 0)
        )

        test ("a linear height is interpolated exactly through chained operations and unionAll", fun _ ->
            let rand = Random 11
            let engine = KonturEngine tolerance
            for _ in 1 .. 10 do
                let a = Kontur.createSingleton (randomTangle rand 0.0 0.0 10.0 8)
                let b = Kontur.createSingleton (randomTangle rand 2.0 1.0 10.0 8)
                let c = Kontur.createSingleton (randomTangle rand 1.0 3.0 8.0 6)
                for op in ops do
                    let r = engine.ExecuteWithData (withHeights a, withHeights b, op, lerpA)
                    checkHeights r
                    let r2 = engine.ExecuteWithData (r, withHeights c, ClipType.Difference, lerpA)
                    checkHeights r2
                checkHeights (engine.UnionAllWithData ([ withHeights a; withHeights b; withHeights c ], lerpA))
        )

        test ("a crossing that two result contours share is computed once", fun _ ->
            let a = Kontur.createSingleton (square 0.0 0.0 10.0)
            let b = Kontur.createSingleton (square 5.0 5.0 10.0)
            let calls = ref 0
            let count (_: CrossedSegment<int>) (_: CrossedSegment<int>) =
                calls.Value <- calls.Value + 1
                -calls.Value
            let xor = (KonturEngine tolerance).ExecuteWithData (KonturData.init (a, fun _ i -> i), KonturData.init (b, fun _ i -> 10 + i), ClipType.Xor, count)
            assertThat xor.Kontur.PathCount (tag "xor is two contours touching at the crossings" >> isEqualTo 2)
            assertThat calls.Value (tag "one call per crossing" >> isEqualTo 2)
            let inPath r = xor.Data.[r] |> Array.filter (fun v -> v < 0) |> Array.distinct |> Array.sort |> List.ofArray
            assertThat (inPath 0) (tag "the first contour has both crossings" >> isEqualTo [ -2; -1 ])
            assertThat (inPath 1) (tag "the second contour has the same crossings" >> isEqualTo [ -2; -1 ])
        )

        test ("an input vertex on a segment of the other shape keeps its value", fun _ ->
            let a = Kontur.createSingleton (square 0.0 0.0 10.0)
            let b = Kontur.createSingleton (poly [ (10.0, 5.0); (20.0, 0.0); (20.0, 10.0) ]) // touches the right edge of a at its first point
            let calls = ref 0
            let fails (_: CrossedSegment<Origin>) (_: CrossedSegment<Origin>) =
                calls.Value <- calls.Value + 1
                At (true, -1, -1)
            let uni = (KonturEngine tolerance).ExecuteWithData (tagged true a, tagged false b, ClipType.Union, fails)
            checkOrigins a b uni |> ignore
            assertThat calls.Value (tag "a T junction is no crossing" >> isEqualTo 0)
            let touching = [ for d in uni.Data do for v in d.[0 .. d.Length - 2] do if v = At (false, 0, 0) then 1 ] |> List.sum
            assertThat touching (tag "the touching point is in both contours with the value of the clip point" >> isEqualTo 2)
        )

        test ("points within tolerance become one vertex with the value of the first", fun _ ->
            let a = Kontur.createSingleton (square 0.0 0.0 10.0)
            let b = Kontur.createSingleton (poly [ (10.0, 10.0 + 1e-8); (20.0, 10.0); (20.0, 20.0); (10.0, 20.0) ]) // its first point is within tolerance of a corner of a
            let uni = (KonturEngine tolerance).ExecuteWithData (tagged true a, tagged false b, ClipType.Union, crossing)
            checkOrigins a b uni |> ignore
            let all = [ for d in uni.Data do yield! d ]
            assertThat all (tag "the subject corner is kept" >> contain (At (true, 0, 2)))
            assertThat (List.contains (At (false, 0, 0)) all) (tag "the clip point merged into it is gone" >> isFalse)
        )

        test ("dropped points and dropped paths do not shift the indices", fun _ ->
            let tiny = poly [ (50.0, 50.0); (50.0 + 1e-9, 50.0); (50.0, 50.0 + 1e-9) ] // fewer than three distinct points, ingest drops it
            let sq = poly [ (0.0, 0.0); (10.0, 0.0); (10.0, 1e-8); (10.0, 10.0); (0.0, 10.0) ] // the third point is within tolerance of the second
            let a = Kontur.create ([ tiny; sq ], FillRule.NonZero)
            let s = (KonturEngine tolerance).SimplifyWithData (tagged true a, crossing)
            checkOrigins a a s |> ignore
            let all = [ for d in s.Data do yield! d ] |> List.distinct |> List.sort
            assertThat all (tag "the points of the second path, without the dropped one" >> isEqualTo [ At (true, 1, 0); At (true, 1, 1); At (true, 1, 3); At (true, 1, 4) ])
        )

        test ("init, create and the checks of the data arrays", fun _ ->
            let pl = square 0.0 0.0 1.0
            let k = Kontur.createSingleton pl
            let called = ResizeArray<int> ()
            let d = KonturData.init (k, (fun _ i -> called.Add i; i * 10))
            assertThat (List.ofArray d.Data.[0]) (tag "the closing point gets the value of the first" >> isEqualTo [ 0; 10; 20; 30; 0 ])
            assertThat (List.ofSeq called) (tag "valueOf is not called for the closing point" >> isEqualTo [ 0; 1; 2; 3 ])
            assertThat (KonturData.create (k, [ [| 1; 2; 3; 4; 1 |] ])).Data.Count (tag "create with matching arrays" >> isEqualTo 1)
            assertThat (fun () -> KonturData.create (k, [ [| 1; 2; 3; 4 |] ]) |> ignore) (tag "an array without the closing point" >> throws)
            assertThat (fun () -> KonturData.create (k, ([||]: int[][])) |> ignore) (tag "no array for the path" >> throws)
            assertThat (fun () -> KonturData.createSingleton (pl, (null: int[])) |> ignore) (tag "a null array" >> throws)
            let open' = Polyline2D 2
            open'.AddXY (0.0, 0.0)
            open'.AddXY (1.0, 0.0)
            assertThat (fun () -> KonturData.createSingleton (open', [| 0; 1 |]) |> ignore) (tag "an open path" >> throws)
        )

        test ("operations without data leave the tracking buffers empty", fun _ ->
            let rand = Random 3
            let a = Kontur.createSingleton (randomTangle rand 0.0 0.0 10.0 12)
            let b = Kontur.createSingleton (randomTangle rand 1.0 1.0 10.0 12)
            let engine = KonturEngine tolerance
            for op in ops do engine.Execute (a, b, op) |> ignore
            engine.UnionAll [ a; b ] |> ignore
            assertThat engine.State.VertPoint.Length (tag "no point indices recorded" >> isEqualTo 0)
            assertThat engine.State.PathSource.Length (tag "no path indices recorded" >> isEqualTo 0)
            assertThat engine.State.OutVert.Length (tag "no result vertices recorded" >> isEqualTo 0)
            assertThat engine.State.CrossEvent.Length (tag "no crossing events looked up" >> isEqualTo 0)
            // and after an operation with data the next one without data does not track:
            engine.ExecuteWithData (withHeights a, withHeights b, ClipType.Union, lerpA) |> ignore
            engine.Execute (a, b, ClipType.Union) |> ignore
            assertThat engine.State.Track (tag "tracking is off again" >> isFalse)
            assertThat engine.State.OutCount (tag "the link phase recorded nothing" >> isEqualTo 0)
        )
        ])
