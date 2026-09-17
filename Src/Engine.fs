namespace BoolOps

open System
open Euclid
open Euclid.EuclidErrors

/// <summary>Runs boolean operations on Shapes. Holds all scratch buffers of the algorithm, so that repeated
/// operations on one engine do not allocate anything but the result Polyline2Ds.
/// Not thread safe, use one engine per thread.
/// The tolerance is absolute, in the units of the coordinates: points closer than the tolerance are the same
/// point, a point closer than the tolerance to a segment lies on it. See DESIGN.md section 2.</summary>
[<Sealed; NoEquality; NoComparison>]
type BoolOpsEngine (tolerance: float) =

    do
        if Double.IsNaN tolerance || Double.IsInfinity tolerance || tolerance < 0.0 then
            fail $"BoolOpsEngine: tolerance {tolerance} must be a finite number that is not negative."

    let state = EngineState tolerance

    /// The default absolute tolerance: 1e-6
    static member DefaultTolerance : float = 1e-6

    /// The absolute tolerance of this engine.
    member _.Tolerance : float = tolerance

    /// The internal buffers, for tests and debugging.
    member internal _.State : EngineState = state

    /// <summary>Runs one boolean operation between a subject and a clip Shape.
    /// The fill rule of each Shape decides what is inside it, see the docs of FillRule.</summary>
    /// <param name="subject">The subject Shape.</param>
    /// <param name="clip">The clip Shape. May be empty, then Union returns the subject simplified under its fill rule.</param>
    /// <param name="op">Union, Intersection, Difference (subject minus clip) or Xor.</param>
    /// <returns>A simple Shape with fill rule Positive: no self intersections, no overlaps, outer contours counter clockwise, holes clockwise.
    /// Input vertices of the result keep their exact coordinates.</returns>
    member _.Execute (subject: Shape, clip: Shape, op: ClipType) : Shape =
        if isNull (box subject) then fail "BoolOpsEngine.Execute: subject is null."
        if isNull (box clip) then fail "BoolOpsEngine.Execute: clip is null."
        let s = state
        s.Clear ()
        Ingest.addShape s subject 0
        Ingest.addShape s clip 1
        Ingest.finish s
        let results = ResizeArray<Polyline2D> ()
        if s.SegCount > 0 then
            Intersect.buildTree s
            Intersect.findIntersections s
            Intersect.splitSegments s
            Graph.cluster s
            Graph.buildEdges s
            Graph.buildRings s
            Winding.computeByRayCast s
            Link.select s subject.FillRule clip.FillRule op
            Link.link s results
        Shape.createUnchecked (results, FillRule.Positive)

    /// Resolves the self intersections and overlaps of one Shape under its fill rule.
    /// The same as a Union with an empty clip.
    member e.Simplify (shape: Shape) : Shape =
        e.Execute (shape, Shape.empty FillRule.NonZero, ClipType.Union)

    /// Union of any number of Shapes, each under its own fill rule.
    /// Every Shape is simplified on its own first, then all results are merged in one NonZero pass.
    member e.UnionAll (shapes: seq<Shape>) : Shape =
        if isNull shapes then fail "BoolOpsEngine.UnionAll: shapes is null."
        let all = ResizeArray<Polyline2D> ()
        for shape in shapes do
            let simple = e.Simplify shape
            all.AddRange simple.Paths
        e.Simplify (Shape.createUnchecked (all, FillRule.NonZero))
