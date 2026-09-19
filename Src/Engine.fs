namespace Euclid

open System
open Euclid.EuclidErrors

/// <summary>Runs boolean operations on Kontur values. Holds all scratch buffers of the algorithm, so that repeated
/// operations on one engine do not allocate anything but the result Polyline2Ds.
/// Not thread safe, use one engine per thread.
/// The tolerance is absolute, in the units of the coordinates: points closer than the tolerance are the same
/// point, a point closer than the tolerance to a segment lies on it. See DESIGN.md section 2.</summary>
[<Sealed; NoEquality; NoComparison>]
type KonturEngine (tolerance: float) =

    do
        if Double.IsNaN tolerance || Double.IsInfinity tolerance || tolerance < 0.0 then
            fail $"KonturEngine: tolerance {tolerance} must be a finite number that is not negative."

    let state = EngineState tolerance

    /// The empty clip of Simplify, shared by all engines. It is only ever read.
    static let emptyClip = Kontur.empty FillRule.NonZero

    /// The default absolute tolerance: 1e-6
    static member DefaultTolerance : float = 1e-6

    /// The absolute tolerance of this engine.
    member _.Tolerance : float = tolerance

    /// The internal buffers, for tests and debugging.
    member internal _.State : EngineState = state

    /// <summary>Runs one boolean operation between a subject and a clip Kontur.
    /// The fill rule of each Kontur decides what is inside it, see the docs of FillRule.</summary>
    /// <param name="subject">The subject Kontur.</param>
    /// <param name="clip">The clip Kontur. May be empty, then Union returns the subject simplified under its fill rule.</param>
    /// <param name="op">Union, Intersection, Difference (subject minus clip) or Xor.</param>
    /// <returns>A simple Kontur with fill rule Positive: no self intersections, no overlaps, outer contours counter clockwise, holes clockwise.
    /// Input vertices of the result keep their exact coordinates.</returns>
    member _.Execute (subject: Kontur, clip: Kontur, op: ClipType) : Kontur =
        if isNull (box subject) then fail "KonturEngine.Execute: subject is null."
        if isNull (box clip) then fail "KonturEngine.Execute: clip is null."
        let s = state
        s.Clear ()
        Ingest.addKontur s subject 0
        Ingest.addKontur s clip 1
        Ingest.finish s
        let results = ResizeArray<Polyline2D> ()
        if s.SegCount > 0 then
            Intersect.sortRects s
            Intersect.findIntersections s
            Intersect.splitSegments s
            Graph.cluster s
            Graph.buildEdges s
            Graph.buildRings s
            Winding.propagate s
            Link.select s subject.FillRule clip.FillRule op
            Link.link s results
        Kontur.createUnchecked (results, FillRule.Positive)

    /// Resolves the self intersections and overlaps of one Kontur under its fill rule.
    /// The same as a Union with an empty clip.
    member e.Simplify (shape: Kontur) : Kontur =
        e.Execute (shape, emptyClip, ClipType.Union)

    /// Union of any number of Kontur values, each under its own fill rule.
    /// Every Kontur is simplified on its own first, then all results are merged in one NonZero pass.
    member e.UnionAll (shapes: seq<Kontur>) : Kontur =
        if isNull shapes then fail "KonturEngine.UnionAll: shapes is null."
        let all = ResizeArray<Polyline2D> ()
        for shape in shapes do
            let simple = e.Simplify shape
            all.AddRange simple.Paths
        e.Simplify (Kontur.createUnchecked (all, FillRule.NonZero))
