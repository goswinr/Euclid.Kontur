module Bench

// The benchmark fixtures of the Klip repository (D:\Git\_Euclid_\Klip), ported to Euclid.Kontur:
// - Test/TypeScript/bench/test-data.ts and its C# twin VitestFixtures in
//   Test/FSharp/Benchmark/VitestFixtureBenchmarks.cs: circles, rectangles, complex polygons, grids,
//   the overlapping pairs, the four circles and the geo scale coordinates
// - Test/FSharp/Benchmark/Benchmarks.cs: the dense random polygons of 100 and 500 edges
//
// This file is part of the Test project, so Fable compiles it to JavaScript together with the tests.
// Both benchmark scripts drive these same fixtures and this same timing harness:
//   dotnet fsi Test/Scripts/console/bench-fixtures.fsx     (.NET, also against Klip and Clipper2)
//   node Test/Scripts/console/bench-fixtures.mjs           (Node, also against Klip and clipper2-ts)
// So the two runtimes run the identical workload, which the printed fixture checksum verifies.
//
// Not ported, because Euclid.Kontur has no counterpart: the PolyTree cases, the reused instance cases of the
// vitest suite (a KonturEngine is always reused, that is what the engine is for) and the clipper2-wasm column.

open System
open Euclid
open Euclid

// ---------------------- a clock and a timing harness that work on both targets ----------------------

#if FABLE_COMPILER
/// The high resolution clock in milliseconds: performance.now() under Node.
let private nowMs () : float = Fable.Core.JsInterop.emitJsExpr () "performance.now()"
#else
let private clock = Diagnostics.Stopwatch.StartNew ()
/// The high resolution clock in milliseconds: the Stopwatch on .NET.
let private nowMs () : float = clock.Elapsed.TotalMilliseconds
#endif

/// <summary>The milliseconds one call of the function takes.
/// Warms up for 100 ms, measures the cost of one call, then times five batches of about 100 ms each and
/// returns the mean of the fastest batch. The fastest batch is the one least disturbed by other work on the
/// machine and by the garbage collector, which makes runs on different days comparable.
/// The same harness on both runtimes, so the numbers can be compared across them.</summary>
let timePerCall (f: unit -> unit) : float =
    let mutable calls = 0
    let start = nowMs ()
    while nowMs () - start < 100.0 || calls < 2 do
        f ()
        calls <- calls + 1
    let estimate = (nowMs () - start) / float calls
    let batch = max 1 (int (100.0 / max estimate 1e-4))
    let mutable best = Double.MaxValue
    for _ = 1 to 5 do
        let s = nowMs ()
        for _ = 1 to batch do f ()
        let ms = (nowMs () - s) / float batch
        if ms < best then best <- ms
    best

// ---------------------- the fixture generators ----------------------

/// <summary>JavaScript's Math.round: a half rounds towards plus infinity.
/// System.Math.Round rounds a half to even, so the two runtimes would disagree on every coordinate that
/// lands exactly on .5 and the fixtures would not be identical. floor (v + 0.5) is the JavaScript rule and
/// compiles to the same arithmetic on both targets.</summary>
let inline private jsRound (v: float) : float = floor (v + 0.5)

/// A path is one interleaved x y array, the layout of Polyline2D.XYs, without a repeated closing point.
/// numPoints points on a circle, counter clockwise from the +X axis.
let generateCircle (radius: float) (centerX: float) (centerY: float) (numPoints: int) : float[] =
    let p = Array.zeroCreate (2 * numPoints)
    for i = 0 to numPoints - 1 do
        let angle = 2.0 * Math.PI * float i / float numPoints
        p.[2 * i] <- jsRound (centerX + radius * cos angle)
        p.[2 * i + 1] <- jsRound (centerY + radius * sin angle)
    p

/// An axis aligned rectangle, clockwise if top is below bottom in the Y up convention.
let generateRectangle (left: float) (top: float) (right: float) (bottom: float) : float[] =
    [| left; top;  right; top;  right; bottom;  left; bottom |]

/// A star shaped polygon around (1000, 1000): radius 500 with five lobes of 100 on top of it.
let generateComplexPolygon (numVertices: int) : float[] =
    let p = Array.zeroCreate (2 * numVertices)
    for i = 0 to numVertices - 1 do
        let angle = 2.0 * Math.PI * float i / float numVertices
        let radius = 500.0 + sin (angle * 5.0) * 100.0
        p.[2 * i] <- jsRound (1000.0 + radius * cos angle)
        p.[2 * i + 1] <- jsRound (1000.0 + radius * sin angle)
    p

/// A grid of disjoint squares, rows by cols, with the given cell size and gap.
let generateGrid (rows: int) (cols: int) (cellSize: float) (gap: float) : float[][] =
    [| for row = 0 to rows - 1 do
        for col = 0 to cols - 1 do
            let x = float col * (cellSize + gap)
            let y = float row * (cellSize + gap)
            yield generateRectangle x y (x + cellSize) (y + cellSize) |]

