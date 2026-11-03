namespace Euclid

open System
open Euclid.EuclidErrors

/// Phases 4 and 5: merges vertices within tolerance, builds the canonical graph edges with their winding deltas
/// and the counter clockwise sorted ring of half edges around every vertex. Phase 8b: repairs the planarity of
/// the graph when the result edges do not link.
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

    /// The rounds Graph.repair reroutes edges, before it merges the vertices that are still in conflict.
    [<Literal>]
    let RerouteRounds = 4

    /// The most rounds Graph.repair runs. Every round after RerouteRounds merges at least two clusters,
    /// so this only guards against an endless loop, see DESIGN.md section 4.5.1.
    [<Literal>]
    let MaxRepairRounds = 64

    /// Records a reroute of the graph edge e through the vertex w if w lies within tolerance of the inside of the edge.
    /// The ends of the edge are farther than the tolerance from w, they would be in w's cluster otherwise.
    let private rerouteIfClose (s: EngineState) (sqTol: float) (e: int) (w: int) : unit =
        let a = Arr.get s.GA e
        let b = Arr.get s.GB e
        if w <> a && w <> b then
            let ax = s.X a
            let ay = s.Y a
            let bx = s.X b
            let by = s.Y b
            let px = s.X w
            let py = s.Y w
            let t = Intersect.closestParam ax ay bx by px py
            if t > 0.0 && t < 1.0 && Intersect.sqDistAt ax ay bx by t px py <= sqTol then
                s.AddFix (e, t, w)

    /// Appends the sub segments of one piece of a rerouted graph edge, from u to v, carrying the edge's
    /// winding deltas as one sub segment per unit of delta, so that buildEdges can merge them as usual.
    let private addPiece (s: EngineState) (u: int) (v: int) (deltaS: int) (deltaC: int) : unit =
        for _ = 1 to abs deltaS do
            if deltaS > 0 then s.AddSubSegment (u, v, 0) else s.AddSubSegment (v, u, 0)
        for _ = 1 to abs deltaC do
            if deltaC > 0 then s.AddSubSegment (u, v, 1) else s.AddSubSegment (v, u, 1)

    /// Cuts every graph edge at its reroutes, sorted by parameter, and builds the graph edges again from the pieces.
    let private applyReroutes (s: EngineState) : unit =
        let g = s.GCount
        let fixCount = s.FixCount
        s.FixStart <- Buffers.ensureInt s.FixStart 0 (g + 1)
        s.FixOrder <- Buffers.ensureInt s.FixOrder 0 fixCount
        let start = s.FixStart
        let order = s.FixOrder
        // counting sort of the reroutes by graph edge:
        for e = 0 to g do
            Arr.set start e 0
        for k = 0 to fixCount - 1 do
            let e = Arr.get s.FixEdge k + 1
            Arr.set start e (Arr.get start e + 1)
        for e = 1 to g do
            Arr.set start e (Arr.get start e + Arr.get start (e - 1))
        for k = 0 to fixCount - 1 do
            let e = Arr.get s.FixEdge k
            Arr.set order (Arr.get start e) k
            Arr.set start e (Arr.get start e + 1)
        for e = g downto 1 do
            Arr.set start e (Arr.get start (e - 1))
        Arr.set start 0 0
        // room for every piece of every edge, one sub segment per unit of winding delta:
        let mutable total = 0
        for e = 0 to g - 1 do
            let pieces = Arr.get start (e + 1) - Arr.get start e + 1
            total <- total + pieces * (abs (Arr.get s.GDeltaS e) + abs (Arr.get s.GDeltaC e))
        s.ECount <- 0
        s.EnsureSubSegments total
        let fixT = s.FixT
        for e = 0 to g - 1 do
            let first = Arr.get start e
            let last = Arr.get start (e + 1) - 1
            if last > first then
                Buffers.sortIndices order first last (fun k1 k2 -> Arr.get fixT k1 < Arr.get fixT k2)
            let deltaS = Arr.get s.GDeltaS e
            let deltaC = Arr.get s.GDeltaC e
            let mutable prev = Arr.get s.GA e
            for i = first to last do
                let w = Arr.get s.FixVert (Arr.get order i)
                if w <> prev then
                    addPiece s prev w deltaS deltaC
                    prev <- w
            let b = Arr.get s.GB e
            if b <> prev then addPiece s prev b deltaS deltaC
        buildEdges s // the ends are representatives already, so the Parent lookup in there changes nothing

    /// TRUE if the split event ev puts an input vertex onto a segment it does not lie on exactly: a T junction or
    /// the end of a collinear overlap within tolerance. The sub segments of that segment then bend towards the vertex.
    /// Crossing vertices are computed on their segments, they bend them by rounding only.
    let private bendsSegment (s: EngineState) (ev: int) : bool =
        let v = Arr.get s.EvVert ev
        if v >= s.InputVertexCount then false
        else
            let seg = Arr.get s.EvSeg ev
            let a = Arr.get s.SegA seg
            let b = Arr.get s.SegB seg
            Intersect.sqDistAt (s.X a) (s.Y a) (s.X b) (s.Y b) (Arr.get s.EvT ev) (s.X v) (s.Y v) > 0.0

    /// <summary>Phase 8b: makes the graph planar again where merging or snapping moved edges.
    /// Merging a vertex into the representative of its cluster moves the ends of its sub segments by up to the
    /// tolerance, and splitting a segment at an input vertex within tolerance of it bends its sub segments by as much.
    /// So an edge can come to pass through another vertex or cross another edge without sharing a vertex, and the
    /// winding numbers would no longer agree around the vertices. The repair applies the T junction rule of the
    /// intersect phase to the graph, around every hot vertex: a representative that took in a vertex at another
    /// position, or that a segment was bent to. Every edge within tolerance of a hot vertex is rerouted through it,
    /// and every edge leaving a hot vertex is rerouted through every other vertex within tolerance of it.
    /// A reroute bends an edge by up to the tolerance, so the next round checks around the vertices that were used,
    /// until a round finds nothing. Only existing vertices are used, the repair never creates one.
    /// A few vertices that lie within tolerance of each other's edges, but not of each other, can make the reroutes
    /// undo each other: four vertices around the crossing of two edges, each within tolerance of the other edge.
    /// So from round RerouteRounds + 1 on, every vertex an edge would be rerouted through is merged into the nearer
    /// end of that edge instead. Every such round merges at least two clusters, which ends the repair.
    /// The engine runs this phase only when Link.tryLink failed, then builds the rings, the winding numbers and the
    /// selection again and links once more, see DESIGN.md section 4.5.1.</summary>
    let repair (s: EngineState) : unit =
        let n = s.VertexCount
        let parent = s.Parent
        s.RepairRounds <- 0
        let mutable moved = false
        let mutable v = 0
        while not moved && v < n do
            let r = Arr.get parent v
            if r <> v && (s.X v <> s.X r || s.Y v <> s.Y r) then moved <- true
            v <- v + 1
        let mutable ev = 0
        while not moved && ev < s.EvCount do
            if bendsSegment s ev then moved <- true
            ev <- ev + 1
        if moved then
            s.Hot      <- Buffers.ensureInt s.Hot      0 n
            s.Used     <- Buffers.ensureInt s.Used     0 n
            s.VertItem <- Buffers.ensureInt s.VertItem 0 n
            s.HotItem  <- Buffers.ensureInt s.HotItem  0 n
            let hot = s.Hot
            let used = s.Used
            for v = 0 to n - 1 do
                Arr.set hot v 0
                Arr.set used v 0
            for v = 0 to n - 1 do
                let r = Arr.get parent v
                if r <> v && (s.X v <> s.X r || s.Y v <> s.Y r) then Arr.set hot r 1
            for ev = 0 to s.EvCount - 1 do
                if bendsSegment s ev then Arr.set hot (Arr.get parent (Arr.get s.EvVert ev)) 1
            let tol = s.Tolerance
            let sqTol = tol * tol
            let mutable round = 1
            while round <= MaxRepairRounds do
                let rnd = round // an immutable copy for the visitors
                s.RepairRounds <- rnd
                s.FixCount <- 0
                let g = s.GCount
                // the ends of the graph edges, and the hot ones among them:
                let mutable items = 0
                let mutable hotCount = 0
                for e = 0 to g - 1 do
                    let a = Arr.get s.GA e
                    let b = Arr.get s.GB e
                    if Arr.get used a <> rnd then
                        Arr.set used a rnd
                        Arr.set s.VertItem items a
                        items <- items + 1
                        if Arr.get hot a = rnd then
                            Arr.set s.HotItem hotCount a
                            hotCount <- hotCount + 1
                    if Arr.get used b <> rnd then
                        Arr.set used b rnd
                        Arr.set s.VertItem items b
                        items <- items + 1
                        if Arr.get hot b = rnd then
                            Arr.set s.HotItem hotCount b
                            hotCount <- hotCount + 1
                // the edges at hot vertices:
                s.CandItem <- Buffers.ensureInt s.CandItem 0 g
                let mutable candCount = 0
                for e = 0 to g - 1 do
                    if Arr.get hot (Arr.get s.GA e) = rnd || Arr.get hot (Arr.get s.GB e) = rnd then
                        Arr.set s.CandItem candCount e
                        candCount <- candCount + 1
                // Both queries go into a tree over the few hot items, not over all of them, so that a round costs
                // about one pass over the edges and one over the vertices.
                // Every edge within tolerance of a hot vertex leads through it, each edge asks the tree of hot vertices:
                let vb = s.VertBvh
                vb.Reset hotCount
                for k = 0 to hotCount - 1 do
                    let w = Arr.get s.HotItem k
                    vb.SetRect (k, s.X w, s.Y w, s.X w, s.Y w)
                vb.Build ()
                for e = 0 to g - 1 do
                    let ax = s.X (Arr.get s.GA e)
                    let ay = s.Y (Arr.get s.GA e)
                    let bx = s.X (Arr.get s.GB e)
                    let by = s.Y (Arr.get s.GB e)
                    vb.VisitInRect (min ax bx - tol, min ay by - tol, max ax bx + tol, max ay by + tol, fun k ->
                        rerouteIfClose s sqTol e (Arr.get s.HotItem k))
                // every edge at a hot vertex leads through the other vertices within tolerance of it, the hot ones
                // among them were found above. Each vertex asks the tree of these edges, expanded by the tolerance:
                let eb = s.EdgeBvh // the winding phase builds it again if it needs it
                eb.Reset candCount
                for k = 0 to candCount - 1 do
                    let e = Arr.get s.CandItem k
                    let ax = s.X (Arr.get s.GA e)
                    let ay = s.Y (Arr.get s.GA e)
                    let bx = s.X (Arr.get s.GB e)
                    let by = s.Y (Arr.get s.GB e)
                    eb.SetRect (k, min ax bx - tol, min ay by - tol, max ax bx + tol, max ay by + tol)
                eb.Build ()
                for i = 0 to items - 1 do
                    let w = Arr.get s.VertItem i
                    if Arr.get hot w <> rnd then
                        let x = s.X w
                        let y = s.Y w
                        eb.VisitInRect (x, y, x, y, fun k -> rerouteIfClose s sqTol (Arr.get s.CandItem k) w)
                if s.FixCount = 0 then
                    round <- MaxRepairRounds + 1 // planar where it was checked, done
                elif rnd <= RerouteRounds then
                    for k = 0 to s.FixCount - 1 do
                        Arr.set hot (Arr.get s.FixVert k) (rnd + 1)
                    applyReroutes s
                    round <- round + 1
                else
                    // the reroutes did not settle: merge each vertex into the nearer end of its edge,
                    // the lower id stays the representative, as in the cluster phase
                    for k = 0 to s.FixCount - 1 do
                        let e = Arr.get s.FixEdge k
                        let end' = if Arr.get s.FixT k < 0.5 then Arr.get s.GA e else Arr.get s.GB e
                        let rw = find parent (Arr.get s.FixVert k)
                        let re = find parent end'
                        if rw < re then Arr.set parent re rw
                        elif re < rw then Arr.set parent rw re
                    for i = 0 to n - 1 do
                        Arr.set parent i (find parent i)
                    for k = 0 to s.FixCount - 1 do
                        Arr.set hot (Arr.get parent (Arr.get s.FixVert k)) (rnd + 1)
                    s.FixCount <- 0
                    applyReroutes s // no reroutes, just the graph edges built again through the new representatives
                    round <- round + 1

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
