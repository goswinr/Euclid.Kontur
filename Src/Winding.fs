namespace BoolOps

open System

/// Phase 6: the winding number of subject and clip on both sides of every graph edge.
/// This is the ray casting version: every edge casts a ray from its midpoint against the input segments.
/// It is simple and every edge is independent, so it serves as the oracle for the faster propagation
/// planned in DESIGN.md section 4.6.
module internal Winding =

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
