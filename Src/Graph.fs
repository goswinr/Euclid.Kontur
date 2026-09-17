namespace BoolOps

open System
open Euclid.EuclidErrors

/// Phases 4 and 5: merges vertices within tolerance, builds the canonical graph edges with their winding deltas
/// and the counter clockwise sorted ring of half edges around every vertex.
module internal Graph =

    /// The representative of the cluster of v, with path halving.
    let find (parent: int[]) (v: int) : int =
        let mutable v = v
        while parent.[v] <> v do
            parent.[v] <- parent.[parent.[v]]
            v <- parent.[v]
        v

    /// Phase 4: all vertices closer than the tolerance are merged with union find.
    /// The representative of a cluster is its lowest vertex id, which prefers input vertices over intersection
    /// vertices because input vertices are stored first. So input coordinates survive into the result unchanged.
    let cluster (s: EngineState) : unit =
        let n = s.VertexCount
        s.Parent <- Buffers.ensureInt s.Parent 0 n
        let parent = s.Parent
        for i = 0 to n - 1 do
            parent.[i] <- i
        let tol = s.Tolerance
        let sqTol = tol * tol
        let bvh = s.VertBvh
        bvh.Reset n
        for i = 0 to n - 1 do
            let x = s.X i
            let y = s.Y i
            bvh.SetRect (i, x, y, x, y)
        bvh.Build ()
        bvh.VisitClosePairs (tol, fun i j ->
            let dx = s.X i - s.X j
            let dy = s.Y i - s.Y j
            if dx * dx + dy * dy <= sqTol then
                let ri = find parent i
                let rj = find parent j
                if ri < rj then parent.[rj] <- ri
                elif rj < ri then parent.[ri] <- rj)
        for i = 0 to n - 1 do
            parent.[i] <- find parent i

    /// Phase 5a: rewrites the sub segments to cluster representatives, puts them into canonical direction
    /// (lower vertex id first), sorts them by their vertex pair and merges coincident ones by summing their
    /// winding deltas. Sub segments that became zero length and merged edges with no winding change on either
    /// group are dropped.
    let buildEdges (s: EngineState) : unit =
        let n = s.ECount
        let parent = s.Parent
        let eFrom = s.EFrom
        let eTo = s.ETo
        s.EDir <- Buffers.ensureInt s.EDir 0 n
        let eDir = s.EDir
        for i = 0 to n - 1 do
            let a = parent.[eFrom.[i]]
            let b = parent.[eTo.[i]]
            if a <= b then
                eFrom.[i] <- a
                eTo.[i] <- b
                eDir.[i] <- 1
            else
                eFrom.[i] <- b
                eTo.[i] <- a
                eDir.[i] <- -1
        s.SortIdx <- Buffers.ensureInt s.SortIdx 0 n
        let idx = s.SortIdx
        for i = 0 to n - 1 do
            idx.[i] <- i
        Buffers.sortIndices idx 0 (n - 1) (fun i j -> eFrom.[i] < eFrom.[j] || (eFrom.[i] = eFrom.[j] && eTo.[i] < eTo.[j]))
        s.GA      <- Buffers.ensureInt s.GA      0 n
        s.GB      <- Buffers.ensureInt s.GB      0 n
        s.GDeltaS <- Buffers.ensureInt s.GDeltaS 0 n
        s.GDeltaC <- Buffers.ensureInt s.GDeltaC 0 n
        let mutable g = 0
        let mutable i = 0
        while i < n do
            let a = eFrom.[idx.[i]]
            let b = eTo.[idx.[i]]
            let mutable deltaS = 0
            let mutable deltaC = 0
            while i < n && eFrom.[idx.[i]] = a && eTo.[idx.[i]] = b do
                let k = idx.[i]
                if s.EGroup.[k] = 0 then deltaS <- deltaS + eDir.[k]
                else                     deltaC <- deltaC + eDir.[k]
                i <- i + 1
            if a <> b && (deltaS <> 0 || deltaC <> 0) then
                s.GA.[g] <- a
                s.GB.[g] <- b
                s.GDeltaS.[g] <- deltaS
                s.GDeltaC.[g] <- deltaC
                g <- g + 1
        s.GCount <- g

    /// A cheap monotonic stand-in for the angle of the direction (dx, dy): 0.0 at +X, 1.0 at +Y, 2.0 at -X, 3.0 at -Y,
    /// increasing counter clockwise up to 4.0. Only the order matters, so no trigonometry is needed.
    let inline pseudoAngle (dx: float) (dy: float) : float =
        let p = dx / (abs dx + abs dy)
        if dy >= 0.0 then 1.0 - p else 3.0 + p

    /// Phase 5b: builds the ring of half edges around every vertex, sorted counter clockwise by pseudo angle.
    /// Half edge 2e leaves GA.[e], half edge 2e+1 leaves GB.[e].
    let buildRings (s: EngineState) : unit =
        let v = s.VertexCount
        let g = s.GCount
        let h = 2 * g
        s.HalfAngle  <- Buffers.ensureFloat s.HalfAngle  0 h
        s.VHalfStart <- Buffers.ensureInt   s.VHalfStart 0 (v + 1)
        s.VHalf      <- Buffers.ensureInt   s.VHalf      0 h
        s.RingPos    <- Buffers.ensureInt   s.RingPos    0 h
        let halfAngle = s.HalfAngle
        let vStart = s.VHalfStart
        let vHalf = s.VHalf
        let ga = s.GA
        let gb = s.GB
        for e = 0 to g - 1 do
            let dx = s.X gb.[e] - s.X ga.[e]
            let dy = s.Y gb.[e] - s.Y ga.[e]
            halfAngle.[2 * e]     <- pseudoAngle dx dy
            halfAngle.[2 * e + 1] <- pseudoAngle -dx -dy
        // counting sort of the half edges by origin vertex:
        for i = 0 to v do
            vStart.[i] <- 0
        for e = 0 to g - 1 do
            vStart.[ga.[e] + 1] <- vStart.[ga.[e] + 1] + 1
            vStart.[gb.[e] + 1] <- vStart.[gb.[e] + 1] + 1
        for i = 1 to v do
            vStart.[i] <- vStart.[i] + vStart.[i - 1]
        for e = 0 to g - 1 do
            let a = ga.[e]
            vHalf.[vStart.[a]] <- 2 * e
            vStart.[a] <- vStart.[a] + 1
            let b = gb.[e]
            vHalf.[vStart.[b]] <- 2 * e + 1
            vStart.[b] <- vStart.[b] + 1
        for i = v downto 1 do
            vStart.[i] <- vStart.[i - 1]
        vStart.[0] <- 0
        // sort every ring counter clockwise:
        for i = 0 to v - 1 do
            let first = vStart.[i]
            let last = vStart.[i + 1] - 1
            if last > first then
                Buffers.sortIndices vHalf first last (fun h1 h2 -> halfAngle.[h1] < halfAngle.[h2])
        for pos = 0 to h - 1 do
            s.RingPos.[vHalf.[pos]] <- pos

    /// Checks the invariants of the graph, for tests and debugging. Fails with a message on the first violation.
    let validate (s: EngineState) : unit =
        let g = s.GCount
        for e = 0 to g - 1 do
            if s.GA.[e] >= s.GB.[e] then fail $"Graph.validate: edge {e} is not canonical, {s.GA.[e]} >= {s.GB.[e]}."
            if s.GDeltaS.[e] = 0 && s.GDeltaC.[e] = 0 then fail $"Graph.validate: edge {e} has no winding change."
            if e > 0 && s.GA.[e - 1] = s.GA.[e] && s.GB.[e - 1] = s.GB.[e] then fail $"Graph.validate: edges {e - 1} and {e} are duplicates."
        for v = 0 to s.VertexCount - 1 do
            // the winding deltas around a vertex sum to zero, because every path passing through it enters and leaves once:
            let mutable sumS = 0
            let mutable sumC = 0
            for pos = s.VHalfStart.[v] to s.VHalfStart.[v + 1] - 1 do
                let h = s.VHalf.[pos]
                let e = h >>> 1
                let sign = if h &&& 1 = 0 then 1 else -1
                sumS <- sumS + sign * s.GDeltaS.[e]
                sumC <- sumC + sign * s.GDeltaC.[e]
                if s.RingPos.[h] <> pos then fail $"Graph.validate: ring position of half edge {h} is wrong."
                if pos > s.VHalfStart.[v] && s.HalfAngle.[s.VHalf.[pos - 1]] > s.HalfAngle.[h] then fail $"Graph.validate: ring of vertex {v} is not sorted."
            if sumS <> 0 || sumC <> 0 then fail $"Graph.validate: winding deltas around vertex {v} sum to {sumS} and {sumC}, not zero."
