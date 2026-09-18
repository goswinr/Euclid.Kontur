namespace BoolOps

open System
open Euclid.EuclidErrors

/// Inline geometry helpers for the Bvh, on raw coordinates.
module internal BvhUtil =

    /// The squared distance between two axis aligned rectangles, 0.0 if they overlap or touch.
    let inline sqRectDist (aMinX: float) (aMinY: float) (aMaxX: float) (aMaxY: float)
                          (bMinX: float) (bMinY: float) (bMaxX: float) (bMaxY: float) : float =
        let dx = if bMinX > aMaxX then bMinX - aMaxX elif aMinX > bMaxX then aMinX - bMaxX else 0.0
        let dy = if bMinY > aMaxY then bMinY - aMaxY elif aMinY > bMaxY then aMinY - bMaxY else 0.0
        dx * dx + dy * dy

    /// TRUE if the two axis aligned rectangles overlap or touch.
    let inline overlaps (aMinX: float) (aMinY: float) (aMaxX: float) (aMaxY: float)
                        (bMinX: float) (bMinY: float) (bMaxX: float) (bMaxY: float) : bool =
        not (bMinX > aMaxX || aMinX > bMaxX || bMinY > aMaxY || aMinY > bMaxY)

/// <summary>A static Bounding Volume Hierarchy over axis aligned rectangles, modelled on Bvh2d in Euclid.BVH
/// but stored as flat float and int arrays so that neither .NET nor Fable allocates one object per rectangle.
/// The instance owns all its arrays and reuses them: fill the item rectangles with <c>Reset</c> and <c>SetRect</c>,
/// then call <c>Build</c>, then query. Repeated use only allocates when the item count grows.
/// The tree is built top down by a median split of the rectangle centers along the longer axis
/// of each node's rectangle, with a leaf holding up to LeafSize items.
/// Queries take inlined visitor functions and use an explicit stack owned by the instance,
/// so a query allocates nothing. A visitor must not query the same Bvh instance again while it runs.</summary>
[<Sealed; NoEquality; NoComparison>]
type internal Bvh (leafSize: int) =

    let leafSize = if leafSize < 1 then Bvh.DefaultLeafSize else leafSize

    /// The default maximum count of items per leaf node.
    static member DefaultLeafSize : int = 4

    /// The maximum count of items per leaf node.
    member _.LeafSize : int = leafSize

    // per item, indexed by item id. Only the first ItemCount entries are valid:

    /// The minimum X of each item's rectangle.
    member val MinX : float[] = Array.zeroCreate 0 with get, set
    /// The minimum Y of each item's rectangle.
    member val MinY : float[] = Array.zeroCreate 0 with get, set
    /// The maximum X of each item's rectangle.
    member val MaxX : float[] = Array.zeroCreate 0 with get, set
    /// The maximum Y of each item's rectangle.
    member val MaxY : float[] = Array.zeroCreate 0 with get, set
    /// The count of items in the tree.
    member val ItemCount : int = 0 with get, set

    // per node, flattened, the root is node 0. Only the first NodeCount entries are valid:

    /// The minimum X of the rectangle around everything below each node.
    member val NodeMinX : float[] = Array.zeroCreate 0 with get, set
    /// The minimum Y of the rectangle around everything below each node.
    member val NodeMinY : float[] = Array.zeroCreate 0 with get, set
    /// The maximum X of the rectangle around everything below each node.
    member val NodeMaxX : float[] = Array.zeroCreate 0 with get, set
    /// The maximum Y of the rectangle around everything below each node.
    member val NodeMaxY : float[] = Array.zeroCreate 0 with get, set
    /// For a leaf node the start index into ItemIndices, otherwise the index of the left child node.
    member val NodeLeftOrStart : int[] = Array.zeroCreate 0 with get, set
    /// The index of the right child node. -1 for a leaf node.
    member val NodeRightChild : int[] = Array.zeroCreate 0 with get, set
    /// The count of items in a leaf node. 0 for an internal node.
    member val NodeItemCount : int[] = Array.zeroCreate 0 with get, set
    /// The count of nodes in the tree. 0 if the tree is empty or not built yet.
    member val NodeCount : int = 0 with get, set
    /// The depth of the tree, 1 for a tree with only a root leaf, 0 if empty.
    member val Depth : int = 0 with get, set

    /// The permutation of item ids. A leaf owns the range ItemIndices.[start .. start + count - 1].
    member val ItemIndices : int[] = Array.zeroCreate 0 with get, set
    /// Scratch space for the split keys while building, parallel to ItemIndices.
    member val Keys : float[] = Array.zeroCreate 0 with get, set
    /// Scratch stack of node indices for the single tree queries.
    member val Stack : int[] = Array.zeroCreate 0 with get, set
    /// Scratch stack of first node indices for the dual tree queries.
    member val StackA : int[] = Array.zeroCreate 0 with get, set
    /// Scratch stack of second node indices for the dual tree queries.
    member val StackB : int[] = Array.zeroCreate 0 with get, set

    /// Prepares the tree for itemCount items, growing the item arrays if needed.
    /// The rectangles are undefined afterwards, set them all with SetRect before calling Build.
    member b.Reset (itemCount: int) : unit =
        if itemCount < 0 then fail $"Bvh.Reset: itemCount {itemCount} must not be negative."
        b.MinX <- Buffers.ensureFloat b.MinX 0 itemCount
        b.MinY <- Buffers.ensureFloat b.MinY 0 itemCount
        b.MaxX <- Buffers.ensureFloat b.MaxX 0 itemCount
        b.MaxY <- Buffers.ensureFloat b.MaxY 0 itemCount
        b.ItemCount <- itemCount
        b.NodeCount <- 0
        b.Depth <- 0

    /// Sets the rectangle of the item. Min must not be bigger than max on either axis.
    member b.SetRect (item: int, minX: float, minY: float, maxX: float, maxY: float) : unit =
        Arr.set b.MinX item minX
        Arr.set b.MinY item minY
        Arr.set b.MaxX item maxX
        Arr.set b.MaxY item maxY

    /// Builds the tree over the first ItemCount rectangles.
    member b.Build () : unit =
        let n = b.ItemCount
        let minX = b.MinX
        let minY = b.MinY
        let maxX = b.MaxX
        let maxY = b.MaxY
        b.ItemIndices <- Buffers.ensureInt b.ItemIndices 0 n
        b.Keys        <- Buffers.ensureFloat b.Keys 0 n
        // a binary tree with at most n leaves has at most 2n-1 nodes:
        let maxNodes = 2 * n + 1
        b.NodeMinX        <- Buffers.ensureFloat b.NodeMinX 0 maxNodes
        b.NodeMinY        <- Buffers.ensureFloat b.NodeMinY 0 maxNodes
        b.NodeMaxX        <- Buffers.ensureFloat b.NodeMaxX 0 maxNodes
        b.NodeMaxY        <- Buffers.ensureFloat b.NodeMaxY 0 maxNodes
        b.NodeLeftOrStart <- Buffers.ensureInt b.NodeLeftOrStart 0 maxNodes
        b.NodeRightChild  <- Buffers.ensureInt b.NodeRightChild 0 maxNodes
        b.NodeItemCount   <- Buffers.ensureInt b.NodeItemCount 0 maxNodes
        let idx = b.ItemIndices
        let keys = b.Keys
        let nMinX = b.NodeMinX
        let nMinY = b.NodeMinY
        let nMaxX = b.NodeMaxX
        let nMaxY = b.NodeMaxY
        let nLeft = b.NodeLeftOrStart
        let nRight = b.NodeRightChild
        let nCount = b.NodeItemCount
        for i = 0 to n - 1 do
            Arr.set idx i i
        b.Depth <- 0

        // recursively builds the node for idx.[start .. start+count-1] into slot nodeIdx and its
        // subtree into the slots right after it. Returns the first free slot after the subtree.
        let rec buildNode nodeIdx start count level : int =
            if level > b.Depth then b.Depth <- level
            // the rectangle around all items of this node:
            let mutable rMinX = Arr.get minX (Arr.get idx start)
            let mutable rMinY = Arr.get minY (Arr.get idx start)
            let mutable rMaxX = Arr.get maxX (Arr.get idx start)
            let mutable rMaxY = Arr.get maxY (Arr.get idx start)
            for i = start + 1 to start + count - 1 do
                let ii = Arr.get idx i
                if Arr.get minX ii < rMinX then rMinX <- Arr.get minX ii
                if Arr.get minY ii < rMinY then rMinY <- Arr.get minY ii
                if Arr.get maxX ii > rMaxX then rMaxX <- Arr.get maxX ii
                if Arr.get maxY ii > rMaxY then rMaxY <- Arr.get maxY ii
            Arr.set nMinX nodeIdx rMinX
            Arr.set nMinY nodeIdx rMinY
            Arr.set nMaxX nodeIdx rMaxX
            Arr.set nMaxY nodeIdx rMaxY
            if count <= leafSize then
                Arr.set nLeft nodeIdx start
                Arr.set nRight nodeIdx (-1)
                Arr.set nCount nodeIdx count
                nodeIdx + 1
            else
                // split at the median of the rectangle centers along the longer axis of this node's rectangle:
                let last = start + count - 1
                if rMaxX - rMinX >= rMaxY - rMinY then
                    for i = start to last do
                        let ii = Arr.get idx i
                        Arr.set keys i (Arr.get minX ii + Arr.get maxX ii)
                else
                    for i = start to last do
                        let ii = Arr.get idx i
                        Arr.set keys i (Arr.get minY ii + Arr.get maxY ii)
                let mid = count / 2
                // only partition around the median, do not sort the whole range:
                Buffers.selectNth idx keys start last (start + mid)
                let left = nodeIdx + 1
                let right = buildNode left start mid (level + 1)
                let free = buildNode right (start + mid) (count - mid) (level + 1)
                Arr.set nLeft nodeIdx left
                Arr.set nRight nodeIdx right
                Arr.set nCount nodeIdx 0
                free

        b.NodeCount <- if n = 0 then 0 else buildNode 0 0 n 1
        // the single tree traversal pushes two children per popped node, so it needs at most depth + 1 slots.
        // The dual traversal pushes up to three pairs per popped pair and descends at most 2 * depth times:
        b.Stack  <- Buffers.ensureInt b.Stack  0 (b.Depth + 2)
        b.StackA <- Buffers.ensureInt b.StackA 0 (4 * b.Depth + 8)
        b.StackB <- Buffers.ensureInt b.StackB 0 (4 * b.Depth + 8)

    /// <summary>Calls the visitor with the id of every item whose rectangle overlaps or touches the query rectangle.
    /// The order of the visits is not defined. The visitor must not query this Bvh instance.</summary>
    member inline b.VisitInRect (qMinX: float, qMinY: float, qMaxX: float, qMaxY: float, [<InlineIfLambda>] visit: int -> unit) : unit =
        if b.NodeCount > 0 then
            let stack = b.Stack
            let nMinX = b.NodeMinX
            let nMinY = b.NodeMinY
            let nMaxX = b.NodeMaxX
            let nMaxY = b.NodeMaxY
            let nLeft = b.NodeLeftOrStart
            let nRight = b.NodeRightChild
            let nCount = b.NodeItemCount
            let idx = b.ItemIndices
            let minX = b.MinX
            let minY = b.MinY
            let maxX = b.MaxX
            let maxY = b.MaxY
            let mutable sp = 1
            Arr.set stack 0 0
            while sp > 0 do
                sp <- sp - 1
                let n = Arr.get stack sp
                if BvhUtil.overlaps qMinX qMinY qMaxX qMaxY (Arr.get nMinX n) (Arr.get nMinY n) (Arr.get nMaxX n) (Arr.get nMaxY n) then
                    let count = Arr.get nCount n
                    if count > 0 then
                        let start = Arr.get nLeft n
                        for i = start to start + count - 1 do
                            let ii = Arr.get idx i
                            if BvhUtil.overlaps qMinX qMinY qMaxX qMaxY (Arr.get minX ii) (Arr.get minY ii) (Arr.get maxX ii) (Arr.get maxY ii) then
                                visit ii
                    else
                        Arr.set stack sp (Arr.get nLeft n)
                        Arr.set stack (sp + 1) (Arr.get nRight n)
                        sp <- sp + 2

    /// <summary>Calls the visitor with every pair of items whose rectangles are closer to each other than
    /// the maximum distance, or overlap. Each unordered pair is visited once, with the smaller id first.
    /// A dual tree traversal: pairs of subtrees whose rectangles are farther apart than the maximum distance are skipped.
    /// The order of the visits is not defined. The visitor must not query this Bvh instance.</summary>
    member inline b.VisitClosePairs (maxDistance: float, [<InlineIfLambda>] visit: int -> int -> unit) : unit =
        if b.NodeCount > 0 then
            let sqMaxDist = maxDistance * maxDistance
            let stackA = b.StackA
            let stackB = b.StackB
            let nMinX = b.NodeMinX
            let nMinY = b.NodeMinY
            let nMaxX = b.NodeMaxX
            let nMaxY = b.NodeMaxY
            let nLeft = b.NodeLeftOrStart
            let nRight = b.NodeRightChild
            let nCount = b.NodeItemCount
            let idx = b.ItemIndices
            let minX = b.MinX
            let minY = b.MinY
            let maxX = b.MaxX
            let maxY = b.MaxY
            let mutable sp = 1
            Arr.set stackA 0 0
            Arr.set stackB 0 0
            while sp > 0 do
                sp <- sp - 1
                let na = Arr.get stackA sp
                let nb = Arr.get stackB sp
                if na = nb then // a self pair: every unordered pair below this node exactly once
                    let count = Arr.get nCount na
                    if count > 0 then
                        let start = Arr.get nLeft na
                        let last = start + count - 1
                        for i = start to last do
                            let ii = Arr.get idx i
                            for j = i + 1 to last do
                                let jj = Arr.get idx j
                                if BvhUtil.sqRectDist (Arr.get minX ii) (Arr.get minY ii) (Arr.get maxX ii) (Arr.get maxY ii) (Arr.get minX jj) (Arr.get minY jj) (Arr.get maxX jj) (Arr.get maxY jj) <= sqMaxDist then
                                    if ii < jj then visit ii jj else visit jj ii
                    else
                        let l = Arr.get nLeft na
                        let r = Arr.get nRight na
                        Arr.set stackA sp l; Arr.set stackB sp l
                        Arr.set stackA (sp + 1) r; Arr.set stackB (sp + 1) r
                        Arr.set stackA (sp + 2) l; Arr.set stackB (sp + 2) r
                        sp <- sp + 3
                elif BvhUtil.sqRectDist (Arr.get nMinX na) (Arr.get nMinY na) (Arr.get nMaxX na) (Arr.get nMaxY na) (Arr.get nMinX nb) (Arr.get nMinY nb) (Arr.get nMaxX nb) (Arr.get nMaxY nb) <= sqMaxDist then
                    let countA = Arr.get nCount na
                    let countB = Arr.get nCount nb
                    if countA > 0 && countB > 0 then // both leaves
                        let startA = Arr.get nLeft na
                        let startB = Arr.get nLeft nb
                        for i = startA to startA + countA - 1 do
                            let ii = Arr.get idx i
                            for j = startB to startB + countB - 1 do
                                let jj = Arr.get idx j
                                if BvhUtil.sqRectDist (Arr.get minX ii) (Arr.get minY ii) (Arr.get maxX ii) (Arr.get maxY ii) (Arr.get minX jj) (Arr.get minY jj) (Arr.get maxX jj) (Arr.get maxY jj) <= sqMaxDist then
                                    if ii < jj then visit ii jj else visit jj ii
                    elif countA = 0 then // descend into a
                        Arr.set stackA sp (Arr.get nLeft na); Arr.set stackB sp nb
                        Arr.set stackA (sp + 1) (Arr.get nRight na); Arr.set stackB (sp + 1) nb
                        sp <- sp + 2
                    else // a is a leaf, descend into b
                        Arr.set stackA sp na; Arr.set stackB sp (Arr.get nLeft nb)
                        Arr.set stackA (sp + 1) na; Arr.set stackB (sp + 1) (Arr.get nRight nb)
                        sp <- sp + 2

    /// <summary>Calls the visitor with every pair of an item of this tree and an item of the other tree whose
    /// rectangles are closer to each other than the maximum distance, or overlap. The first argument is the id
    /// in this tree, the second the id in the other tree. A dual tree traversal like VisitClosePairs.
    /// Both trees must be built. The visitor must not query either tree. Uses the stacks of this tree.</summary>
    member inline b.VisitClosePairsWith (other: Bvh, maxDistance: float, [<InlineIfLambda>] visit: int -> int -> unit) : unit =
        if b.NodeCount > 0 && other.NodeCount > 0 then
            let sqMaxDist = maxDistance * maxDistance
            // the pair stack needs room for the depths of both trees:
            b.StackA <- Buffers.ensureInt b.StackA 0 (2 * (b.Depth + other.Depth) + 8)
            b.StackB <- Buffers.ensureInt b.StackB 0 (2 * (b.Depth + other.Depth) + 8)
            let stackA = b.StackA
            let stackB = b.StackB
            let aMinX = b.NodeMinX
            let aMinY = b.NodeMinY
            let aMaxX = b.NodeMaxX
            let aMaxY = b.NodeMaxY
            let aLeft = b.NodeLeftOrStart
            let aRight = b.NodeRightChild
            let aCount = b.NodeItemCount
            let aIdx = b.ItemIndices
            let bMinX = other.NodeMinX
            let bMinY = other.NodeMinY
            let bMaxX = other.NodeMaxX
            let bMaxY = other.NodeMaxY
            let bLeft = other.NodeLeftOrStart
            let bRight = other.NodeRightChild
            let bCount = other.NodeItemCount
            let bIdx = other.ItemIndices
            let mutable sp = 1
            Arr.set stackA 0 0
            Arr.set stackB 0 0
            while sp > 0 do
                sp <- sp - 1
                let na = Arr.get stackA sp
                let nb = Arr.get stackB sp
                if BvhUtil.sqRectDist (Arr.get aMinX na) (Arr.get aMinY na) (Arr.get aMaxX na) (Arr.get aMaxY na) (Arr.get bMinX nb) (Arr.get bMinY nb) (Arr.get bMaxX nb) (Arr.get bMaxY nb) <= sqMaxDist then
                    let countA = Arr.get aCount na
                    let countB = Arr.get bCount nb
                    if countA > 0 && countB > 0 then // both leaves
                        let startA = Arr.get aLeft na
                        let startB = Arr.get bLeft nb
                        for i = startA to startA + countA - 1 do
                            let ii = Arr.get aIdx i
                            for j = startB to startB + countB - 1 do
                                let jj = Arr.get bIdx j
                                if BvhUtil.sqRectDist (Arr.get b.MinX ii) (Arr.get b.MinY ii) (Arr.get b.MaxX ii) (Arr.get b.MaxY ii) (Arr.get other.MinX jj) (Arr.get other.MinY jj) (Arr.get other.MaxX jj) (Arr.get other.MaxY jj) <= sqMaxDist then
                                    visit ii jj
                    elif countA = 0 then // descend into a
                        Arr.set stackA sp (Arr.get aLeft na); Arr.set stackB sp nb
                        Arr.set stackA (sp + 1) (Arr.get aRight na); Arr.set stackB (sp + 1) nb
                        sp <- sp + 2
                    else // a is a leaf, descend into b
                        Arr.set stackA sp na; Arr.set stackB sp (Arr.get bLeft nb)
                        Arr.set stackA (sp + 1) na; Arr.set stackB (sp + 1) (Arr.get bRight nb)
                        sp <- sp + 2

    /// The ids of all items whose rectangle overlaps or touches the query rectangle, in undefined order.
    /// A convenience for tests and debugging, use VisitInRect in the library itself.
    member b.ItemsInRect (qMinX: float, qMinY: float, qMaxX: float, qMaxY: float) : ResizeArray<int> =
        let r = ResizeArray<int> ()
        b.VisitInRect (qMinX, qMinY, qMaxX, qMaxY, fun i -> r.Add i)
        r

    /// All pairs of items whose rectangles are closer than the maximum distance, as a flat list: i0, j0, i1, j1, ...
    /// with i smaller than j in each pair, in undefined order.
    /// A convenience for tests and debugging, use VisitClosePairs in the library itself.
    member b.ClosePairs (maxDistance: float) : ResizeArray<int> =
        let r = ResizeArray<int> ()
        b.VisitClosePairs (maxDistance, fun i j -> r.Add i ; r.Add j)
        r

    /// All pairs of an item of this tree and an item of the other tree closer than the maximum distance,
    /// as a flat list: i0, j0, i1, j1, ... in undefined order.
    /// A convenience for tests and debugging, use VisitClosePairsWith in the library itself.
    member b.ClosePairsWith (other: Bvh, maxDistance: float) : ResizeArray<int> =
        let r = ResizeArray<int> ()
        b.VisitClosePairsWith (other, maxDistance, fun i j -> r.Add i ; r.Add j)
        r
