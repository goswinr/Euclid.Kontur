// The benchmark fixtures of the Klip repository on .NET: Euclid.Kontur against Klip and Clipper2 on the same input.
// The fixtures and the timing harness come from Test/Bench.fs, the very file that Fable compiles for the Node
// counterpart bench-fixtures.mjs, so both runtimes run the identical workload. The printed fixture checksums
// must match between the two runs.
// One off exploratory script, not part of CI.
//
// Build the library first:  dotnet build -c Release Src/Euclid.Kontur.fsproj
// Then run:                 dotnet fsi Test/Scripts/console/bench-fixtures.fsx

#r "nuget: Euclid, 0.51.0"
#r "nuget: Klip, 3.1.1"
#r "nuget: Clipper2, 2.0.0"
#r "../../../Src/bin/Release/net6.0/Euclid.Kontur.dll"
#load "../../Bench.fs"

open System
open Euclid

/// Klip's ClipType for one of the four Kontur operations.
let private klipOp (op: ClipType) : Klip.ClipType =
    match op with
    | ClipType.Intersection -> Klip.ClipType.Intersection
    | ClipType.Difference -> Klip.ClipType.Difference
    | ClipType.Xor -> Klip.ClipType.Xor
    | _ -> Klip.ClipType.Union

/// Clipper2's ClipType for one of the four Kontur operations.
let private clipperOp (op: ClipType) : Clipper2Lib.ClipType =
    match op with
    | ClipType.Intersection -> Clipper2Lib.ClipType.Intersection
    | ClipType.Difference -> Clipper2Lib.ClipType.Difference
    | ClipType.Xor -> Clipper2Lib.ClipType.Xor
    | _ -> Clipper2Lib.ClipType.Union

/// Klip's flat buffer paths from the interleaved x y arrays of a fixture. Null for an empty list, which is how
/// Klip's own benchmarks pass "no clip".
let private toKlip (paths: float[][]) : Klip.Paths64<unit> =
    if paths.Length = 0 then Unchecked.defaultof<Klip.Paths64<unit>>
    else
        let ps = Klip.Paths64<unit> paths.Length
        for p in paths do
            ps.Add (Klip.Path64<unit> (ResizeArray p, None))
        ps

/// Clipper2's integer paths from the interleaved x y arrays of a fixture. Every fixture coordinate is a whole
/// number, so the conversion to the integer grid of Clipper2 is exact and all three libraries see the same shape.
let private toClipper (paths: float[][]) : Clipper2Lib.Paths64 =
    let ps = Clipper2Lib.Paths64 ()
    for p in paths do
        let path = Clipper2Lib.Path64 (p.Length / 2)
        for i in 0 .. 2 .. p.Length - 2 do
            path.Add (Clipper2Lib.Point64 (int64 p.[i], int64 p.[i + 1]))
        ps.Add path
    ps

// The net area of a result: the sum of the signed areas of its paths. All three libraries return positively
// oriented results with clockwise holes, so the signed sum is the area of the region and can be compared between
// them. A sum of absolute areas could not: it also counts every hole as positive.

let private klipArea (ps: Klip.Paths64<unit>) =
    if isNull (box ps) then 0.0
    else
        let mutable a = 0.0
        for p in ps do a <- a + p.SignedArea
        a

let private clipperArea (ps: Clipper2Lib.Paths64) =
    let mutable a = 0.0
    for p in ps do a <- a + Clipper2Lib.Clipper.Area p
    a

let private konturArea (s: Kontur) = s.SignedArea

do
    printfn "# The Klip benchmark fixtures on .NET"
    printfn ""
    printfn "%d cases, Euclid.Kontur %s, Klip 3.1.1, Clipper2 2.0.0, .NET %s" Bench.cases.Length "(this build)" (Environment.Version.ToString ())
    printfn "Time per call: the mean of the fastest of five batches after a warmup, see Bench.timePerCall."

    // the same table the Node script prints, so the two runtimes can be compared case by case:
    Bench.runKontur ($"the .NET runtime {Environment.Version}")

    printfn ""
    printfn "### Euclid.Kontur against Klip and Clipper2, milliseconds per call"
    printfn ""
    printfn "| Case | Op | Euclid.Kontur | Klip | Clipper2 | Klip / Euclid.Kontur | Clipper2 / Euclid.Kontur |"
    printfn "| --- | --- | ---: | ---: | ---: | ---: | ---: |"
    let engine = KonturEngine Kontur.defaultTolerance
    let disagree = ResizeArray<string> ()
    for c in Bench.cases do
        let subject = Bench.toKontur (c.Subject, FillRule.NonZero)
        let clip = Bench.toKontur (c.Clip, FillRule.NonZero)
        let kSubject = toKlip c.Subject
        let kClip = toKlip c.Clip
        let cSubject = toClipper c.Subject
        let cClip = toClipper c.Clip
        let cClipOrNull = if c.Clip.Length = 0 then null else cClip
        let bMs = Bench.timePerCall (fun () -> engine.Execute (subject, clip, c.Op) |> ignore)
        let kMs = Bench.timePerCall (fun () -> Klip.Klipper.booleanOp (klipOp c.Op, kSubject, kClip, Klip.FillRule.NonZero) |> ignore)
        let cMs = Bench.timePerCall (fun () -> Clipper2Lib.Clipper.BooleanOp (clipperOp c.Op, cSubject, cClipOrNull, Clipper2Lib.FillRule.NonZero) |> ignore)
        printfn "| %s | %s | %.4f | %.4f | %.4f | %.2fx | %.2fx |"
            c.Name (Bench.opName c.Op) bMs kMs cMs (kMs / bMs) (cMs / bMs)
        // the areas of the three libraries on the same input, as a correctness cross check:
        let bArea = konturArea (engine.Execute (subject, clip, c.Op))
        let kArea = klipArea (Klip.Klipper.booleanOp (klipOp c.Op, kSubject, kClip, Klip.FillRule.NonZero))
        let cArea = clipperArea (Clipper2Lib.Clipper.BooleanOp (clipperOp c.Op, cSubject, cClipOrNull, Clipper2Lib.FillRule.NonZero))
        let biggest = max bArea (max kArea cArea)
        if biggest > 0.0 && (abs (bArea - kArea) / biggest > 0.005 || abs (bArea - cArea) / biggest > 0.005) then
            disagree.Add $"| {c.Name} | {Bench.opName c.Op} | {bArea:F0} | {kArea:F0} | {cArea:F0} |"

    printfn ""
    if disagree.Count = 0 then
        printfn "All three libraries agree on the area of every case within half a percent."
    else
        printfn "### Cases where the areas differ by more than half a percent"
        printfn ""
        printfn "| Case | Op | Euclid.Kontur area | Klip area | Clipper2 area |"
        printfn "| --- | --- | ---: | ---: | ---: |"
        for line in disagree do printfn "%s" line
