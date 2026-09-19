namespace Euclid

open System

/// Phases 2 and 3: finds all intersections between input segments with the segment tree and records them as
/// split events, then cuts every segment at its events into sub segments.
module internal Intersect =

    /// The parameter of the point closest to (px, py) on the segment (ax, ay) to (bx, by), clamped to 0..1.
    /// Returns 0.0 for a zero length segment.
    let inline closestParam (ax: float) (ay: float) (bx: float) (by: float) (px: float) (py: float) : float =
        let dx = bx - ax
        let dy = by - ay
        let lenSq = dx * dx + dy * dy
        if lenSq <= 0.0 then 0.0
        else
            let t = ((px - ax) * dx + (py - ay) * dy) / lenSq
            if t < 0.0 then 0.0 elif t > 1.0 then 1.0 else t

    /// The squared distance between the point (px, py) and the point at parameter t on the segment (ax, ay) to (bx, by).
    let inline sqDistAt (ax: float) (ay: float) (bx: float) (by: float) (t: float) (px: float) (py: float) : float =
        let cx = ax + (bx - ax) * t - px
        let cy = ay + (by - ay) * t - py
        cx * cx + cy * cy

    /// The squared distance between two vertices.
    let inline sqDistVerts (s: EngineState) (u: int) (v: int) : float =
        let dx = s.X u - s.X v
        let dy = s.Y u - s.Y v
        dx * dx + dy * dy

    /// Phase 2a: the rectangle of every input segment, expanded by the tolerance, and the segment ids sorted
    /// by the minimum X of their rectangle.
    let sortRects (s: EngineState) : unit =
        let n = s.SegCount
        let tol = s.Tolerance
        s.SegMinX  <- Buffers.ensureFloat s.SegMinX  0 n
        s.SegMinY  <- Buffers.ensureFloat s.SegMinY  0 n
        s.SegMaxX  <- Buffers.ensureFloat s.SegMaxX  0 n
        s.SegMaxY  <- Buffers.ensureFloat s.SegMaxY  0 n
        s.SegOrder <- Buffers.ensureInt   s.SegOrder 0 n
        let minX = s.SegMinX
        let minY = s.SegMinY
        let maxX = s.SegMaxX
        let maxY = s.SegMaxY
        let order = s.SegOrder
        for i = 0 to n - 1 do
            let ax = s.X (Arr.get s.SegA i)
            let ay = s.Y (Arr.get s.SegA i)
            let bx = s.X (Arr.get s.SegB i)
            let by = s.Y (Arr.get s.SegB i)
            Arr.set minX i ((if ax < bx then ax else bx) - tol)
            Arr.set maxX i ((if ax < bx then bx else ax) + tol)
            Arr.set minY i ((if ay < by then ay else by) - tol)
            Arr.set maxY i ((if ay < by then by else ay) + tol)
            Arr.set order i i
        Buffers.sortIndices order 0 (n - 1) (fun a b -> Arr.get minX a < Arr.get minX b)

    /// Builds the tree over the input segments from the rectangles of sortRects.
    /// Only the per edge ray casting oracle of the tests needs it.
    let buildTree (s: EngineState) : unit =
        let bvh = s.SegBvh
        let n = s.SegCount
        bvh.Reset n
        for i = 0 to n - 1 do
            bvh.SetRect (i, Arr.get s.SegMinX i, Arr.get s.SegMinY i, Arr.get s.SegMaxX i, Arr.get s.SegMaxY i)
        bvh.Build ()

    /// Records a split event on the segment seg from (ax, ay) to (bx, by) if the vertex c at (cx, cy) lies within
    /// tolerance of the segment. The caller has checked that c is farther than the tolerance from both ends,
    /// a vertex within tolerance of an end is merged with that end by the cluster phase instead.
    let inline private endpointOnSegment (s: EngineState) (sqTol: float) (seg: int) (ax: float) (ay: float) (bx: float) (by: float) (c: int) (cx: float) (cy: float) : unit =
        let t = closestParam ax ay bx by cx cy
        if sqDistAt ax ay bx by t cx cy <= sqTol then
            s.AddEvent (seg, t, c)

    /// The squared distance between two points.
    let inline private sqDist (ax: float) (ay: float) (bx: float) (by: float) : float =
        let dx = ax - bx
        let dy = ay - by
        dx * dx + dy * dy

    /// <summary>Classifies one pair of segments, p from a to b and q from c to d, and records its split events.
    /// Endpoints on the other segment come first, so that T junctions and collinear overlaps reuse existing vertices.
    /// A proper crossing farther than the tolerance from all four ends creates one new vertex shared by both segments.
    /// The eight coordinates are read once. Three cross products give the signed area of every end against the
    /// line of the other segment: an end can only lie within tolerance of the other segment if it lies within
    /// tolerance of that segment's line. For most pairs no end does, and then only a proper crossing is possible,
    /// whose point is farther than the tolerance from all four ends by the same argument. That is the fast path.</summary>
    let private intersectPair (s: EngineState) (sqTol: float) (p: int) (q: int) : unit =
        let a = Arr.get s.SegA p
        let b = Arr.get s.SegB p
        let c = Arr.get s.SegA q
        let d = Arr.get s.SegB q
        let ax = s.X a
        let ay = s.Y a
        let bx = s.X b
        let by = s.Y b
        let cx = s.X c
        let cy = s.Y c
        let dx = s.X d
        let dy = s.Y d
        let vAx = bx - ax
        let vAy = by - ay
        let vBx = dx - cx
        let vBy = dy - cy
        let wx = cx - ax
        let wy = cy - ay
        let det = vAx * vBy - vAy * vBx // twice the signed area of the parallelogram of the two directions
        let sc = vAx * wy - vAy * wx    // c against the line of p, times the length of p
        let sd = sc + det               // d against the line of p
        let sa = wx * vBy - wy * vBx    // a against the line of q, times the length of q
        let sb = sa - det               // b against the line of q
        let lineTolA = sqTol * (vAx * vAx + vAy * vAy)
        let lineTolB = sqTol * (vBx * vBx + vBy * vBy)
        if sc * sc > lineTolA && sd * sd > lineTolA && sa * sa > lineTolB && sb * sb > lineTolB then
            // no end within tolerance of the other line, so none within tolerance of the other segment:
            if det <> 0.0 then // parallel segments never cross properly
                let t = sa / det
                let u = -sc / det
                if t >= 0.0 && t <= 1.0 && u >= 0.0 && u <= 1.0 then
                    let v = s.AddVertex (ax + vAx * t, ay + vAy * t)
                    s.AddEvent (p, t, v)
                    s.AddEvent (q, u, v)
        else
            // which ends are within tolerance of an end of the other segment (shared ends included):
            let ac = a = c || sqDist ax ay cx cy <= sqTol
            let ad = a = d || sqDist ax ay dx dy <= sqTol
            let bc = b = c || sqDist bx by cx cy <= sqTol
            let bd = b = d || sqDist bx by dx dy <= sqTol
            if sc * sc <= lineTolA && not ac && not bc then endpointOnSegment s sqTol p ax ay bx by c cx cy
            if sd * sd <= lineTolA && not ad && not bd then endpointOnSegment s sqTol p ax ay bx by d dx dy
            if sa * sa <= lineTolB && not ac && not ad then endpointOnSegment s sqTol q cx cy dx dy a ax ay
            if sb * sb <= lineTolB && not bc && not bd then endpointOnSegment s sqTol q cx cy dx dy b bx by
            if det <> 0.0 then // parallel segments never cross properly, their overlaps are handled above
                let t = sa / det
                let u = -sc / det
                if t >= 0.0 && t <= 1.0 && u >= 0.0 && u <= 1.0 then
                    let x = ax + vAx * t
                    let y = ay + vAy * t
                    if sqDist ax ay x y > sqTol && sqDist bx by x y > sqTol && sqDist cx cy x y > sqTol && sqDist dx dy x y > sqTol then
                        let v = s.AddVertex (x, y)
                        s.AddEvent (p, t, v)
                        s.AddEvent (q, u, v)

    /// <summary>Phase 2b: visits every pair of segments whose expanded rectangles overlap and records the split events.
    /// A sweep over the segments sorted by the minimum X of their rectangles: every segment is paired with the
    /// following ones as long as their rectangles start before its own ends, and the pair is kept if the
    /// rectangles overlap in Y too. Each unordered pair is visited once, with the smaller id first, which keeps
    /// the crossing point computed from the same segment whatever the sort order.</summary>
    let findIntersections (s: EngineState) : unit =
        s.EvCount <- 0
        s.EnsureEvents s.SegCount // a guess at the count of events, so that a fresh engine does not grow in tiny steps
        let sqTol = s.Tolerance * s.Tolerance
        let n = s.SegCount
        let minX = s.SegMinX
        let minY = s.SegMinY
        let maxX = s.SegMaxX
        let maxY = s.SegMaxY
        let order = s.SegOrder
        for i = 0 to n - 2 do
            let a = Arr.get order i
            let aMaxX = Arr.get maxX a
            let aMinY = Arr.get minY a
            let aMaxY = Arr.get maxY a
            let mutable j = i + 1
            let mutable go = true
            while go && j < n do
                let b = Arr.get order j
                if Arr.get minX b > aMaxX then
                    go <- false // every later rectangle starts even farther right
                else
                    if Arr.get minY b <= aMaxY && aMinY <= Arr.get maxY b then
                        if a < b then intersectPair s sqTol a b else intersectPair s sqTol b a
                    j <- j + 1

    /// Phase 3: groups the events by segment, sorts them by parameter and emits the sub segments.
    let splitSegments (s: EngineState) : unit =
        let segCount = s.SegCount
        let evCount = s.EvCount
        s.EvStart <- Buffers.ensureInt s.EvStart 0 (segCount + 1)
        s.EvOrder <- Buffers.ensureInt s.EvOrder 0 evCount
        let evStart = s.EvStart
        let evOrder = s.EvOrder
        let evSeg = s.EvSeg
        let evT = s.EvT
        // counting sort of the events by segment:
        for i = 0 to segCount do
            Arr.set evStart i 0
        for e = 0 to evCount - 1 do
            Arr.set evStart (Arr.get evSeg e + 1) (Arr.get evStart (Arr.get evSeg e + 1) + 1)
        for i = 1 to segCount do
            Arr.set evStart i (Arr.get evStart i + Arr.get evStart (i - 1))
        for e = 0 to evCount - 1 do
            let seg = Arr.get evSeg e
            Arr.set evOrder (Arr.get evStart seg) e
            Arr.set evStart seg (Arr.get evStart seg + 1)
        for i = segCount downto 1 do
            Arr.set evStart i (Arr.get evStart (i - 1))
        Arr.set evStart 0 0
        // sort the events of each segment by parameter and emit the sub segments.
        // A segment with k events yields at most k + 1 sub segments, so this is room enough:
        s.ECount <- 0
        s.EnsureSubSegments (segCount + evCount)
        for seg = 0 to segCount - 1 do
            let first = Arr.get evStart seg
            let last = Arr.get evStart (seg + 1) - 1
            if last > first then
                Buffers.sortIndices evOrder first last (fun e1 e2 -> Arr.get evT e1 < Arr.get evT e2)
            let group = Arr.get s.SegGroup seg
            let mutable prev = Arr.get s.SegA seg
            for i = first to last do
                let v = Arr.get s.EvVert (Arr.get evOrder i)
                if v <> prev then
                    s.AddSubSegment (prev, v, group)
                    prev <- v
            let b = Arr.get s.SegB seg
            if b <> prev then
                s.AddSubSegment (prev, b, group)
