namespace Euclid

open Euclid.EuclidErrors
open System.ComponentModel

/// When Euclid is opened this module will be auto-opened.
/// It only contains extension members for type Polyline2D:
/// the boolean operations of the Kontur module directly on closed Polyline2Ds, without creating a Kontur first.
/// In Union, Intersection, Difference, Xor and UnionMany every Polyline2D is one region under the NonZero fill rule,
/// so its orientation does not matter and a self intersecting Polyline2D is filled wherever it winds around.
/// Simplify is different: it keeps only the regions a Polyline2D winds around in its main direction, the sign of its
/// signed area, and drops the loops that wind the other way, like the inverted loops of an offset.
/// All results are simple closed contours: no self intersections, no overlaps, outer contours counter clockwise, holes clockwise.
/// A fresh engine is created per call. For many operations in a row, for other fill rules or for regions of
/// several paths use Kontur and KonturEngine.
[<AutoOpen; EditorBrowsable(EditorBrowsableState.Never)>]
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

    /// Resolves the self intersections of one closed Polyline2D and keeps only the regions it winds around in its main direction.
    /// The main direction is the sign of the signed area, which is the sum of the areas of all regions weighted by their winding numbers.
    /// A counter clockwise Polyline2D is simplified under the Positive fill rule, a clockwise one under the Negative fill rule.
    /// This equals reversing a clockwise Polyline2D first and then using Positive, without copying it.
    let private simplify (tolerance: float) (pl: Polyline2D) : ResizeArray<Polyline2D> =
        let engine = KonturEngine tolerance
        let shape = Kontur.createSingleton pl // fails on a null or open Polyline2D before the area is computed
        let rule = if pl.SignedArea < 0.0 then FillRule.Negative else FillRule.Positive
        (engine.Simplify (Kontur.createUnchecked (shape.Paths, rule))).Paths

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

        /// Resolves the self intersections of this closed Polyline2D and keeps only the regions it winds around in its main direction, with the given absolute tolerance.
        /// The main direction is the sign of its signed area. Loops that wind the other way, like the inverted loops of an offset, are dropped,
        /// for a counter clockwise and for a clockwise Polyline2D alike: it is simplified under FillRule.Positive or FillRule.Negative.
        /// To keep every region it winds around in either direction use Kontur.simplify (Kontur.createSingleton (polyline, FillRule.NonZero)).
        /// Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        member pl.SimplifyWith (tolerance: float) : ResizeArray<Polyline2D> =
            simplify tolerance pl

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

        /// Resolves the self intersections of this closed Polyline2D and keeps only the regions it winds around in its main direction, with the default tolerance of 1e-6.
        /// The main direction is the sign of its signed area. Loops that wind the other way, like the inverted loops of an offset, are dropped,
        /// for a counter clockwise and for a clockwise Polyline2D alike: it is simplified under FillRule.Positive or FillRule.Negative.
        /// To keep every region it winds around in either direction use Kontur.simplify (Kontur.createSingleton (polyline, FillRule.NonZero)).
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

        /// Resolves the self intersections of one closed Polyline2D and keeps only the regions it winds around in its main direction, with the given absolute tolerance.
        /// The main direction is the sign of its signed area. Loops that wind the other way, like the inverted loops of an offset, are dropped,
        /// for a counter clockwise and for a clockwise Polyline2D alike: it is simplified under FillRule.Positive or FillRule.Negative.
        /// To keep every region it winds around in either direction use Kontur.simplify (Kontur.createSingleton (polyline, FillRule.NonZero)).
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

        /// Resolves the self intersections of one closed Polyline2D and keeps only the regions it winds around in its main direction, with the default tolerance of 1e-6.
        /// The main direction is the sign of its signed area. Loops that wind the other way, like the inverted loops of an offset, are dropped,
        /// for a counter clockwise and for a clockwise Polyline2D alike: it is simplified under FillRule.Positive or FillRule.Negative.
        /// To keep every region it winds around in either direction use Kontur.simplify (Kontur.createSingleton (polyline, FillRule.NonZero)).
        /// Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        static member simplify (polyline: Polyline2D) : ResizeArray<Polyline2D> =
            polyline.SimplifyWith KonturEngine.DefaultTolerance

        /// Union of any number of Polyline2Ds, with the default tolerance of 1e-6.
        /// All must be closed. Each is filled on its own, so Polyline2Ds of opposite orientation do not cancel each other.
        /// Returns the closed contours of the result, outer contours counter clockwise, holes clockwise.
        static member unionMany (polylines: seq<Polyline2D>) : ResizeArray<Polyline2D> =
            Polyline2D.unionManyWith KonturEngine.DefaultTolerance polylines
