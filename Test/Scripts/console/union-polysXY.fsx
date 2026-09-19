// Self union of the noisy data/polysXY.json dataset at ten scales.
// Euclid.Kontur is compared against Klip and Clipper2 at the matching precision, in result and in time per call.
// One off exploratory script, not part of CI. Adapted from Klip/Test/Scripts/console/union-polysXY.fsx.
//
// Build the library first:  dotnet build -c Release Src/Euclid.Kontur.fsproj
// Then run:                 dotnet fsi Test/Scripts/console/union-polysXY.fsx

#r "nuget: Euclid, 0.51.0"
#r "nuget: Klip, 3.1.1"
#r "nuget: Clipper2, 2.0.0"
#r "../../../Src/bin/Release/net6.0/Euclid.Kontur.dll"

open System
open System.IO
open System.Text.Json
open Euclid
open Euclid

type XY = { x: float; y: float }

/// Scales every coordinate by the factor, so Euclid.Kontur with its absolute tolerance
/// sees the same geometry that Clipper2 sees at the matching precision.
let scaled (scale: float) (xy: XY[][]) : XY[][] =
    xy |> Array.map (Array.map (fun p -> { x = p.x * scale; y = p.y * scale }))

/// Klip.unionSelfChecked forces all paths to a positive orientation before the NonZero union.
/// The same here: reverse clockwise paths so none of them count as holes.
let toKontur (xy: XY[][]) : Kontur =
    let paths =
        xy
        |> Array.map (fun ps ->
            let pl = ps |> Array.map (fun p -> Pt (p.x, p.y)) |> Polyline2D.createFromPts
            pl.CloseInPlace ()
            if pl.SignedArea < 0.0 then pl.ReverseInPlace ()
            pl)
    Kontur.create (paths, FillRule.NonZero)

/// How often each timed call is repeated after one warmup call.
let repetitions = 20

/// Runs the function once untimed, then repetitions times timed.
/// Returns the last result and the average time per call in stopwatch ticks.
let timed (f: unit -> 'T) : struct ('T * int64) =
    let mutable result = f ()
    let sw = Diagnostics.Stopwatch.StartNew ()
    for _ = 1 to repetitions do
        result <- f ()
    sw.Stop ()
    struct (result, sw.ElapsedTicks / int64 repetitions)

/// Stopwatch ticks as milliseconds with three decimals.
let ms (ticks: int64) : string =
    (float ticks * 1000.0 / float Diagnostics.Stopwatch.Frequency).ToString "0.000"

do
    let xy: XY[][][] =
        __SOURCE_DIRECTORY__ + "/../data/polysXY.json"
        |> File.ReadAllText
        |> JsonSerializer.Deserialize<_>

    let xy: XY[][] =
        xy
        |> Array.map Array.head // only the outer path of each polygon

    printfn $"Original Paths: {xy.Length}, {repetitions} timed runs per call after one warmup, the input is built outside the timing"
    let engine = KonturEngine Kontur.defaultTolerance
    for i = -3 to 6 do
        let scale = 10. ** float i
        let xys = scaled scale xy

        // Euclid.Kontur, a fresh engine per call and one reused engine:
        let shape = toKontur xys
        let struct (br, tFresh) = timed (fun () -> Kontur.simplify shape)
        let struct (_, tReused) = timed (fun () -> engine.Simplify shape)
        printfn $"Euclid.Kontur:  Scale: {scale}, Result Paths: {br.PathCount}, Area: {br.SignedArea / (scale * scale)}, {ms tFresh} ms fresh engine, {ms tReused} ms reused engine"

        // Klip:
        let kPaths = Klip.Paths64.createFromxyMembers xys
        let struct (kr, tKlip) = timed (fun () -> Klip.Klipper.unionSelfChecked kPaths)
        printfn $"Klip:     Scale: {scale}, Result Paths: {kr.Count}, {ms tKlip} ms"

        // Clipper2:
        let cD =
            let ps = Clipper2Lib.PathsD()
            for xs in xy do
                let c = Clipper2Lib.PathD()
                for p in xs do
                    c.Add(Clipper2Lib.PointD(p.x, p.y))
                ps.Add(c)
            ps
        let struct (cr, tClipper) =
            timed (fun () ->
                Clipper2Lib.Clipper.BooleanOp(
                    Clipper2Lib.ClipType.Union,
                    cD, null,
                    Clipper2Lib.FillRule.NonZero,
                    precision = i))
        printfn $"Clipper2: Scale: {scale}, Result Paths: {cr.Count}, {ms tClipper} ms\n-"
