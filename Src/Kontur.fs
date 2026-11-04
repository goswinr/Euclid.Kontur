namespace Euclid

open Euclid.EuclidErrors

/// <summary>A Kontur (German for contour) is a region of the plane defined by one or more closed Polyline2Ds and a fill rule.
/// Paths may self intersect, overlap each other, or be nested. The fill rule decides what is inside.
/// The fill rule belongs to the Kontur, not to the boolean operation, unlike in Clipper.
/// Subject and clip get separate winding numbers on every edge, and each fill rule is applied to
/// its own winding number before the boolean combines them. So two different fill rules never
/// conflict, they are two independent decisions.
/// What this buys is one pass instead of two: an EvenOdd glyph unioned with a NonZero CAD outline
/// works in one operation. With a fill rule per operation the glyph would have to be simplified
/// first, then unioned.
/// Kontur values returned by a boolean operation are always simple: no self intersections, no overlaps,
/// outer paths counter clockwise, holes clockwise, and their fill rule is Positive. Such a result
/// reads the same under NonZero, Positive and EvenOdd, so the rule of a result never has to be changed.
/// A Kontur does not copy the Polyline2Ds it is given. They are still mutable, so they must not be changed
/// in a way that opens them while the Kontur is in use.</summary>
[<NoEquality; NoComparison>] // because its made up from floats
type Kontur private (paths: ResizeArray<Polyline2D>, fillRule: FillRule) =

    /// Fails if the Polyline2D is not closed or has fewer than three distinct points.
    /// A closed Polyline2D stores its first point twice, so it has at least four points.
    static member internal checkPath (methodName: string) (pathIndex: int) (pl: Polyline2D) : unit =
        if isNull (box pl) then fail $"Kontur.{methodName}: path {pathIndex} is null."
        if pl.PointCount < 4 then fail $"Kontur.{methodName}: path {pathIndex} has only {pl.PointCount} points, a closed path needs at least 4 (three distinct points plus the repeated first point)."
        if not pl.IsClosed then
            let s = pl.Start
            let e = pl.End
            fail $"Kontur.{methodName}: path {pathIndex} is not closed, its first point {s.AsString} and last point {e.AsString} differ. Use Polyline2D.CloseInPlace first."

    /// The closed paths of this Kontur. Each path stores its first point twice, as first and as last point.
    /// The paths of a boolean result come in no particular order: there may be several outer paths, and a hole
    /// may come before the outer path around it. Use Outers and Holes to tell them apart.
    /// This is the live internal list, do not add open paths to it.
    member _.Paths : ResizeArray<Polyline2D> = paths

    /// How the winding number decides what is inside this Kontur.
    member _.FillRule : FillRule = fillRule

    /// The count of paths in this Kontur.
    member _.PathCount : int = paths.Count

    /// TRUE if this Kontur has no paths.
    member _.IsEmpty : bool = paths.Count = 0

    /// The axis aligned bounding rectangle around all paths.
    /// Fails on an empty Kontur.
    member _.BoundingRectangle : BRect =
        if paths.Count = 0 then fail "Kontur.BoundingRectangle: the Kontur is empty."
        let mutable r = paths.[0].BoundingRectangle
        for i = 1 to paths.Count - 1 do
            r <- r.Union paths.[i].BoundingRectangle
        r

    /// Counts how often the paths of this Kontur wind around the point.
    /// The sum of Polyline2D.WindingNumber over all paths.
    /// Points exactly on a path may give either side's result.
    member _.WindingNumber (pt: Pt) : int =
        let mutable w = 0
        for i = 0 to paths.Count - 1 do
            w <- w + paths.[i].WindingNumber pt
        w

    /// Tests if the point is inside this Kontur according to its fill rule.
    /// Points exactly on a path may give either result.
    member s.Contains (pt: Pt) : bool =
        FillRule.isInside fillRule (s.WindingNumber pt)

    /// The sum of the signed areas of all paths. Counter clockwise paths count positive.
    /// This is the area of the Kontur only if the paths do not overlap and holes are oriented opposite
    /// to their outer path, which is the case for every Kontur returned by a boolean operation.
    member _.SignedArea : float =
        let mutable a = 0.0
        for i = 0 to paths.Count - 1 do
            a <- a + paths.[i].SignedArea
        a

    /// The counter clockwise paths of this Kontur, those with a positive signed area, in the order of Paths.
    /// For a Kontur returned by a boolean operation these are its outer paths, and there can be any number of them.
    /// For other Kontur values the orientation alone does not tell outer paths from holes, under NonZero a clockwise
    /// path on its own is filled too. Simplify such a Kontur first.
    /// A new list on every call, the Polyline2Ds are not copied.
    member _.Outers : ResizeArray<Polyline2D> =
        let r = ResizeArray<Polyline2D> ()
        for i = 0 to paths.Count - 1 do
            let p = paths.[i]
            if p.SignedArea > 0.0 then r.Add p
        r

    /// The clockwise paths of this Kontur, those with a negative signed area, in the order of Paths.
    /// For a Kontur returned by a boolean operation these are its holes. Each lies inside one of the Outers,
    /// but not necessarily after it in Paths.
    /// For other Kontur values the orientation alone does not tell outer paths from holes, simplify them first.
    /// A new list on every call, the Polyline2Ds are not copied.
    member _.Holes : ResizeArray<Polyline2D> =
        let r = ResizeArray<Polyline2D> ()
        for i = 0 to paths.Count - 1 do
            let p = paths.[i]
            if p.SignedArea < 0.0 then r.Add p
        r

    /// Format this Kontur into a string with its fill rule, count of paths and count of points.
    member s.AsString : string =
        let mutable pts = 0
        for i = 0 to paths.Count - 1 do
            pts <- pts + paths.[i].PointCount
        $"Kontur({fillRule}, {paths.Count} paths, {pts} points)"

    override s.ToString () = s.AsString

    /// <summary>Creates a Kontur from closed paths and a fill rule.
    /// The paths are not copied, the Kontur refers to the given Polyline2Ds.</summary>
    /// <param name="paths">The closed Polyline2Ds. Each must have its first point repeated as last point and at least three distinct points.</param>
    /// <param name="fillRule">Optional, FillRule.NonZero by default. How the winding number decides what is inside. See the docs of FillRule for why the rule belongs to the Kontur.</param>
    /// <returns>A new Kontur.</returns>
    static member create (paths: seq<Polyline2D>, [<OPT;DEF(FillRule.NonZero)>] fillRule: FillRule) : Kontur =
        if isNull paths then fail "Kontur.create: paths is null."
        let ps = ResizeArray<Polyline2D> paths
        for i = 0 to ps.Count - 1 do
            Kontur.checkPath "create" i ps.[i]
        Kontur (ps, fillRule)

    /// <summary>Creates a Kontur from one closed path.</summary>
    /// <param name="path">The closed Polyline2D. It must have its first point repeated as last point and at least three distinct points.</param>
    /// <param name="fillRule">Optional, FillRule.NonZero by default. How the winding number decides what is inside.</param>
    /// <returns>A new Kontur with one path.</returns>
    static member createSingleton (path: Polyline2D, [<OPT;DEF(FillRule.NonZero)>] fillRule: FillRule) : Kontur =
        Kontur.checkPath "createSingleton" 0 path
        let ps = ResizeArray<Polyline2D> 1
        ps.Add path
        Kontur (ps, fillRule)

    /// Creates a Kontur without checking the paths. For results of the engine, whose paths are closed by construction.
    static member internal createUnchecked (paths: ResizeArray<Polyline2D>, fillRule: FillRule) : Kontur =
        Kontur (paths, fillRule)

    /// Creates an empty Kontur without any paths.
    static member empty (fillRule: FillRule) : Kontur =
        Kontur (ResizeArray<Polyline2D> 0, fillRule)

    /// The counter clockwise paths of the Kontur, the outer paths of a boolean result. See the member Outers.
    static member outers (k: Kontur) : ResizeArray<Polyline2D> =
        k.Outers

    /// The clockwise paths of the Kontur, the holes of a boolean result. See the member Holes.
    static member holes (k: Kontur) : ResizeArray<Polyline2D> =
        k.Holes

    /// Calls the action for every path of the Kontur, in the order of Paths.
    static member inline iter ([<InlineIfLambda>] action: Polyline2D -> unit) (k: Kontur) : unit =
        let ps = k.Paths
        for i = 0 to ps.Count - 1 do
            action ps.[i]

    /// Calls the action with the index and the path for every path of the Kontur, in the order of Paths.
    static member inline iteri ([<InlineIfLambda>] action: int -> Polyline2D -> unit) (k: Kontur) : unit =
        let ps = k.Paths
        for i = 0 to ps.Count - 1 do
            action i ps.[i]

    /// <summary>A new Kontur of the paths the mapping returns for each path, under the same fill rule.
    /// Fails if the mapping returns a path that is not closed or has fewer than three distinct points.
    /// A mapping that mirrors the paths also reverses their orientation. Then under FillRule.Positive the outer
    /// paths of a boolean result wind the wrong way and nothing is inside any more, unless the mapping reverses them too.</summary>
    /// <param name="mapping">Gets one path of the Kontur and returns the closed path that replaces it, for example a moved copy.</param>
    /// <param name="k">The Kontur to map.</param>
    /// <returns>A new Kontur with one path per path of k.</returns>
    static member map (mapping: Polyline2D -> Polyline2D) (k: Kontur) : Kontur =
        let ps = k.Paths
        let r = ResizeArray<Polyline2D> ps.Count
        for i = 0 to ps.Count - 1 do
            let p = mapping ps.[i]
            Kontur.checkPath "map" i p
            r.Add p
        Kontur (r, k.FillRule)

    /// <summary>A new Kontur with only the paths for which the predicate returns TRUE, under the same fill rule.
    /// The Polyline2Ds are not copied.
    /// Dropping an outer path of a boolean result but not its holes leaves those holes on their own:
    /// under FillRule.Positive they fill nothing, but they still count in PathCount and SignedArea.</summary>
    /// <param name="predicate">Gets one path of the Kontur and returns TRUE to keep it.</param>
    /// <param name="k">The Kontur to filter.</param>
    /// <returns>A new Kontur with the kept paths in the order of Paths.</returns>
    static member filter (predicate: Polyline2D -> bool) (k: Kontur) : Kontur =
        let ps = k.Paths
        let r = ResizeArray<Polyline2D> ()
        for i = 0 to ps.Count - 1 do
            let p = ps.[i]
            if predicate p then r.Add p
        Kontur (r, k.FillRule)

    /// TRUE if the predicate returns TRUE for at least one path of the Kontur. Stops at the first such path.
    /// FALSE for an empty Kontur.
    static member inline exists ([<InlineIfLambda>] predicate: Polyline2D -> bool) (k: Kontur) : bool =
        let ps = k.Paths
        let mutable found = false
        let mutable i = 0
        while not found && i < ps.Count do
            found <- predicate ps.[i]
            i <- i + 1
        found

    /// TRUE if the predicate returns TRUE for every path of the Kontur. Stops at the first path where it does not.
    /// TRUE for an empty Kontur.
    static member inline forall ([<InlineIfLambda>] predicate: Polyline2D -> bool) (k: Kontur) : bool =
        let ps = k.Paths
        let mutable all = true
        let mutable i = 0
        while all && i < ps.Count do
            all <- predicate ps.[i]
            i <- i + 1
        all
