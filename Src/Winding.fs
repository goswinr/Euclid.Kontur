namespace BoolOps

open System

/// Phase 6: the winding number of subject and clip on both sides of every graph edge.
/// The primary method is propagation: one ray cast per connected component seeds the winding number of one
/// wedge at the component's leftmost vertex, and every other wedge and edge follows from the winding deltas,
/// which sum to zero around every vertex. So all edges of a component are consistent by construction.
/// The per edge ray casting method is kept as an oracle for tests and debugging.
module internal Winding =

    /// <summary>The exact winding numbers of subject and clip at the point (px, py), by counting the crossings
    /// of the ray from that point to +X with the input segments. The half open rule on Y
    /// (a segment counts if exactly one of its ends has Y at or below the ray) makes every crossing count once,
    /// also through vertices, and never counts horizontal segments. +1 for a segment going up, -1 going down.
    /// The result is left in RayS and RayC.</summary>
    let windingAt (s: EngineState) (px: float) (py: float) : unit =
        s.RayS <- 0
        s.RayC <- 0
        s.SegBvh.VisitInRect (px, py, Double.PositiveInfinity, py, fun seg ->
            let x0 = s.X s.SegA.[seg]
            let y0 = s.Y s.SegA.[seg]
            let x1 = s.X s.SegB.[seg]
            let y1 = s.Y s.SegB.[seg]
            if (y0 <= py) <> (y1 <= py) then
                let xc = x0 + (py - y0) * (x1 - x0) / (y1 - y0)
                if xc > px then
                    let d = if y1 > y0 then 1 else -1
                    if s.SegGroup.[seg] = 0 then s.RayS <- s.RayS + d
                    else                         s.RayC <- s.RayC + d)

    /// <summary>Assigns the winding numbers of all edges around vertex v, given the winding of one wedge.
    /// The wedge counter clockwise after the half edge at ring position startPos has the winding (wS, wC).
    /// Going counter clockwise across a half edge that leaves v in canonical direction (even id) adds the edge's
    /// delta, across one that leaves against the canonical direction (odd id) subtracts it.
    /// Then pushes the twins of all half edges of the ring onto the queue, for the vertices at their other ends.</summary>
    let private assignRing (s: EngineState) (v: int) (startPos: int) (wS: int) (wC: int) : unit =
        let first = s.VHalfStart.[v]
        let last = s.VHalfStart.[v + 1] - 1
        let mutable pos = startPos
        let mutable wS = wS
        let mutable wC = wC
        for _ = first to last do
            let nextPos = if pos = last then first else pos + 1
            let h = s.VHalf.[nextPos]
            let e = h >>> 1
            // the wedge clockwise of h has (wS, wC), crossing h counter clockwise:
            if h &&& 1 = 0 then
                wS <- wS + s.GDeltaS.[e]
                wC <- wC + s.GDeltaC.[e]
                s.WindLeftS.[e] <- wS // the counter clockwise side of a canonical half edge is the left side
                s.WindLeftC.[e] <- wC
            else
                s.WindLeftS.[e] <- wS // the clockwise side of a reversed half edge is the canonical left side
                s.WindLeftC.[e] <- wC
                wS <- wS - s.GDeltaS.[e]
                wC <- wC - s.GDeltaC.[e]
            s.Queue.[s.QueueEnd] <- h ^^^ 1
            s.QueueEnd <- s.QueueEnd + 1
            pos <- nextPos

    /// <summary>Computes the winding numbers on the left side of every graph edge by propagation.
    /// Vertices are visited in order of increasing X. The first unvisited vertex is the leftmost of its component,
    /// so every edge of the component leaves it towards +X or straight up or down. The winding of the wedge that
    /// contains the direction just above +X is found by one ray cast from a point a hair to the right of the vertex:
    /// segments through the vertex cross the ray at the vertex itself, left of the start, so they are excluded
    /// exactly, and every other segment is counted with the exact half open rule. The half open rule puts a point
    /// on a horizontal segment on that segment's upper side, which is why the seed wedge is the one above a
    /// horizontal edge leaving the vertex to the right, if there is one.
    /// From there a breadth first walk assigns every ring of the component. Each edge is assigned by the first of
    /// its two vertices to be processed, the second one produces the same value because the deltas sum to zero.</summary>
    let propagate (s: EngineState) : unit =
        let g = s.GCount
        let v = s.VertexCount
        s.WindLeftS <- Buffers.ensureInt s.WindLeftS 0 g
        s.WindLeftC <- Buffers.ensureInt s.WindLeftC 0 g
        s.VertOrder <- Buffers.ensureInt s.VertOrder 0 v
        s.VertDone  <- Buffers.ensureInt s.VertDone  0 v
        s.Queue     <- Buffers.ensureInt s.Queue     0 (2 * g)
        let order = s.VertOrder
        let vertDone = s.VertDone
        let queue = s.Queue
        let xy = s.XY
        for i = 0 to v - 1 do
            order.[i] <- i
            vertDone.[i] <- 0
        Buffers.sortIndices order 0 (v - 1) (fun a b -> xy.[2 * a] < xy.[2 * b])
        
        let mutable queueStart = 0
        s.QueueEnd <- 0
        for i = 0 to v - 1 do
            let seed = order.[i]
            if vertDone.[seed] = 0 && s.VHalfStart.[seed + 1] > s.VHalfStart.[seed] then
                // the leftmost vertex of a new component: all its edges point to +X or straight up or down
                vertDone.[seed] <- 1
                let sx = s.X seed
                let sy = s.Y seed
                let hair = 1e-9 * (1.0 + abs sx + abs sy)
                windingAt s (sx + hair) sy
                // the wedge containing the direction just above +X (pseudo angle 0) is the wedge after the last
                // half edge with pseudo angle 0.0 (an edge going exactly to +X), else the wedge after the last
                // half edge of the ring, whose angle is below 4.0 (cyclically just before 0):
                let first = s.VHalfStart.[seed]
                let last = s.VHalfStart.[seed + 1] - 1
                let mutable startPos = last
                let mutable k = first
                while k <= last && s.HalfAngle.[s.VHalf.[k]] = 0.0 do
                    startPos <- k
                    k <- k + 1
                queueStart <- s.QueueEnd
                assignRing s seed startPos s.RayS s.RayC
                while queueStart < s.QueueEnd do
                    let h = queue.[queueStart] // a half edge leaving a vertex u, whose edge is assigned already
                    queueStart <- queueStart + 1
                    let e = h >>> 1
                    let u = if h &&& 1 = 0 then s.GA.[e] else s.GB.[e]
                    if vertDone.[u] = 0 then
                        vertDone.[u] <- 1
                        let pos = s.RingPos.[h]
                        if h &&& 1 = 0 then
                            // canonical direction leaves u: the left side is the wedge counter clockwise after h
                            assignRing s u pos s.WindLeftS.[e] s.WindLeftC.[e]
                        else
                            // the reversed half edge leaves u: the canonical left side is the wedge clockwise before h,
                            // which is the wedge after the previous ring position
                            let uFirst = s.VHalfStart.[u]
                            let uLast = s.VHalfStart.[u + 1] - 1
                            let prev = if pos = uFirst then uLast else pos - 1
                            assignRing s u prev s.WindLeftS.[e] s.WindLeftC.[e]

    /// <summary>Casts a ray against the input segments from a point just right of the midpoint of the graph edge e,
    /// and stores the winding numbers of both groups on the left side of the edge.
    /// The ray starts twice the tolerance away from the edge on its right side, not on the edge itself: snapping a
    /// vertex onto a segment or merging vertices within tolerance bends a sub segment away from its parent by up
    /// to the tolerance, and a point on the sub segment could fall into that sliver, on the wrong side of the parent.
    /// From the offset point the ray goes to +X, counting every crossing with the exact half open rule on Y
    /// (+1 for a segment going up, -1 going down), which gives the exact winding number of the input at that point.
    /// The left side is then the right side plus the winding delta of the edge.</summary>
    let rayCastEdge (s: EngineState) (e: int) : unit =
        let a = s.GA.[e]
        let b = s.GB.[e]
        let ax = s.X a
        let ay = s.Y a
        let dx = s.X b - ax
        let dy = s.Y b - ay
        let mx = ax + dx * 0.5
        let my = ay + dy * 0.5
        let offset = 2.0 * s.Tolerance + 1e-12 * (1.0 + abs mx + abs my)
        let len = sqrt (dx * dx + dy * dy) // never zero, zero length edges were dropped
        // the right normal of the edge is (dy, -dx):
        let px = mx + dy / len * offset
        let py = my - dx / len * offset
        s.RayS <- 0
        s.RayC <- 0
        s.SegBvh.VisitInRect (px, py, Double.PositiveInfinity, py, fun seg ->
            let x0 = s.X s.SegA.[seg]
            let y0 = s.Y s.SegA.[seg]
            let x1 = s.X s.SegB.[seg]
            let y1 = s.Y s.SegB.[seg]
            if (y0 <= py) <> (y1 <= py) then
                let xc = x0 + (py - y0) * (x1 - x0) / (y1 - y0)
                if xc > px then
                    let d = if y1 > y0 then 1 else -1
                    if s.SegGroup.[seg] = 0 then s.RayS <- s.RayS + d
                    else                         s.RayC <- s.RayC + d)
        s.WindLeftS.[e] <- s.RayS + s.GDeltaS.[e]
        s.WindLeftC.[e] <- s.RayC + s.GDeltaC.[e]

    /// Computes the winding numbers on the left side of every graph edge by one ray cast per edge.
    let computeByRayCast (s: EngineState) : unit =
        let g = s.GCount
        s.WindLeftS <- Buffers.ensureInt s.WindLeftS 0 g
        s.WindLeftC <- Buffers.ensureInt s.WindLeftC 0 g
        for e = 0 to g - 1 do
            rayCastEdge s e
