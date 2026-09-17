namespace BoolOps

open Euclid
open Euclid.EuclidErrors

/// <summary>A region of the plane, defined by one or more closed Polyline2Ds and a fill rule.
/// Paths may self intersect, overlap each other, or be nested. The fill rule decides what is inside.
/// The fill rule belongs to the Shape, not to the boolean operation, unlike in Clipper.
/// Subject and clip get separate winding numbers on every edge, and each fill rule is applied to
/// its own winding number before the boolean combines them. So two different fill rules never
/// conflict, they are two independent decisions.
/// What this buys is one pass instead of two: an EvenOdd glyph unioned with a NonZero CAD outline
/// works in one operation. With a fill rule per operation the glyph would have to be simplified
/// first, then unioned.
/// Shapes returned by a boolean operation are always simple: no self intersections, no overlaps,
/// outer paths counter clockwise, holes clockwise, and their fill rule is Positive. Such a result
/// reads the same under NonZero, Positive and EvenOdd, so the rule of a result never has to be changed.
/// A Shape does not copy the Polyline2Ds it is given. They are still mutable, so they must not be changed
/// in a way that opens them while the Shape is in use.</summary>
[<NoEquality; NoComparison>] // because its made up from floats
type Shape private (paths: ResizeArray<Polyline2D>, fillRule: FillRule) =

    /// Fails if the Polyline2D is not closed or has fewer than three distinct points.
    /// A closed Polyline2D stores its first point twice, so it has at least four points.
    static member internal checkPath (methodName: string) (pathIndex: int) (pl: Polyline2D) : unit =
        if isNull (box pl) then fail $"Shape.{methodName}: path {pathIndex} is null."
        if pl.PointCount < 4 then fail $"Shape.{methodName}: path {pathIndex} has only {pl.PointCount} points, a closed path needs at least 4 (three distinct points plus the repeated first point)."
        if not pl.IsClosed then
            let s = pl.Start
            let e = pl.End
            fail $"Shape.{methodName}: path {pathIndex} is not closed, its first point {s.AsString} and last point {e.AsString} differ. Use Polyline2D.CloseInPlace first."

    /// The closed paths of this Shape. Each path stores its first point twice, as first and as last point.
    /// This is the live internal list, do not add open paths to it.
    member _.Paths : ResizeArray<Polyline2D> = paths

    /// How the winding number decides what is inside this Shape.
    member _.FillRule : FillRule = fillRule

    /// The count of paths in this Shape.
    member _.PathCount : int = paths.Count

    /// TRUE if this Shape has no paths.
    member _.IsEmpty : bool = paths.Count = 0

    /// The axis aligned bounding rectangle around all paths.
    /// Fails on an empty Shape.
    member _.BoundingRectangle : BRect =
        if paths.Count = 0 then fail "Shape.BoundingRectangle: the Shape is empty."
        let mutable r = paths.[0].BoundingRectangle
        for i = 1 to paths.Count - 1 do
            r <- r.Union paths.[i].BoundingRectangle
        r

    /// Counts how often the paths of this Shape wind around the point.
    /// The sum of Polyline2D.WindingNumber over all paths.
    /// Points exactly on a path may give either side's result.
    member _.WindingNumber (pt: Pt) : int =
        let mutable w = 0
        for i = 0 to paths.Count - 1 do
            w <- w + paths.[i].WindingNumber pt
        w

    /// Tests if the point is inside this Shape according to its fill rule.
    /// Points exactly on a path may give either result.
    member s.Contains (pt: Pt) : bool =
        FillRule.isInside fillRule (s.WindingNumber pt)

    /// The sum of the signed areas of all paths. Counter clockwise paths count positive.
    /// This is the area of the Shape only if the paths do not overlap and holes are oriented opposite
    /// to their outer path, which is the case for every Shape returned by a boolean operation.
    member _.SignedArea : float =
        let mutable a = 0.0
        for i = 0 to paths.Count - 1 do
            a <- a + paths.[i].SignedArea
        a

    /// Format this Shape into a string with its fill rule, count of paths and count of points.
    member s.AsString : string =
        let mutable pts = 0
        for i = 0 to paths.Count - 1 do
            pts <- pts + paths.[i].PointCount
        $"Shape({fillRule}, {paths.Count} paths, {pts} points)"

    override s.ToString () = s.AsString

    /// <summary>Creates a Shape from closed paths and a fill rule.
    /// The paths are not copied, the Shape refers to the given Polyline2Ds.</summary>
    /// <param name="paths">The closed Polyline2Ds. Each must have its first point repeated as last point and at least three distinct points.</param>
    /// <param name="fillRule">How the winding number decides what is inside. See the docs of FillRule for why the rule belongs to the Shape.</param>
    /// <returns>A new Shape.</returns>
    static member create (paths: seq<Polyline2D>, fillRule: FillRule) : Shape =
        if isNull paths then fail "Shape.create: paths is null."
        let ps = ResizeArray<Polyline2D> paths
        for i = 0 to ps.Count - 1 do
            Shape.checkPath "create" i ps.[i]
        Shape (ps, fillRule)

    /// <summary>Creates a Shape from one closed path.</summary>
    /// <param name="path">The closed Polyline2D. It must have its first point repeated as last point and at least three distinct points.</param>
    /// <param name="fillRule">How the winding number decides what is inside. Optional, NonZero by default.</param>
    /// <returns>A new Shape with one path.</returns>
    static member ofPolyline (path: Polyline2D, [<OPT;DEF(FillRule.NonZero)>] fillRule: FillRule) : Shape =
        Shape.checkPath "ofPolyline" 0 path
        let ps = ResizeArray<Polyline2D> 1
        ps.Add path
        Shape (ps, fillRule)

    /// Creates an empty Shape without any paths.
    static member empty (fillRule: FillRule) : Shape =
        Shape (ResizeArray<Polyline2D> 0, fillRule)
