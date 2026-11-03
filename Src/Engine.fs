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

    /// Runs the pipeline and returns the result contours. With track set, ingest and link also record
    /// where every vertex comes from, for Trace.mapData.
    member private _.Run (subject: Kontur, clip: Kontur, op: ClipType, track: bool) : ResizeArray<Polyline2D> =
        let s = state
        s.Clear ()
        s.Track <- track
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
            if not (Link.tryLink s results) then
                // merging or snapping moved an edge across another one, see Graph.repair
                Graph.repair s
                Graph.buildRings s
                Winding.propagate s
                Link.select s subject.FillRule clip.FillRule op
                Link.link s results
        results

    /// <summary>Runs one boolean operation between a subject and a clip Kontur.
    /// The fill rule of each Kontur decides what is inside it, see the docs of FillRule.</summary>
    /// <param name="subject">The subject Kontur.</param>
    /// <param name="clip">The clip Kontur. May be empty, then Union returns the subject simplified under its fill rule.</param>
    /// <param name="op">Union, Intersection, Difference (subject minus clip) or Xor.</param>
    /// <returns>A simple Kontur with fill rule Positive: no self intersections, no overlaps, outer contours counter clockwise, holes clockwise.
    /// Input vertices of the result keep their exact coordinates.</returns>
    member e.Execute (subject: Kontur, clip: Kontur, op: ClipType) : Kontur =
        if isNull (box subject) then fail "KonturEngine.Execute: subject is null."
        if isNull (box clip) then fail "KonturEngine.Execute: clip is null."
        Kontur.createUnchecked (e.Run (subject, clip, op, false), FillRule.Positive)

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

    /// <summary>Runs one boolean operation like Execute, and also hands the values of the input points on to the
    /// vertices of the result, for tracking where each result vertex comes from.
    /// An input vertex that is part of the result keeps the value of its input point.
    /// That includes an input vertex lying on a segment of the other Kontur, it is no new vertex.
    /// Points within tolerance of each other become one result vertex, which keeps the value of the first of them:
    /// subject before clip, then the earlier path, then the earlier point. An input point wins over a crossing within tolerance of it.
    /// A new vertex where two segments cross gets the value that onCrossing returns for the two segments.
    /// onCrossing is called once for every crossing that is part of the result, after the result contours are complete,
    /// so a crossing that two result contours share gets the same value in both.
    /// Operations without data pay nothing for this: the engine records only integers, and only while this method runs.</summary>
    /// <param name="subject">The subject Kontur with one value per point.</param>
    /// <param name="clip">The clip Kontur with one value per point. May be empty, then Union returns the subject simplified under its fill rule.</param>
    /// <param name="op">Union, Intersection, Difference (subject minus clip) or Xor.</param>
    /// <param name="onCrossing">Returns the value of a new vertex from the two segments crossing there.
    /// The first segment is the subject segment when a subject segment crosses a clip segment.
    /// For a crossing of two segments of the same Kontur it is the earlier one, by path and then by point.</param>
    /// <returns>A simple Kontur with fill rule Positive, as from Execute, and one array of values per result contour,
    /// with one value per point, the closing point included. The result can be the input of the next operation.</returns>
    member e.ExecuteWithData (subject: KonturData<'T>, clip: KonturData<'T>, op: ClipType, onCrossing: CrossedSegment<'T> -> CrossedSegment<'T> -> 'T) : KonturData<'T> =
        if isNull (box subject) then fail "KonturEngine.ExecuteWithData: subject is null."
        if isNull (box clip) then fail "KonturEngine.ExecuteWithData: clip is null."
        if isNull (box onCrossing) then fail "KonturEngine.ExecuteWithData: onCrossing is null."
        // the Polyline2Ds are mutable, so their point count may have changed since the KonturData was created:
        KonturData<'T>.checkData "KonturEngine.ExecuteWithData subject" subject.Kontur subject.Data
        KonturData<'T>.checkData "KonturEngine.ExecuteWithData clip" clip.Kontur clip.Data
        let results = e.Run (subject.Kontur, clip.Kontur, op, true)
        let data = Trace.mapData state results subject.Data clip.Data onCrossing
        KonturData<'T>.createUnchecked (Kontur.createUnchecked (results, FillRule.Positive), data)

    /// <summary>Resolves the self intersections and overlaps of one Kontur under its fill rule, like Simplify,
    /// and hands the values of its points on to the vertices of the result, see ExecuteWithData.</summary>
    /// <param name="shape">The Kontur with one value per point.</param>
    /// <param name="onCrossing">Returns the value of a new vertex from the two segments crossing there, the earlier one first.</param>
    /// <returns>A simple Kontur with fill rule Positive and one array of values per result contour.</returns>
    member e.SimplifyWithData (shape: KonturData<'T>, onCrossing: CrossedSegment<'T> -> CrossedSegment<'T> -> 'T) : KonturData<'T> =
        e.ExecuteWithData (shape, KonturData<'T>.createUnchecked (emptyClip, ResizeArray<'T[]> 0), ClipType.Union, onCrossing)

    /// <summary>Union of any number of Kontur values like UnionAll, and hands the values of their points on to the
    /// vertices of the result, see ExecuteWithData. Every Kontur is simplified on its own first, then all results
    /// are merged in one NonZero pass, so onCrossing may get segments of earlier results, carrying values it returned itself.</summary>
    /// <param name="shapes">The Kontur values with one value per point.</param>
    /// <param name="onCrossing">Returns the value of a new vertex from the two segments crossing there, the earlier one first.</param>
    /// <returns>A simple Kontur with fill rule Positive and one array of values per result contour.</returns>
    member e.UnionAllWithData (shapes: seq<KonturData<'T>>, onCrossing: CrossedSegment<'T> -> CrossedSegment<'T> -> 'T) : KonturData<'T> =
        if isNull (box shapes) then fail "KonturEngine.UnionAllWithData: shapes is null."
        let all = ResizeArray<Polyline2D> ()
        let allData = ResizeArray<'T[]> ()
        for shape in shapes do
            let simple = e.SimplifyWithData (shape, onCrossing)
            all.AddRange simple.Kontur.Paths
            allData.AddRange simple.Data
        e.SimplifyWithData (KonturData<'T>.createUnchecked (Kontur.createUnchecked (all, FillRule.NonZero), allData), onCrossing)
