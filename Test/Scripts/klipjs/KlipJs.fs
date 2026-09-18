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
