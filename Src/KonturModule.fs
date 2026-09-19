namespace Euclid

/// Boolean operations on Kontur values with a fresh engine per call and the default tolerance of 1e-6.
/// For many operations in a row create one KonturEngine and reuse it, that avoids all allocations but the results.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Kontur =

    /// The default absolute tolerance used by the functions of this module: 1e-6
    let defaultTolerance : float = KonturEngine.DefaultTolerance

    /// Everything inside the subject or inside the clip, with the given absolute tolerance.
    let unionWith (tolerance: float) (subject: Kontur) (clip: Kontur) : Kontur =
        (KonturEngine tolerance).Execute (subject, clip, ClipType.Union)

    /// Everything inside the subject and inside the clip, with the given absolute tolerance.
    let intersectionWith (tolerance: float) (subject: Kontur) (clip: Kontur) : Kontur =
        (KonturEngine tolerance).Execute (subject, clip, ClipType.Intersection)

    /// Everything inside the subject but not inside the clip, with the given absolute tolerance.
    let differenceWith (tolerance: float) (subject: Kontur) (clip: Kontur) : Kontur =
        (KonturEngine tolerance).Execute (subject, clip, ClipType.Difference)

    /// Everything inside exactly one of subject and clip, with the given absolute tolerance.
    let xorWith (tolerance: float) (subject: Kontur) (clip: Kontur) : Kontur =
        (KonturEngine tolerance).Execute (subject, clip, ClipType.Xor)

    /// Resolves the self intersections and overlaps of one Kontur under its fill rule, with the given absolute tolerance.
    let simplifyWith (tolerance: float) (shape: Kontur) : Kontur =
        (KonturEngine tolerance).Simplify shape

    /// Union of any number of Kontur values, each under its own fill rule, with the given absolute tolerance.
    let unionAllWith (tolerance: float) (shapes: seq<Kontur>) : Kontur =
        (KonturEngine tolerance).UnionAll shapes

    /// Everything inside the subject or inside the clip.
    let union (subject: Kontur) (clip: Kontur) : Kontur =
        unionWith defaultTolerance subject clip

    /// Everything inside the subject and inside the clip.
    let intersection (subject: Kontur) (clip: Kontur) : Kontur =
        intersectionWith defaultTolerance subject clip

    /// Everything inside the subject but not inside the clip.
    let difference (subject: Kontur) (clip: Kontur) : Kontur =
        differenceWith defaultTolerance subject clip

    /// Everything inside exactly one of subject and clip.
    let xor (subject: Kontur) (clip: Kontur) : Kontur =
        xorWith defaultTolerance subject clip

    /// Resolves the self intersections and overlaps of one Kontur under its fill rule.
    let simplify (shape: Kontur) : Kontur =
        simplifyWith defaultTolerance shape

    /// Union of any number of Kontur values, each under its own fill rule.
    let unionAll (shapes: seq<Kontur>) : Kontur =
        unionAllWith defaultTolerance shapes
