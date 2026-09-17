namespace BoolOps

open Euclid

/// Boolean operations on Shapes with a fresh engine per call and the default tolerance of 1e-6.
/// For many operations in a row create one BoolOpsEngine and reuse it, that avoids all allocations but the results.
module BoolOps =

    /// The default absolute tolerance used by the functions of this module: 1e-6
    let defaultTolerance : float = BoolOpsEngine.DefaultTolerance

    /// Everything inside the subject or inside the clip, with the given absolute tolerance.
    let unionWith (tolerance: float) (subject: Shape) (clip: Shape) : Shape =
        (BoolOpsEngine tolerance).Execute (subject, clip, ClipType.Union)

    /// Everything inside the subject and inside the clip, with the given absolute tolerance.
    let intersectionWith (tolerance: float) (subject: Shape) (clip: Shape) : Shape =
        (BoolOpsEngine tolerance).Execute (subject, clip, ClipType.Intersection)

    /// Everything inside the subject but not inside the clip, with the given absolute tolerance.
    let differenceWith (tolerance: float) (subject: Shape) (clip: Shape) : Shape =
        (BoolOpsEngine tolerance).Execute (subject, clip, ClipType.Difference)

    /// Everything inside exactly one of subject and clip, with the given absolute tolerance.
    let xorWith (tolerance: float) (subject: Shape) (clip: Shape) : Shape =
        (BoolOpsEngine tolerance).Execute (subject, clip, ClipType.Xor)

    /// Resolves the self intersections and overlaps of one Shape under its fill rule, with the given absolute tolerance.
    let simplifyWith (tolerance: float) (shape: Shape) : Shape =
        (BoolOpsEngine tolerance).Simplify shape

    /// Union of any number of Shapes, each under its own fill rule, with the given absolute tolerance.
    let unionAllWith (tolerance: float) (shapes: seq<Shape>) : Shape =
        (BoolOpsEngine tolerance).UnionAll shapes

    /// Everything inside the subject or inside the clip.
    let union (subject: Shape) (clip: Shape) : Shape =
        unionWith defaultTolerance subject clip

    /// Everything inside the subject and inside the clip.
    let intersection (subject: Shape) (clip: Shape) : Shape =
        intersectionWith defaultTolerance subject clip

    /// Everything inside the subject but not inside the clip.
    let difference (subject: Shape) (clip: Shape) : Shape =
        differenceWith defaultTolerance subject clip

    /// Everything inside exactly one of subject and clip.
    let xor (subject: Shape) (clip: Shape) : Shape =
        xorWith defaultTolerance subject clip

    /// Resolves the self intersections and overlaps of one Shape under its fill rule.
    let simplify (shape: Shape) : Shape =
        simplifyWith defaultTolerance shape

    /// Union of any number of Shapes, each under its own fill rule.
    let unionAll (shapes: seq<Shape>) : Shape =
        unionAllWith defaultTolerance shapes
