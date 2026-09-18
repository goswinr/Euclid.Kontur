// Self union of the noisy data/polysXY.json dataset at ten scales.
// BoolOps is compared against Klip and Clipper2 at the matching precision.
// One off exploratory script, not part of CI. Adapted from Klip/Test/Scripts/console/union-polysXY.fsx.
//
// Build the library first:  dotnet build -c Release Src/BoolOps.fsproj
// Then run:                 dotnet fsi Test/Scripts/console/union-polysXY.fsx

#r "nuget: Euclid, 0.51.0"
#r "nuget: Klip, 3.1.1"
#r "nuget: Clipper2, 2.0.0"
#r "../../../Src/bin/Release/net6.0/BoolOps.dll"

open System
open System.IO
open System.Text.Json
open Euclid
open BoolOps

type XY = { x: float; y: float }

/// Scales every coordinate by the factor, so BoolOps with its absolute tolerance
/// sees the same geometry that Clipper2 sees at the matching precision.
let scaled (scale: float) (xy: XY[][]) : XY[][] =
    xy |> Array.map (Array.map (fun p -> { x = p.x * scale; y = p.y * scale }))

/// Klip.unionSelfChecked forces all paths to a positive orientation before the NonZero union.
/// The same here: reverse clockwise paths so none of them count as holes.
let toShape (xy: XY[][]) : Shape =
    let paths =
        xy
        |> Array.map (fun ps ->
            let pl = ps |> Array.map (fun p -> Pt (p.x, p.y)) |> Polyline2D.createFromPts
            pl.CloseInPlace ()
            if pl.SignedArea < 0.0 then pl.ReverseInPlace ()
            pl)
    Shape.create (paths, FillRule.NonZero)

do
    let xy: XY[][][] =
        __SOURCE_DIRECTORY__ + "/../data/polysXY.json"
        |> File.ReadAllText
        |> JsonSerializer.Deserialize<_>

    let xy: XY[][] =
        xy
        |> Array.map Array.head // only the outer path of each polygon

    printfn $"Original Paths: {xy.Length}"
    for i = -3 to 6 do
        let scale = 10. ** float i
        let xys = scaled scale xy

        // BoolOps:
        let br =
            xys
            |> toShape
            |> BoolOps.simplify
        printfn $"BoolOps:  Scale: {scale}, Result Paths: {br.PathCount}, Area: {br.SignedArea / (scale * scale)}"

        // Klip:
        let kr =
            xys
            |> Klip.Paths64.createFromxyMembers
            |> Klip.Klipper.unionSelfChecked
        printfn $"Klip:     Scale: {scale}, Result Paths: {kr.Count}"

        // Clipper2:
        let cD =
            let ps = Clipper2Lib.PathsD()
            for xs in xy do
                let c = Clipper2Lib.PathD()
                for p in xs do
                    c.Add(Clipper2Lib.PointD(p.x, p.y))
                ps.Add(c)
            ps
        let cr =
            Clipper2Lib.Clipper.BooleanOp(
                Clipper2Lib.ClipType.Union,
                cD, null,
                Clipper2Lib.FillRule.NonZero,
                precision = i)
        printfn $"Clipper2: Scale: {scale}, Result Paths: {cr.Count}\n-"
