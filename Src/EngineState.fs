namespace Euclid

open System

/// <summary>All buffers of one boolean operation, owned by a KonturEngine and reused between operations.
/// Every phase of the pipeline reads and writes these flat arrays. Indices, not objects, refer to
/// vertices, segments, events and edges. Each array is valid only up to its count.
/// See DESIGN.md section 4 for the pipeline and the meaning of each buffer.</summary>
[<Sealed; NoEquality; NoComparison>]
type internal EngineState (tolerance: float) =

    /// Points closer than this are the same point, a point closer than this to a segment is on it.
    member val Tolerance : float = tolerance with get, set

    // ---------------------- phase 1, ingest: vertices, paths and input segments ----------------------

    /// Interleaved vertex coordinates x0 y0 x1 y1 ... Input vertices first, intersection vertices appended after them.
    member val XY : float[] = Array.zeroCreate 0 with get, set
    /// The count of vertices, input and intersection vertices together.
    member val VertexCount : int = 0 with get, set
    /// The count of input vertices. Vertex ids below this are input vertices, ids from here on are intersection points.
    member val InputVertexCount : int = 0 with get, set

    /// The first vertex id of each path, with one extra entry holding the total count. Length PathCount + 1.
    member val PathStart : int[] = Array.zeroCreate 0 with get, set
    /// The group of each path: 0 subject, 1 clip.
    member val PathGroup : int[] = Array.zeroCreate 0 with get, set
    /// The count of paths after ingest, paths with fewer than three distinct vertices are dropped.
    member val PathCount : int = 0 with get, set

    /// The start vertex id of each input segment.
    member val SegA : int[] = Array.zeroCreate 0 with get, set
    /// The end vertex id of each input segment. The last segment of a path ends at the path's first vertex.
    member val SegB : int[] = Array.zeroCreate 0 with get, set
    /// The group of each input segment: 0 subject, 1 clip.
    member val SegGroup : int[] = Array.zeroCreate 0 with get, set
    /// The count of input segments.
    member val SegCount : int = 0 with get, set

    /// The minimum X of each input segment's rectangle, expanded by the tolerance.
    member val SegMinX : float[] = Array.zeroCreate 0 with get, set
    /// The minimum Y of each input segment's rectangle, expanded by the tolerance.
    member val SegMinY : float[] = Array.zeroCreate 0 with get, set
    /// The maximum X of each input segment's rectangle, expanded by the tolerance.
    member val SegMaxX : float[] = Array.zeroCreate 0 with get, set
    /// The maximum Y of each input segment's rectangle, expanded by the tolerance.
    member val SegMaxY : float[] = Array.zeroCreate 0 with get, set
    /// Scratch: the segment ids sorted by the minimum X of their rectangle, for the sweep of the intersect phase.
    member val SegOrder : int[] = Array.zeroCreate 0 with get, set

    /// The tree over the input segments, rectangles expanded by the tolerance. Built only by the per edge
    /// ray casting oracle of the tests, the intersect phase itself sweeps the sorted rectangles.
    member val SegBvh : Bvh = Bvh 4 with get

    // ---------------------- phase 2, intersect: split events ----------------------

    /// The segment id of each split event.
    member val EvSeg : int[] = Array.zeroCreate 0 with get, set
    /// The parameter along the segment of each split event, between 0 and 1.
    member val EvT : float[] = Array.zeroCreate 0 with get, set
    /// The vertex id of each split event, an existing vertex or a new intersection vertex.
    member val EvVert : int[] = Array.zeroCreate 0 with get, set
    /// The count of split events.
    member val EvCount : int = 0 with get, set
    /// The events of segment s are EvOrder.[EvStart.[s] .. EvStart.[s+1] - 1], sorted by parameter. Length SegCount + 1.
    member val EvStart : int[] = Array.zeroCreate 0 with get, set
    /// Event ids grouped by segment, see EvStart.
    member val EvOrder : int[] = Array.zeroCreate 0 with get, set

    // ---------------------- phase 3, split: sub segments before clustering ----------------------

    /// The start vertex of each sub segment, in path direction.
    member val EFrom : int[] = Array.zeroCreate 0 with get, set
    /// The end vertex of each sub segment, in path direction.
    member val ETo : int[] = Array.zeroCreate 0 with get, set
    /// The group of each sub segment: 0 subject, 1 clip.
    member val EGroup : int[] = Array.zeroCreate 0 with get, set
    /// Per sub segment after clustering: 1 if the path direction runs from the lower to the higher vertex id, -1 otherwise.
    member val EDir : int[] = Array.zeroCreate 0 with get, set
    /// The count of sub segments.
    member val ECount : int = 0 with get, set

    // ---------------------- phase 4, cluster: vertices within tolerance ----------------------

    /// Scratch: the grid column of each vertex, floor of X over twice the tolerance, for the sweep of the cluster phase.
    member val CellCol : float[] = Array.zeroCreate 0 with get, set
    /// Union find parent of each vertex. After the cluster phase Parent.[v] is the representative of v's cluster.
    member val Parent : int[] = Array.zeroCreate 0 with get, set

    // ---------------------- phase 5, graph: merged edges and the angular rings ----------------------

    /// The lower vertex id of each graph edge.
    member val GA : int[] = Array.zeroCreate 0 with get, set
    /// The higher vertex id of each graph edge.
    member val GB : int[] = Array.zeroCreate 0 with get, set
    /// The change of the subject winding number when crossing the edge from its right side to its left side, looking from GA to GB.
    member val GDeltaS : int[] = Array.zeroCreate 0 with get, set
    /// The change of the clip winding number when crossing the edge from its right side to its left side, looking from GA to GB.
    member val GDeltaC : int[] = Array.zeroCreate 0 with get, set
    /// The count of graph edges.
    member val GCount : int = 0 with get, set
    /// Scratch permutation for sorting sub segments and events.
    member val SortIdx : int[] = Array.zeroCreate 0 with get, set
    /// Scratch: the first position in SortIdx of the sub segments of each lower vertex id, in the counting sort of phase 5a. Length VertexCount + 1.
    member val EdgeStart : int[] = Array.zeroCreate 0 with get, set

    /// The pseudo angle of each half edge. Half edge 2e leaves GA.[e] towards GB.[e], half edge 2e+1 leaves GB.[e] towards GA.[e].
    member val HalfAngle : float[] = Array.zeroCreate 0 with get, set
    /// The half edges leaving vertex v are VHalf.[VHalfStart.[v] .. VHalfStart.[v+1] - 1], sorted counter clockwise. Length VertexCount + 1.
    member val VHalfStart : int[] = Array.zeroCreate 0 with get, set
    /// Half edge ids grouped by their origin vertex, see VHalfStart.
    member val VHalf : int[] = Array.zeroCreate 0 with get, set
    /// The position of each half edge in VHalf.
    member val RingPos : int[] = Array.zeroCreate 0 with get, set

    // ---------------------- phase 6, winding ----------------------

    /// The tree over the graph edges, for the seed ray casts. Built only when the graph has more than one component.
    member val EdgeBvh : Bvh = Bvh 4 with get

    /// Scratch: the subject winding number accumulated by the current ray cast.
    member val RayS : int = 0 with get, set
    /// Scratch: the clip winding number accumulated by the current ray cast.
    member val RayC : int = 0 with get, set

    /// Scratch: the vertex ids sorted by grid column and Y, for the sweep of the cluster phase.
    member val VertOrder : int[] = Array.zeroCreate 0 with get, set
    /// Scratch: 1 for every vertex whose ring has been assigned winding numbers.
    member val VertDone : int[] = Array.zeroCreate 0 with get, set
    /// Scratch: the queue of half edges through which the propagation reaches the next vertices.
    member val Queue : int[] = Array.zeroCreate 0 with get, set
    /// Scratch: the count of half edges pushed onto the queue.
    member val QueueEnd : int = 0 with get, set

    /// The subject winding number on the left side of each graph edge, looking from GA to GB. The right side is left minus GDeltaS.
    member val WindLeftS : int[] = Array.zeroCreate 0 with get, set
    /// The clip winding number on the left side of each graph edge, looking from GA to GB. The right side is left minus GDeltaC.
    member val WindLeftC : int[] = Array.zeroCreate 0 with get, set

    // ---------------------- phases 7 and 8, select and link ----------------------

    /// Per graph edge: 0 not part of the result, 1 part of the result directed GA to GB, -1 directed GB to GA. The inside is on the left.
    member val EdgeOut : int[] = Array.zeroCreate 0 with get, set
    /// Per half edge of a result edge: the half edge that follows it in its contour. -1 if not part of the result.
    member val Next : int[] = Array.zeroCreate 0 with get, set

    /// Forgets all content but keeps the arrays.
    member s.Clear () : unit =
        s.VertexCount <- 0
        s.InputVertexCount <- 0
        s.PathCount <- 0
        s.SegCount <- 0
        s.EvCount <- 0
        s.ECount <- 0
        s.GCount <- 0

    // ---------------------- vertex helpers ----------------------

    /// Makes room for at least the given count of vertices, keeping the existing ones.
    member s.EnsureVertices (count: int) : unit =
        s.XY <- Buffers.ensureFloat s.XY (2 * s.VertexCount) (2 * count)

    /// Appends a vertex and returns its id.
    member s.AddVertex (x: float, y: float) : int =
        let i = s.VertexCount
        if 2 * i + 2 > s.XY.Length then s.EnsureVertices (i + 1)
        Arr.set s.XY (2 * i) x
        Arr.set s.XY (2 * i + 1) y
        s.VertexCount <- i + 1
        i

    /// The X coordinate of a vertex.
    member inline s.X (v: int) : float = Arr.get s.XY (2 * v)

    /// The Y coordinate of a vertex.
    member inline s.Y (v: int) : float = Arr.get s.XY (2 * v + 1)

    /// Makes room for at least the given count of split events, keeping the existing ones.
    member s.EnsureEvents (count: int) : unit =
        let i = s.EvCount
        s.EvSeg  <- Buffers.ensureInt   s.EvSeg  i count
        s.EvT    <- Buffers.ensureFloat s.EvT    i count
        s.EvVert <- Buffers.ensureInt   s.EvVert i count

    /// Appends a split event.
    member s.AddEvent (seg: int, t: float, vert: int) : unit =
        let i = s.EvCount
        if i >= s.EvSeg.Length then s.EnsureEvents (i + 1) // the three arrays always have the same length
        Arr.set s.EvSeg i seg
        Arr.set s.EvT i t
        Arr.set s.EvVert i vert
        s.EvCount <- i + 1

    /// Makes room for at least the given count of sub segments, keeping the existing ones.
    member s.EnsureSubSegments (count: int) : unit =
        let i = s.ECount
        s.EFrom  <- Buffers.ensureInt s.EFrom  i count
        s.ETo    <- Buffers.ensureInt s.ETo    i count
        s.EGroup <- Buffers.ensureInt s.EGroup i count

    /// Appends a sub segment. The caller must have made room with EnsureSubSegments.
    member s.AddSubSegment (from: int, to': int, group: int) : unit =
        let i = s.ECount
        Arr.set s.EFrom i from
        Arr.set s.ETo i to'
        Arr.set s.EGroup i group
        s.ECount <- i + 1