/// The path moved by (dx, dy).
let translatePath (dx: float) (dy: float) (path: float[]) : float[] =
    Array.init path.Length (fun i -> if i % 2 = 0 then path.[i] + dx else path.[i] + dy)

/// <summary>A Lehmer random number generator: the state is multiplied by 16807 modulo 2^31 - 1.
/// The intermediate product stays below 2^53, so the float arithmetic is exact and .NET and JavaScript
/// produce the same sequence. System.Random cannot be used for a shared fixture: its algorithm differs
/// between Fable and .NET, and between .NET versions.</summary>
let private lehmer (seed: int) : int -> float =
    let mutable state = float seed
    fun (maxValue: int) ->
        state <- state * 16807.0 % 2147483647.0
        floor (state % float maxValue)

/// count random points in a width by height box, the dense and heavily self intersecting polygon of
/// Klip's Benchmarks.cs, with its seed 12345 and its 800 by 600 display size.
let randomPath (width: int) (height: int) (count: int) (seed: int) : float[] =
    let next = lehmer seed
    let p = Array.zeroCreate (2 * count)
    for i = 0 to count - 1 do
        p.[2 * i] <- next width       // the x of a point is drawn before its y, as in Klip's MakeRandomPt
        p.[2 * i + 1] <- next height
    p

// ---------------------- the fixtures, the values of Klip's testData and overlappingPairs ----------------------

let mediumComplex = generateComplexPolygon 100
let largeComplex = generateComplexPolygon 500
let veryLargeComplex = generateComplexPolygon 2000
let mediumRect = generateRectangle 0.0 0.0 500.0 500.0
let mediumGrid = generateGrid 5 5 100.0 20.0
let largeGrid = generateGrid 10 10 50.0 10.0

/// The eight point circle of the four circles fixture.
let private simpleCircle = generateCircle 50.0 100.0 100.0 8

/// The geo scale of the vitest suite: degrees of latitude in units of about a hundredth of a millimetre.
let private geoScale = 360000.0
let private geoComplex = mediumComplex |> Array.map (fun v -> jsRound (v * geoScale))
let private geoComplexShifted = translatePath (jsRound (200.0 * geoScale)) (jsRound (200.0 * geoScale)) geoComplex

// ---------------------- the cases ----------------------

/// One benchmark case: a name, one of the four operations, and the subject and clip paths.
/// An empty clip means the operation runs on the subject alone, which for a union is a simplify.
[<NoEquality; NoComparison>]
type Case = {
    Name: string
    Op: ClipType
    Subject: float[][]
    Clip: float[][]
    }

let private case name op subject clip = { Name = name; Op = op; Subject = subject; Clip = clip }
let private noClip : float[][] = [||]

/// Every ported case, in the order of the describe groups of Klip's clipping-operations.bench.ts.
let cases : Case[] = [|
    // Intersection, Difference and XOR Operations: the overlapping pairs
    case "intersection, medium overlapping polygons" ClipType.Intersection [| mediumComplex |] [| translatePath 200.0 200.0 mediumComplex |]
    case "intersection, large overlapping polygons"  ClipType.Intersection [| largeComplex |]  [| translatePath 400.0 400.0 largeComplex |]
    case "intersection, grid with rectangle"         ClipType.Intersection mediumGrid          [| translatePath 150.0 150.0 mediumRect |]
    case "difference, medium overlapping polygons"   ClipType.Difference   [| mediumComplex |] [| translatePath 200.0 200.0 mediumComplex |]
    case "difference, large overlapping polygons"    ClipType.Difference   [| largeComplex |]  [| translatePath 400.0 400.0 largeComplex |]
    case "xor, medium overlapping polygons"          ClipType.Xor          [| mediumComplex |] [| translatePath 200.0 200.0 mediumComplex |]
    case "xor, large overlapping polygons"           ClipType.Xor          [| largeComplex |]  [| translatePath 400.0 400.0 largeComplex |]
    // Union Operations
    case "union, medium complex polygon"             ClipType.Union [| mediumComplex |]    noClip
    case "union, large complex polygon"              ClipType.Union [| largeComplex |]     noClip
    case "union, very large complex polygon"         ClipType.Union [| veryLargeComplex |] noClip
    case "union, medium grid of 25 rectangles"       ClipType.Union mediumGrid             noClip
    case "union, large grid of 100 rectangles"       ClipType.Union largeGrid              noClip
    // Simple Union Operations
    case "union, 3 disjoint rectangles" ClipType.Union [| generateRectangle 0.0 0.0 100.0 100.0; generateRectangle 150.0 150.0 250.0 250.0; generateRectangle 300.0 0.0 400.0 100.0 |] noClip
    case "union, 2 overlapping rectangles" ClipType.Union [| generateRectangle 0.0 0.0 100.0 100.0; generateRectangle 50.0 50.0 150.0 150.0 |] noClip
    case "union, 4 simple circles" ClipType.Union [| simpleCircle; translatePath 200.0 0.0 simpleCircle; translatePath 0.0 200.0 simpleCircle; translatePath 200.0 200.0 simpleCircle |] noClip
    // Geo scale Coordinates
    case "union, geo scale complex polygon"   ClipType.Union        [| geoComplex |] noClip
    case "intersection, geo scale overlapping" ClipType.Intersection [| geoComplex |] [| geoComplexShifted |]
    // the dense random polygons of Benchmarks.cs, one subject and one clip of the same size
    case "intersection, 100 edge random polygons" ClipType.Intersection [| randomPath 800 600 100 12345 |] [| randomPath 800 600 100 54321 |]
    case "union, 100 edge random polygons"        ClipType.Union        [| randomPath 800 600 100 12345 |] [| randomPath 800 600 100 54321 |]
    case "difference, 100 edge random polygons"   ClipType.Difference   [| randomPath 800 600 100 12345 |] [| randomPath 800 600 100 54321 |]
    case "xor, 100 edge random polygons"          ClipType.Xor          [| randomPath 800 600 100 12345 |] [| randomPath 800 600 100 54321 |]
    case "intersection, 500 edge random polygons" ClipType.Intersection [| randomPath 800 600 500 12345 |] [| randomPath 800 600 500 54321 |]
    case "union, 500 edge random polygons"        ClipType.Union        [| randomPath 800 600 500 12345 |] [| randomPath 800 600 500 54321 |]
    case "difference, 500 edge random polygons"   ClipType.Difference   [| randomPath 800 600 500 12345 |] [| randomPath 800 600 500 54321 |]
    case "xor, 500 edge random polygons"          ClipType.Xor          [| randomPath 800 600 500 12345 |] [| randomPath 800 600 500 54321 |]
    |]

