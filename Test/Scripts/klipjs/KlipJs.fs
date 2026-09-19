/// Entry points for the Node script union-polysXY.mjs into the Fable compiled Klip package.
/// Klip's own path constructors are inline, so they do not exist in the JavaScript output. These wrappers do.
module KlipJs

open Klip

/// Builds the Klip paths from one interleaved x y coordinate array per path.
let toPaths (xys: ResizeArray<ResizeArray<float>>) : Paths64<unit> =
    let ps = Paths64<unit> xys.Count
    for c in xys do
        ps.Add (Path64<unit> (c, None))
    ps

/// The self union under NonZero after forcing every path to a positive orientation.
let unionSelfChecked (paths: Paths64<unit>) : Paths64<unit> =
    Klipper.unionSelfChecked paths

/// <summary>One boolean operation between the subject and the clip paths.
/// The clip type and the fill rule are the integer values of Klip's ClipType and FillRule, so the Node script
/// does not need the enums. An empty clip list means the operation runs on the subject alone.</summary>
let booleanOp (clipType: int) (fillRule: int) (subject: Paths64<unit>) (clip: Paths64<unit>) : Paths64<unit> =
    let ct =
        match clipType with
        | 1 -> ClipType.Intersection
        | 2 -> ClipType.Union
        | 3 -> ClipType.Difference
        | 4 -> ClipType.Xor
        | _ -> ClipType.NoClip
    let fr =
        match fillRule with
        | 0 -> FillRule.EvenOdd
        | 2 -> FillRule.Positive
        | 3 -> FillRule.Negative
        | _ -> FillRule.NonZero
    let c = if isNull (box clip) || clip.Count = 0 then Unchecked.defaultof<Paths64<unit>> else clip
    Klipper.booleanOp (ct, subject, c, fr)

/// <summary>The net area of a result: the sum of the signed areas of its paths.
/// Klip, Clipper2 and Euclid.Kontur all return positively oriented results with clockwise holes, so the signed sum is
/// the area of the region and can be compared between them. A sum of absolute areas could not: it also counts
/// every hole as positive.</summary>
let netArea (paths: Paths64<unit>) : float =
    let mutable a = 0.0
    for p in paths do
        a <- a + p.SignedArea
    a
