namespace BoolOps

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

    /// Builds the tree over the input segments, each rectangle expanded by the tolerance.
    let buildTree (s: EngineState) : unit =
        let bvh = s.SegBvh
        let tol = s.Tolerance
        bvh.Reset s.SegCount
        for i = 0 to s.SegCount - 1 do
            let ax = s.X s.SegA.[i]
            let ay = s.Y s.SegA.[i]
            let bx = s.X s.SegB.[i]
            let by = s.Y s.SegB.[i]
            bvh.SetRect (i, (min ax bx) - tol, (min ay by) - tol, (max ax bx) + tol, (max ay by) + tol)
        bvh.Build ()

    /// Records a split event on the segment seg (from vertex a to b) if the vertex c lies within tolerance of it
    /// and farther than the tolerance from both of its ends. A vertex within tolerance of an end is merged
    /// with that end by the cluster phase instead.
    let private endpointOnSegment (s: EngineState) (sqTol: float) (seg: int) (a: int) (b: int) (c: int) : unit =
        if c <> a && c <> b && sqDistVerts s c a > sqTol && sqDistVerts s c b > sqTol then
            let ax = s.X a
            let ay = s.Y a
            let bx = s.X b
            let by = s.Y b
            let cx = s.X c
            let cy = s.Y c
            let t = closestParam ax ay bx by cx cy
            if sqDistAt ax ay bx by t cx cy <= sqTol then
                s.AddEvent (seg, t, c)

    /// Classifies one pair of segments and records its split events.
    /// Endpoints on the other segment come first, so that T junctions and collinear overlaps reuse existing vertices.
    /// A proper crossing farther than the tolerance from all four ends creates one new vertex shared by both segments.
    let private intersectPair (s: EngineState) (sqTol: float) (p: int) (q: int) : unit =
        let a = s.SegA.[p]
        let b = s.SegB.[p]
        let c = s.SegA.[q]
        let d = s.SegB.[q]
        endpointOnSegment s sqTol p a b c
        endpointOnSegment s sqTol p a b d
        endpointOnSegment s sqTol q c d a
        endpointOnSegment s sqTol q c d b
        let ax = s.X a
        let ay = s.Y a
        let vAx = s.X b - ax
        let vAy = s.Y b - ay
        let cx = s.X c
        let cy = s.Y c
        let vBx = s.X d - cx
        let vBy = s.Y d - cy
        let det = vAx * vBy - vAy * vBx
        if det <> 0.0 then // parallel segments never cross properly, their overlaps are handled above
            let dx = cx - ax
            let dy = cy - ay
            let t = (dx * vBy - dy * vBx) / det
            let u = (dx * vAy - dy * vAx) / det
            if t >= 0.0 && t <= 1.0 && u >= 0.0 && u <= 1.0 then
                let x = ax + vAx * t
                let y = ay + vAy * t
                let inline sqDistTo (v: int) =
                    let ex = s.X v - x
                    let ey = s.Y v - y
                    ex * ex + ey * ey
                if sqDistTo a > sqTol && sqDistTo b > sqTol && sqDistTo c > sqTol && sqDistTo d > sqTol then
                    let v = s.AddVertex (x, y)
                    s.AddEvent (p, t, v)
                    s.AddEvent (q, u, v)

    /// Phase 2: visits every pair of segments whose expanded rectangles overlap and records the split events.
    let findIntersections (s: EngineState) : unit =
        s.EvCount <- 0
        let sqTol = s.Tolerance * s.Tolerance
        s.SegBvh.VisitClosePairs (0.0, fun p q -> intersectPair s sqTol p q)

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
            evStart.[i] <- 0
        for e = 0 to evCount - 1 do
            evStart.[evSeg.[e] + 1] <- evStart.[evSeg.[e] + 1] + 1
        for i = 1 to segCount do
            evStart.[i] <- evStart.[i] + evStart.[i - 1]
        for e = 0 to evCount - 1 do
            let seg = evSeg.[e]
            evOrder.[evStart.[seg]] <- e
            evStart.[seg] <- evStart.[seg] + 1
        for i = segCount downto 1 do
            evStart.[i] <- evStart.[i - 1]
        evStart.[0] <- 0
        // sort the events of each segment by parameter and emit the sub segments:
        s.ECount <- 0
        for seg = 0 to segCount - 1 do
            let first = evStart.[seg]
            let last = evStart.[seg + 1] - 1
            if last > first then
                Buffers.sortIndices evOrder first last (fun e1 e2 -> evT.[e1] < evT.[e2])
            let group = s.SegGroup.[seg]
            let mutable prev = s.SegA.[seg]
            for i = first to last do
                let v = s.EvVert.[evOrder.[i]]
                if v <> prev then
                    s.AddSubSegment (prev, v, group)
                    prev <- v
            let b = s.SegB.[seg]
            if b <> prev then
                s.AddSubSegment (prev, b, group)