// ---------------------- helpers for the benchmark scripts ----------------------

/// A closed Polyline2D from one interleaved x y array. The first point is repeated as the last one.
let toPolyline (xys: float[]) : Polyline2D =
    let pl = Polyline2D (xys.Length / 2 + 1)
    for i in 0 .. 2 .. xys.Length - 2 do
        pl.AddXY (xys.[i], xys.[i + 1])
    pl.CloseInPlace 0.0
    pl

/// A Kontur of all the paths under the given fill rule. Tupled, so that the Node script can call it.
let toKontur (paths: float[][], fillRule: FillRule) : Kontur =
    Kontur.create (paths |> Array.map toPolyline, fillRule)

/// The count of input points of a case.
let pointCount (c: Case) : int =
    let mutable n = 0
    for p in c.Subject do n <- n + p.Length / 2
    for p in c.Clip do n <- n + p.Length / 2
    n

/// <summary>A checksum over every coordinate of the case. Both runtimes must print the same value for every
/// case, otherwise they are not running the same fixture and none of the times can be compared.
/// The coordinates are integers below 1e9, so the sum stays exact in a float.</summary>
let checksum (c: Case) : float =
    let mutable s = 0.0
    for p in c.Subject do
        for i in 0 .. 2 .. p.Length - 2 do
            s <- s + p.[i] * 31.0 + p.[i + 1] * 17.0
    for p in c.Clip do
        for i in 0 .. 2 .. p.Length - 2 do
            s <- s + p.[i] * 13.0 + p.[i + 1] * 7.0
    s

/// The name of the operation, for the printed table.
let opName (op: ClipType) : string =
    match op with
    | ClipType.Intersection -> "Intersection"
    | ClipType.Difference -> "Difference"
    | ClipType.Xor -> "Xor"
    | _ -> "Union"

/// <summary>Runs every case through one reused KonturEngine and prints a markdown table:
/// the size of the input, the result, the time per call and the fixture checksum.
/// The result path count and area are printed so that the .NET and the Node run can be compared for equality,
/// not only for speed. The label names the runtime.</summary>
let runKontur (label: string) : unit =
    let engine = KonturEngine Kontur.defaultTolerance
    printfn ""
    printfn "### Euclid.Kontur on %s" label
    printfn ""
    printfn "| Case | Op | In paths | In points | Out paths | Out area | ms per call | Fixture checksum |"
    printfn "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |"
    for c in cases do
        let subject = toKontur (c.Subject, FillRule.NonZero)
        let clip = toKontur (c.Clip, FillRule.NonZero)
        let r = engine.Execute (subject, clip, c.Op)
        let ms = timePerCall (fun () -> engine.Execute (subject, clip, c.Op) |> ignore)
        printfn "| %s | %s | %d | %d | %d | %.0f | %.4f | %.0f |"
            c.Name (opName c.Op) (c.Subject.Length + c.Clip.Length) (pointCount c) r.PathCount r.SignedArea ms (checksum c)
