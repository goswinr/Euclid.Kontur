namespace Euclid

open Euclid.EuclidErrors

/// When Euclid is opened this module will be auto-opened.
/// It only contains extension members for type Polyline2D:
/// the boolean operations of the Kontur module directly on closed Polyline2Ds, without creating a Kontur first.
/// Every Polyline2D is one region under the NonZero fill rule, so its orientation does not matter
/// and a self intersecting Polyline2D is filled wherever it winds around.
/// All results are simple closed contours: no self intersections, no overlaps, outer contours counter clockwise, holes clockwise.
/// A fresh engine is created per call. For many operations in a row, for other fill rules or for regions of
/// several paths use Kontur and KonturEngine.
[<AutoOpen>]
module AutoOpenPolyline2DKontur =

    /// One boolean operation between two closed Polyline2Ds, each under the NonZero fill rule.
    let private execute (tolerance: float) (subject: Polyline2D) (clip: Polyline2D) (op: ClipType) : ResizeArray<Polyline2D> =
        ((KonturEngine tolerance).Execute (Kontur.createSingleton subject, Kontur.createSingleton clip, op)).Paths

    /// Union of any number of closed Polyline2Ds, each under the NonZero fill rule.
    let private unionAll (tolerance: float) (polylines: seq<Polyline2D>) : ResizeArray<Polyline2D> =
        let shapes = ResizeArray<Kontur> ()
        for pl in polylines do
            shapes.Add (Kontur.createSingleton pl)
        ((KonturEngine tolerance).UnionAll shapes).Paths

    type Polyline2D with

        /// Everything inside this Polyline2D or inside the other, with the given absolute tolerance.
        /// Both must be closed. Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        member pl.UnionWith (tolerance: float) (other: Polyline2D) : ResizeArray<Polyline2D> =
            execute tolerance pl other ClipType.Union

        /// Everything inside this Polyline2D and inside the other, with the given absolute tolerance.
        /// Both must be closed. Returns the closed contours of the result, empty if they do not overlap.
        member pl.IntersectionWith (tolerance: float) (other: Polyline2D) : ResizeArray<Polyline2D> =
            execute tolerance pl other ClipType.Intersection

        /// Everything inside this Polyline2D but not inside the other, with the given absolute tolerance.
        /// Both must be closed. Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        member pl.DifferenceWith (tolerance: float) (other: Polyline2D) : ResizeArray<Polyline2D> =
            execute tolerance pl other ClipType.Difference

        /// Everything inside exactly one of this Polyline2D and the other, with the given absolute tolerance.
        /// Both must be closed. Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        member pl.XorWith (tolerance: float) (other: Polyline2D) : ResizeArray<Polyline2D> =
            execute tolerance pl other ClipType.Xor

        /// Resolves the self intersections and overlaps of this closed Polyline2D under the NonZero fill rule, with the given absolute tolerance.
        /// Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        member pl.SimplifyWith (tolerance: float) : ResizeArray<Polyline2D> =
            ((KonturEngine tolerance).Simplify (Kontur.createSingleton pl)).Paths

        /// Union of this Polyline2D with any number of others, with the given absolute tolerance.
        /// All must be closed. Each is filled on its own, so Polyline2Ds of opposite orientation do not cancel each other.
        /// Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        member pl.UnionManyWith (tolerance: float) (others: seq<Polyline2D>) : ResizeArray<Polyline2D> =
            if isNull others then fail "Polyline2D.UnionManyWith: others is null."
            unionAll tolerance (Seq.append (Seq.singleton pl) others)

        /// Everything inside this Polyline2D or inside the other, with the default tolerance of 1e-6.
        /// Both must be closed. Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        member pl.Union (other: Polyline2D) : ResizeArray<Polyline2D> =
            execute KonturEngine.DefaultTolerance pl other ClipType.Union

        /// Everything inside this Polyline2D and inside the other, with the default tolerance of 1e-6.
        /// Both must be closed. Returns the closed contours of the result, empty if they do not overlap.
        member pl.Intersection (other: Polyline2D) : ResizeArray<Polyline2D> =
            execute KonturEngine.DefaultTolerance pl other ClipType.Intersection

        /// Everything inside this Polyline2D but not inside the other, with the default tolerance of 1e-6.
        /// Both must be closed. Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        member pl.Difference (other: Polyline2D) : ResizeArray<Polyline2D> =
            execute KonturEngine.DefaultTolerance pl other ClipType.Difference

        /// Everything inside exactly one of this Polyline2D and the other, with the default tolerance of 1e-6.
        /// Both must be closed. Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        member pl.Xor (other: Polyline2D) : ResizeArray<Polyline2D> =
            execute KonturEngine.DefaultTolerance pl other ClipType.Xor

        /// Resolves the self intersections and overlaps of this closed Polyline2D under the NonZero fill rule, with the default tolerance of 1e-6.
        /// Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        member pl.Simplify () : ResizeArray<Polyline2D> =
            pl.SimplifyWith KonturEngine.DefaultTolerance

        /// Union of this Polyline2D with any number of others, with the default tolerance of 1e-6.
        /// All must be closed. Each is filled on its own, so Polyline2Ds of opposite orientation do not cancel each other.
        /// Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        member pl.UnionMany (others: seq<Polyline2D>) : ResizeArray<Polyline2D> =
            pl.UnionManyWith KonturEngine.DefaultTolerance others

        /// Everything inside the subject or inside the clip, with the given absolute tolerance.
        /// Both must be closed. Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        static member unionWith (tolerance: float) (subject: Polyline2D) (clip: Polyline2D) : ResizeArray<Polyline2D> =
            execute tolerance subject clip ClipType.Union

        /// Everything inside the subject and inside the clip, with the given absolute tolerance.
        /// Both must be closed. Returns the closed contours of the result, empty if they do not overlap.
        static member intersectionWith (tolerance: float) (subject: Polyline2D) (clip: Polyline2D) : ResizeArray<Polyline2D> =
            execute tolerance subject clip ClipType.Intersection

        /// Everything inside the subject but not inside the clip, with the given absolute tolerance.
        /// Both must be closed. Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        static member differenceWith (tolerance: float) (subject: Polyline2D) (clip: Polyline2D) : ResizeArray<Polyline2D> =
            execute tolerance subject clip ClipType.Difference

        /// Everything inside exactly one of subject and clip, with the given absolute tolerance.
        /// Both must be closed. Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        static member xorWith (tolerance: float) (subject: Polyline2D) (clip: Polyline2D) : ResizeArray<Polyline2D> =
            execute tolerance subject clip ClipType.Xor

        /// Resolves the self intersections and overlaps of one closed Polyline2D under the NonZero fill rule, with the given absolute tolerance.
        /// Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        static member simplifyWith (tolerance: float) (polyline: Polyline2D) : ResizeArray<Polyline2D> =
            polyline.SimplifyWith tolerance

        /// Union of any number of Polyline2Ds, with the given absolute tolerance.
        /// All must be closed. Each is filled on its own, so Polyline2Ds of opposite orientation do not cancel each other.
        /// Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        static member unionManyWith (tolerance: float) (polylines: seq<Polyline2D>) : ResizeArray<Polyline2D> =
            if isNull polylines then fail "Polyline2D.unionManyWith: polylines is null."
            unionAll tolerance polylines

        /// Everything inside the subject or inside the clip, with the default tolerance of 1e-6.
        /// Both must be closed. Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        static member union (subject: Polyline2D) (clip: Polyline2D) : ResizeArray<Polyline2D> =
            execute KonturEngine.DefaultTolerance subject clip ClipType.Union

        /// Everything inside the subject and inside the clip, with the default tolerance of 1e-6.
        /// Both must be closed. Returns the closed contours of the result, empty if they do not overlap.
        static member intersection (subject: Polyline2D) (clip: Polyline2D) : ResizeArray<Polyline2D> =
            execute KonturEngine.DefaultTolerance subject clip ClipType.Intersection

        /// Everything inside the subject but not inside the clip, with the default tolerance of 1e-6.
        /// Both must be closed. Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        static member difference (subject: Polyline2D) (clip: Polyline2D) : ResizeArray<Polyline2D> =
            execute KonturEngine.DefaultTolerance subject clip ClipType.Difference

        /// Everything inside exactly one of subject and clip, with the default tolerance of 1e-6.
        /// Both must be closed. Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        static member xor (subject: Polyline2D) (clip: Polyline2D) : ResizeArray<Polyline2D> =
            execute KonturEngine.DefaultTolerance subject clip ClipType.Xor

        /// Resolves the self intersections and overlaps of one closed Polyline2D under the NonZero fill rule, with the default tolerance of 1e-6.
        /// Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        static member simplify (polyline: Polyline2D) : ResizeArray<Polyline2D> =
            polyline.SimplifyWith KonturEngine.DefaultTolerance

        /// Union of any number of Polyline2Ds, with the default tolerance of 1e-6.
        /// All must be closed. Each is filled on its own, so Polyline2Ds of opposite orientation do not cancel each other.
        /// Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        static member unionMany (polylines: seq<Polyline2D>) : ResizeArray<Polyline2D> =
            Polyline2D.unionManyWith KonturEngine.DefaultTolerance polylines
