namespace Euclid

open System
open Euclid.EuclidErrors

/// Phases 4 and 5: merges vertices within tolerance, builds the canonical graph edges with their winding deltas
/// and the counter clockwise sorted ring of half edges around every vertex.
module internal Graph =

    /// The representative of the cluster of v, with path halving.
    let find (parent: int[]) (v: int) : int =
        let mutable v = v
        while Arr.get parent v <> v do
            Arr.set parent v (Arr.get parent (Arr.get parent v))
            v <- Arr.get parent v
        v

    /// Merges the clusters of two vertices if the vertices are closer than the tolerance.
    let inline private unionIfClose (s: EngineState) (parent: int[]) (sqTol: float) (u: int) (v: int) : unit =
        let dx = s.X u - s.X v
        let dy = s.Y u - s.Y v
        if dx * dx + dy * dy <= sqTol then
            let ru = find parent u
            let rv = find parent v
            if ru < rv then Arr.set parent rv ru
            elif rv < ru then Arr.set parent ru rv

    /// <summary>Phase 4: all vertices closer than the tolerance are merged with union find.
    /// The representative of a cluster is its lowest vertex id, which prefers input vertices over intersection
    /// vertices because input vertices are stored first. So input coordinates survive into the result unchanged.
    /// The pairs are found by a sweep instead of a tree: the plane is cut into columns twice the tolerance wide,
    /// so two vertices within tolerance are in the same or in adjacent columns. The vertices are sorted by column
    /// and then by Y, and every vertex is compared with the following vertices of its own column and with the
    /// vertices of the next column whose Y is within tolerance. The window into the next column only moves forward
    /// while the sweep advances, so the phase is linear after the sort, whatever the distribution of the vertices.
    /// Every pair within tolerance is found exactly once, the same pairs a tree query would report.</summary>
    let cluster (s: EngineState) : unit =
        let n = s.VertexCount
        s.Parent    <- Buffers.ensureInt   s.Parent    0 n
        s.CellCol   <- Buffers.ensureFloat s.CellCol   0 n
        s.VertOrder <- Buffers.ensureInt   s.VertOrder 0 n
        let parent = s.Parent
        let col = s.CellCol
        let order = s.VertOrder
        let tol = s.Tolerance
        let sqTol = tol * tol
        let xy = s.XY
        // A tolerance of zero merges only identical vertices, any column width works for that.
        // Columns twice the tolerance wide: the rounding of the division can move a vertex across one column
        // boundary, still two vertices within tolerance never end up more than one column apart.
        let invWidth = if tol > 0.0 then 0.5 / tol else 1.0
        for i = 0 to n - 1 do
            Arr.set parent i i
            Arr.set col i (floor (Arr.get xy (2 * i) * invWidth))
            Arr.set order i i
        Buffers.sortIndices order 0 (n - 1) (fun a b ->
            let ca = Arr.get col a
            let cb = Arr.get col b
            ca < cb || (ca = cb && Arr.get xy (2 * a + 1) < Arr.get xy (2 * b + 1)))
        let mutable j = 0 // the first position whose vertex is not before the window into the next column
        for i = 0 to n - 1 do
            let v = Arr.get order i
            let cv = Arr.get col v
            let yv = Arr.get xy (2 * v + 1)
            // the following vertices of the same column, up to tolerance above v:
            let mutable k = i + 1
            while k < n && (let u = Arr.get order k in Arr.get col u = cv && Arr.get xy (2 * u + 1) - yv <= tol) do
                unionIfClose s parent sqTol v (Arr.get order k)
                k <- k + 1
            // the vertices of the next column from tolerance below v to tolerance above v. The lower end of that
            // window never moves backwards while i advances, because the sort order is by column and then by Y:
            let cNext = cv + 1.0
            if j <= i then j <- i + 1
            while j < n && (let u = Arr.get order j in let cu = Arr.get col u in cu < cNext || (cu = cNext && Arr.get xy (2 * u + 1) < yv - tol)) do
                j <- j + 1
            k <- j
            while k < n && (let u = Arr.get order k in Arr.get col u = cNext && Arr.get xy (2 * u + 1) - yv <= tol) do
                unionIfClose s parent sqTol v (Arr.get order k)
                k <- k + 1
        for i = 0 to n - 1 do
            Arr.set parent i (find parent i)

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
            let a = Arr.get parent (Arr.get eFrom i)
            let b = Arr.get parent (Arr.get eTo i)
            if a <= b then
                Arr.set eFrom i a
                Arr.set eTo i b
                Arr.set eDir i 1
            else
                Arr.set eFrom i b
                Arr.set eTo i a
                Arr.set eDir i (-1)
        // sort by (lower vertex, higher vertex): a counting sort by the lower vertex into CSR ranges,
        // then each range, which holds the edges of one vertex, is sorted by the higher vertex:
        let v = s.VertexCount
        s.SortIdx <- Buffers.ensureInt s.SortIdx 0 n
        s.EdgeStart <- Buffers.ensureInt s.EdgeStart 0 (v + 1)
        let idx = s.SortIdx
        let start = s.EdgeStart
        for i = 0 to v do
            Arr.set start i 0
        for i = 0 to n - 1 do
            let a = Arr.get eFrom i + 1
            Arr.set start a (Arr.get start a + 1)
        for i = 1 to v do
            Arr.set start i (Arr.get start i + Arr.get start (i - 1))
        for i = 0 to n - 1 do
            let a = Arr.get eFrom i
            Arr.set idx (Arr.get start a) i
            Arr.set start a (Arr.get start a + 1)
        for i = v downto 1 do
            Arr.set start i (Arr.get start (i - 1))
        Arr.set start 0 0
        for a = 0 to v - 1 do
            let first = Arr.get start a
            let last = Arr.get start (a + 1) - 1
            if last > first then
                Buffers.sortIndices idx first last (fun i j -> Arr.get eTo i < Arr.get eTo j)
        s.GA      <- Buffers.ensureInt s.GA      0 n
        s.GB      <- Buffers.ensureInt s.GB      0 n
        s.GDeltaS <- Buffers.ensureInt s.GDeltaS 0 n
        s.GDeltaC <- Buffers.ensureInt s.GDeltaC 0 n
        let mutable g = 0
        let mutable i = 0
        while i < n do
            let a = Arr.get eFrom (Arr.get idx i)
            let b = Arr.get eTo (Arr.get idx i)
            let mutable deltaS = 0
            let mutable deltaC = 0
            while i < n && Arr.get eFrom (Arr.get idx i) = a && Arr.get eTo (Arr.get idx i) = b do
                let k = Arr.get idx i
                if Arr.get s.EGroup k = 0 then deltaS <- deltaS + Arr.get eDir k
                else                     deltaC <- deltaC + Arr.get eDir k
                i <- i + 1
            if a <> b && (deltaS <> 0 || deltaC <> 0) then
                Arr.set s.GA g a
                Arr.set s.GB g b
                Arr.set s.GDeltaS g deltaS
                Arr.set s.GDeltaC g deltaC
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
            let dx = s.X (Arr.get gb e) - s.X (Arr.get ga e)
            let dy = s.Y (Arr.get gb e) - s.Y (Arr.get ga e)
            Arr.set halfAngle (2 * e) (pseudoAngle dx dy)
            Arr.set halfAngle (2 * e + 1) (pseudoAngle -dx -dy)
        // counting sort of the half edges by origin vertex:
        for i = 0 to v do
            Arr.set vStart i 0
        for e = 0 to g - 1 do
            Arr.set vStart (Arr.get ga e + 1) (Arr.get vStart (Arr.get ga e + 1) + 1)
            Arr.set vStart (Arr.get gb e + 1) (Arr.get vStart (Arr.get gb e + 1) + 1)
        for i = 1 to v do
            Arr.set vStart i (Arr.get vStart i + Arr.get vStart (i - 1))
        for e = 0 to g - 1 do
            let a = Arr.get ga e
            Arr.set vHalf (Arr.get vStart a) (2 * e)
            Arr.set vStart a (Arr.get vStart a + 1)
            let b = Arr.get gb e
            Arr.set vHalf (Arr.get vStart b) (2 * e + 1)
            Arr.set vStart b (Arr.get vStart b + 1)
        for i = v downto 1 do
            Arr.set vStart i (Arr.get vStart (i - 1))
        Arr.set vStart 0 0
        // sort every ring counter clockwise:
        for i = 0 to v - 1 do
            let first = Arr.get vStart i
            let last = Arr.get vStart (i + 1) - 1
            if last > first then
                Buffers.sortIndices vHalf first last (fun h1 h2 -> Arr.get halfAngle h1 < Arr.get halfAngle h2)
        for pos = 0 to h - 1 do
            Arr.set s.RingPos (Arr.get vHalf pos) pos

    /// Checks the invariants of the graph, for tests and debugging. Fails with a message on the first violation.
    let validate (s: EngineState) : unit =
        let g = s.GCount
        for e = 0 to g - 1 do
            if Arr.get s.GA e >= Arr.get s.GB e then fail $"Graph.validate: edge {e} is not canonical, {Arr.get s.GA e} >= {Arr.get s.GB e}."
            if Arr.get s.GDeltaS e = 0 && Arr.get s.GDeltaC e = 0 then fail $"Graph.validate: edge {e} has no winding change."
            if e > 0 && Arr.get s.GA (e - 1) = Arr.get s.GA e && Arr.get s.GB (e - 1) = Arr.get s.GB e then fail $"Graph.validate: edges {e - 1} and {e} are duplicates."
        for v = 0 to s.VertexCount - 1 do
            // the winding deltas around a vertex sum to zero, because every path passing through it enters and leaves once:
            let mutable sumS = 0
            let mutable sumC = 0
            for pos = Arr.get s.VHalfStart v to (Arr.get s.VHalfStart (v + 1)) - 1 do
                let h = Arr.get s.VHalf pos
                let e = h >>> 1
                let sign = if h &&& 1 = 0 then 1 else -1
                sumS <- sumS + sign * Arr.get s.GDeltaS e
                sumC <- sumC + sign * Arr.get s.GDeltaC e
                if Arr.get s.RingPos h <> pos then fail $"Graph.validate: ring position of half edge {h} is wrong."
                if pos > Arr.get s.VHalfStart v && Arr.get s.HalfAngle (Arr.get s.VHalf (pos - 1)) > Arr.get s.HalfAngle h then fail $"Graph.validate: ring of vertex {v} is not sorted."
            if sumS <> 0 || sumC <> 0 then fail $"Graph.validate: winding deltas around vertex {v} sum to {sumS} and {sumC}, not zero."
