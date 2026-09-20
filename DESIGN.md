# Euclid.Kontur design

Boolean operations (union, intersection, difference, xor) on 2D regions made of closed
`Polyline2D`s from Euclid. Float coordinates only, no integer grid, no sweep line.
Segment pairs are found with a private Bounding Volume Hierarchy modelled on Euclid.BVH.

This is a design document, not documentation of existing code. Every section marked
**Decision** is a choice that was reviewed. Section 11 lists the review outcome.

## 1. Goals and non goals

Goals

- Union, Intersection, Difference and Xor between two Kontur values, plus Simplify (union of a Kontur with nothing) to resolve self intersections.
- Fill rules NonZero, EvenOdd, Positive and Negative, chosen per Kontur.
- Input vertices come out unchanged. Only intersection points are new coordinates. This is what "exact" means here, see section 2.
- Robust against the usual degeneracies: touching vertices, T junctions, collinear overlapping edges, duplicate points, zero length segments, spikes, self intersections, many paths per Kontur.
- Low allocation: all intermediate state lives in flat arrays inside a reusable engine object. A boolean operation allocates only the output `Polyline2D`s when the engine is reused.
- Same source compiles on .NET (net6.0, net472) and with Fable to JavaScript, like Euclid and Euclid.BVH.

Non goals for version 1

- Open polylines clipped by regions (Clipper's "open paths"). The internal model leaves room for it, see 9.
- Offsetting (Euclid already has `Offset2D`).
- Arcs, curves. Polylines only.
- Minkowski sums.
- A nesting tree (outer/hole hierarchy) as output. Orientation of the output encodes it, a tree can be derived later.

## 2. What "exact" means with floats

Clipper2 and iOverlay scale every coordinate to an integer grid so that every predicate
(left/right test, segment crossing) is computed without rounding. The price is that every
input vertex is quantized to the grid and comes back slightly moved.

With `float` there is no way to make the predicates exact for free, and the intersection point
of two float segments is not representable as a float anyway. So "exact" cannot mean
"mathematically exact geometry". It can mean, and here means:

1. Input vertices are copied, never rounded, scaled or moved. They appear in the output bit for bit.
2. Each intersection point is computed once and inserted as one shared vertex into both segments. Both sides of the graph therefore agree on it. Consistency of the topology does not depend on any predicate being exact.
3. Everything closer than a user given absolute `tolerance` is considered equal: two points, a point and a segment, two collinear segments. This is the Rhino model of absolute tolerance, not the CGAL model of exact predicates.

**Decision:** tolerance based snapping is the robustness mechanism. Exact orientation predicates
(Shewchuk's adaptive `orient2d`) are not used in version 1. Section 7 explains where the
design deliberately avoids depending on the sign of a near zero determinant so that
this is safe.

The known weak spot of every tolerance based approach: merging vertices that are within
tolerance moves vertices by up to `tolerance`, which can create a crossing of two edges that
did not cross before. Those residual crossings are at the tolerance scale. Version 1 ignores
them and documents it. Clipper2 has the same class of artefacts.

## 3. Data model

### 3.1 Public types

```fsharp
namespace Euclid

/// How the winding number of a point decides if the point is inside a Kontur.
type FillRule =
    | EvenOdd  = 0  // odd winding is inside
    | NonZero  = 1  // winding <> 0 is inside
    | Positive = 2  // winding > 0 is inside, needs oriented paths: counter clockwise outer, clockwise holes
    | Negative = 3  // winding < 0 is inside

type ClipType =
    | Union        = 0
    | Intersection = 1
    | Difference   = 2  // subject minus clip
    | Xor          = 3

/// A Kontur (German for contour) is a region of the plane defined by one or more closed Polyline2Ds and a fill rule.
/// Paths may self intersect, overlap each other, or be nested. The fill rule decides what is inside.
/// The fill rule belongs to the Kontur, not to the boolean operation, unlike in Clipper.
/// Subject and clip get separate winding numbers on every edge, and each fill rule is applied to
/// its own winding number before the boolean combines them. So two different fill rules never
/// conflict, they are two independent decisions.
/// What this buys is one pass instead of two: an EvenOdd glyph unioned with a NonZero CAD outline
/// works in one operation. With a fill rule per operation the glyph would have to be simplified
/// first, then unioned.
/// Kontur values returned by a boolean operation are always simple: no self intersections, no overlaps,
/// outer paths counter clockwise, holes clockwise, and their fill rule is Positive. Such a result
/// reads the same under NonZero, Positive and EvenOdd, so the rule of a result never has to be changed.
type Kontur =
    member Paths : ResizeArray<Polyline2D>   // each path is closed: first point equals last point
    member FillRule : FillRule
    member BoundingRectangle : BRect
    member WindingNumber : Pt -> int         // sum of Polyline2D.WindingNumber over all paths
    member Contains : Pt -> bool             // fill rule applied to WindingNumber
    member IsEmpty : bool

    static member create : paths: seq<Polyline2D> * fillRule: FillRule -> Kontur
    static member ofPolyline : Polyline2D * ?fillRule: FillRule -> Kontur   // default NonZero
```

The docstring above is part of the design: the reason for the fill rule living on the `Kontur`
must stay explicit in the published XML docs of `Kontur`, `Kontur.create` and `FillRule`, so a
reader coming from Clipper sees why this differs.

`Kontur` wraps the polylines it is given. It does not copy them and does not validate them
beyond "closed and at least 3 distinct points" at operation time. Open polylines fail with an
`EuclidException` style error, they are not silently closed.

### 3.2 Operations

```fsharp
/// One boolean operation between a subject and a clip Kontur.
module Kontur =
    val union        : subject: Kontur -> clip: Kontur -> Kontur
    val intersection : subject: Kontur -> clip: Kontur -> Kontur
    val difference   : subject: Kontur -> clip: Kontur -> Kontur
    val xor          : subject: Kontur -> clip: Kontur -> Kontur
    /// Resolves self intersections and overlaps of one Kontur under its fill rule.
    val simplify     : Kontur -> Kontur
    /// Union of many Kontur values. Each Kontur is simplified under its own fill rule first, then all are merged in one NonZero pass.
    val unionAll     : seq<Kontur> -> Kontur
    /// Same as above with an explicit tolerance instead of Kontur.defaultTolerance
    val unionWith        : tolerance: float -> subject: Kontur -> clip: Kontur -> Kontur
    ...
```

These are convenience wrappers that create a `KonturEngine` per call. For loops over many
operations the engine is reused:

```fsharp
/// Holds all scratch buffers of the algorithm so that repeated operations do not allocate.
/// Not thread safe. One engine per thread.
type KonturEngine (tolerance: float) =
    member Tolerance : float
    member Execute : subject: Kontur * clip: Kontur * op: ClipType -> Kontur
    member Simplify : Kontur -> Kontur
    member UnionAll : seq<Kontur> -> Kontur
```

`tolerance` is absolute, in the units of the coordinates. Default `1e-6`.

## 4. Pipeline

Every phase reads and writes flat arrays owned by the engine. Indices, not objects, refer to
vertices, segments and edges. The phases:

```
 Kontur A, Kontur B
       |
  [1] ingest        copy paths into one interleaved vertex buffer, one segment per polyline edge
       |
  [2] intersect     sweep over the segment rectangles sorted by min x; classify every overlapping pair; record split events
       |
  [3] split         sort split events per segment by parameter; produce sub segments
       |
  [4] cluster       union find over all vertices within tolerance; input vertices win as representatives
       |
  [5] graph         canonical edges (low id -> high id), merge coincident edges by summing winding deltas
       |
  [6] winding       winding number of subject and clip on the left and right side of every edge
       |
  [7] select        per edge: inside on left? inside on right? keep if they differ, orient inside to the left
       |
  [8] link          walk selected edges into closed contours, tightest turn at each vertex
       |
    Kontur out       one Polyline2D per contour, outer CCW, holes CW, FillRule.Positive
```

### 4.1 Ingest

```fsharp
// engine fields, all grow only and reused between executions
xy        : float[]   // interleaved x0 y0 x1 y1 ..  same layout as Polyline2D.XYs, plus vertexCount
pathStart : int[]     // first vertex index of each path, length pathCount + 1
pathGroup : int[]     // 0 subject, 1 clip
segCount  : int                  // number of input segments
```

A closed `Polyline2D` stores its first point twice, so segment `i` of a path runs from vertex
`i` to vertex `i + 1` inside the path's range. No segment array is needed for the input: a
segment is identified by the index of its start vertex, and `segPath` is found by binary
search in `pathStart` or, cheaper, stored as `segPath : int[]` filled during ingest.

Ingest drops consecutive vertices closer than `tolerance` (zero length segments) and paths
that end up with fewer than 3 distinct vertices. It does not remove collinear vertices.

The copy from `Polyline2D.XYs` into `xy` is a plain loop after one `ensureCapacity`.

### 4.2 Intersect

The rectangle of every segment, expanded by `tolerance`, goes into four flat arrays, and the
segment ids are sorted by the minimum `x` of their rectangle. A **sweep and prune** then
reports every pair of segments whose rectangles overlap, each unordered pair once: every
segment is paired with the following ones in sorted order as long as their rectangles start
before its own ends, and the pair is kept if the rectangles overlap in `y` too. The candidate
pairs are visited with the smaller id first, so the crossing point is always computed from the
same segment, whatever the sort order.

This replaced a dual tree self traversal over a BVH of the segment rectangles (section 5).
The sweep does more rectangle tests than the tree, 8916 against 6117 on the polysXY dataset,
but they are two comparisons on sequential memory with one predictable branch, while the
traversal spent two thirds of the intersect phase on node pops and stack traffic, and the tree
build cost as much again. The sweep was a quarter faster on the whole operation under Node and
on .NET, and still slightly faster on two 3000 corner stars whose long spikes all overlap in
`x`, the worst case for a sweep and prune: 1.9 million `y` tests, but sequential ones.
The `x` sort is the one place the intersect phase is not linear.

For every candidate pair `(p, q)` classify, in this order, because earlier cases reuse
existing vertices and later ones create new ones:

| case | test | split events |
|---|---|---|
| shared endpoint | same vertex index, or endpoints within tolerance | none, the cluster phase merges them |
| T junction | an endpoint of `q` within `tolerance` of `p` (and vice versa) | `p` splits at that vertex, parameter from projection |
| collinear overlap | both endpoints of `q` within `tolerance` of the line of `p` and the parameter ranges overlap | `p` splits at the endpoints of `q` that lie inside it, and vice versa |
| proper crossing | parameters `t`, `u` strictly inside `(0, 1)` after the tolerance cases are excluded | one new vertex appended to `xy`, both segments split at it |
| parallel, disjoint | none of the above | none |

A split event is three parallel entries:

```fsharp
splitSeg  : int[]    // segment index
splitT    : float[]  // parameter along the segment, 0 < t < 1
splitVert : int[]    // vertex index, existing or newly appended
splitCount : int
```

The T junction test before the crossing test is what makes an input vertex sitting on another
path's edge produce a single shared vertex instead of a new point a few ulps away.

The crossing point itself is computed with the standard determinant formula
(`XLineXY.parameters` in Euclid). The determinant is only used to compute the point, never to
decide topology: whether two segments cross is decided by the tolerance tests above plus the
parameter range check, and the resulting vertex is shared. A wrong sign of a near zero
determinant therefore produces a point that lands within tolerance of an endpoint, which the
cluster phase merges.

The classification reads the eight coordinates once and starts from three cross products:
the determinant of the two directions, and the signed areas of `c` against the line of `p` and
of `a` against the line of `q`; the areas of `d` and `b` follow by adding or subtracting the
determinant, and the crossing parameters are ratios of these. An endpoint can only lie within
tolerance of the other segment if it lies within tolerance of that segment's line, which is
one comparison of a squared area against `tolerance² * length²`. For most candidate pairs no
endpoint passes that gate, and then only a proper crossing is possible, whose point is farther
than the tolerance from all four ends by the same argument. That fast path does no endpoint
distances at all. The gate is a necessary condition, so the slow path still runs the full
tolerance tests of the table above.

The traversal calls the classification for adjacent segments of the same path too. They
share an endpoint, which is the first case, but they may also overlap collinearly (a spike
`A B A`), which must be detected.

### 4.3 Split

Split events are grouped per segment with a counting sort into a CSR layout
(`splitStart : int[]` of length `segCount + 1`, `splitOrder : int[]`), then each segment's
range is sorted by `t` with an insertion sort. Ranges are tiny, typically 0 to 3 entries.
Duplicate vertices inside a range (the same vertex recorded twice, from two different pairs)
collapse to one.

Output of the phase: the pre-cluster edge list

```fsharp
edgeFrom  : int[]   // vertex index
edgeTo    : int[]
edgeGroup : int[]   // 0 subject, 1 clip; direction is the path direction
```

### 4.4 Cluster

All vertices, input and new, closer than `tolerance` are merged. The pairs feed a union find
(`parent : int[]`). Representative choice: the input vertex with the lowest index in the
cluster if there is one, else the lowest index. This is what keeps input coordinates
unchanged in the output.

The pairs are found by a sweep, not by a tree. The plane is cut into columns `2 * tolerance`
wide (`floor (x / (2 * tolerance))`, stored per vertex as a float in `cellCol`), so two
vertices within tolerance are in the same or in adjacent columns, even when the rounding of
the division moves one of them across a column boundary. The vertex ids are sorted by column
and then by `y`. Then every vertex is compared with the following vertices of its own column up
to `tolerance` above it, and with the vertices of the next column from `tolerance` below to
`tolerance` above it. That window into the next column only moves forward while the sweep
advances, so after the sort the phase is linear in the vertex count plus the pairs checked,
whatever the distribution of the vertices: a column of vertices sharing one `x`, common in
rectilinear input, costs nothing extra. Every pair within tolerance is found exactly once, the
same pairs a tree query would report.

This replaced a second BVH over the vertices as zero size rectangles with a close pairs
traversal. The tree build alone cost three times the segment tree build, and the whole phase
took a quarter of the running time on the polysXY dataset under Node. A tolerance of zero
merges only identical vertices, any column width works for that, so the width falls back to
one.

Merging is transitive, so a chain of vertices each within tolerance of the next collapses to
one even if the ends are farther apart than tolerance. That is the expected behaviour of a
tolerance model and is documented.

After clustering `edgeFrom` and `edgeTo` are rewritten to representatives. Edges whose ends
became equal are dropped.

### 4.5 Graph

Each edge is put into canonical direction `a < b` (vertex index order) and carries a winding
delta per group:

```fsharp
edgeA      : int[]
edgeB      : int[]
edgeDeltaS : int[]   // +1 if the subject path ran a -> b, -1 if b -> a, summed over merged edges
edgeDeltaC : int[]
```

Winding convention: paths with positive signed area (counter clockwise, Euclid's
`Polyline2D.IsCounterClockwise`) have their inside on the left. Crossing an edge from its
right side to its left side adds the edge's delta to the winding number.

Coincident edges (same `a`, same `b`) are merged by sorting on the key `(a, b)` and summing
the deltas. An edge whose two deltas are both zero separates nothing and is dropped. Merging
is what handles overlapping collinear edges, shared boundaries between subject and clip, and
paths traced twice.

Sorting on the `(a, b)` key: not an `int64` key, Fable emulates `int64` with a slow class.
A counting sort by `a` into CSR ranges (one range per vertex, in a scratch `int[]` of length
`vertexCount + 1`), then each range is sorted by `b` with the in place quicksort of an index
array. The ranges are short, a vertex's degree, so this is O(E + V) in practice. It replaced one
quicksort over all sub segments with a two-int comparison, which took a tenth of the running time.

Per vertex adjacency in CSR layout: `vertEdgeStart : int[]` of length `vertexCount + 1` and
`vertEdges : int[]` holding every edge twice. Each vertex's range is sorted by the direction
of the edge leaving that vertex, using a **pseudo angle key**, not `atan2`:

```fsharp
// monotonic in the true angle, range [0, 4), no trigonometry, no division by zero for non zero vectors
let inline pseudoAngle (dx: float) (dy: float) =
    let p = dx / (abs dx + abs dy)          // in [-1, 1]
    if dy >= 0.0 then 1.0 - p else 3.0 + p
```

This is a key sort, so the ordering is always a consistent total order even for edges that
are nearly parallel. For nearly parallel edges the geometric order may be wrong, and section
7 explains why that stays a local, cosmetic error.

### 4.6 Winding

For every edge we need the winding number of subject and of clip on its left side. The right
side follows from `left - delta`.

**Decision (implemented):** propagate winding numbers through the graph, and use one ray cast
per connected component to seed it.

Propagation around a vertex: with incident half edges sorted counter clockwise `h0 .. hk`, the
wedge between `hi` and `hi+1` is a region. Going counter clockwise across `hi` (leaving the
vertex along it) crosses from its right wedge to its left wedge, so the wedge winding changes
by `+delta` if `hi` leaves the vertex in canonical direction (even half edge id) and `-delta`
if it leaves against it (odd id). The sum of these around a full circle is zero for any
consistent input, so a wrong angular order of two edges affects only the wedge between them.

The left wedge of an edge at vertex `a` and its left wedge at vertex `b` are the same face,
so winding values propagate along edges. A breadth first walk over half edges (a reusable
`int[]` queue) visits every vertex once and assigns all its edges. The whole phase is O(E)
after the angular sort, and all edges of a component are consistent with each other by
construction, whatever the seed.

Seed per component: vertices are processed in id order, the first unprocessed vertex with
edges seeds a new component. The wedge containing the direction just above `+x` is known for
any vertex: it is the wedge after the last half edge with pseudo angle `0.0` (an edge going
exactly to `+x`), else the wedge after the last half edge of the ring, cyclically before the
first. Its winding is found by one ray cast from the vertex going to `+x`, counting crossings
with the **graph edges** with the exact half open rule (`y0 <= py` differs from `y1 <= py`):
an edge whose canonical direction goes up adds its winding deltas, one going down subtracts
them. That count is the exact winding of the point just right of the seed, which lies in that
wedge whatever directions the edges of the seed take. Edges incident to the seed are skipped
by their vertex ids, they cross the horizontal through the seed at the seed itself and must
not be counted by a rounding error; every other edge is farther than the tolerance from the
seed and is counted exactly. The half open rule treats a point on a horizontal edge as lying
on that edge's upper side, which is why the seed wedge is the one above a horizontal edge
leaving to the right.
The first component scans all graph edges, which is cheaper than building a tree; from the
second component on a BVH over the graph edges answers the ray queries.

An earlier version sorted the vertices by `x` and seeded each component at its leftmost
vertex, so that all edges of the seed point to `+x`. The argument above does not need that,
and the sort took a tenth of the running time, so it was dropped.

Three earlier variants were tried and rejected, and the reasons matter for anyone revisiting this:

- A ray from a hair (1e-9 relative) right of the seed against the **input** segments: a segment
  passing within tolerance of the seed, or ending at a vertex merged into the seed, is a graph
  edge through the seed, but the input segment crosses the ray next to the seed and was counted.
  That shifted every winding number of the component and turned a union inside out. Found on the
  `Test/Scripts/data/polysXY.json` dataset at small scales, where the noise of the data falls
  below the tolerance. The graph edges are the only geometry the rings agree with.

- A ray from the midpoint of every edge (per edge ray casting): a vertex snapped onto a segment
  bends the sub edge away from its parent by up to the tolerance, and the midpoint can fall into
  that sliver, on the wrong side of the parent. Offsetting the start by twice the tolerance
  fixes that but then a segment passing within that offset without being split (which the
  tolerance model allows) is crossed instead. Both errors make edges of one vertex disagree,
  and linking fails. Per edge ray casting survives in `Winding.computeByRayCast` as a test
  oracle for clean input only.
- A seed on the `-x` side of the leftmost vertex, at tolerance distance: segments passing within
  tolerance of an intersection vertex are not split at it, and one crossing the ray between the
  start and the vertex flips the whole component. Starting a hair to the right of the vertex
  needs no tolerance at all.

Slivers at the tolerance scale (a segment passing within tolerance of an intersection vertex
without sharing it) remain as tiny faces of the graph. They are consistent, they only make
point in region answers inside them fuzzy, which the tolerance model allows.

### 4.7 Select

```fsharp
let inline isInside (rule: FillRule) (w: int) =
    match rule with
    | FillRule.EvenOdd  -> w % 2 <> 0
    | FillRule.NonZero  -> w <> 0
    | FillRule.Positive -> w > 0
    | _                 -> w < 0

let inline combine (op: ClipType) (inS: bool) (inC: bool) =
    match op with
    | ClipType.Union        -> inS || inC
    | ClipType.Intersection -> inS && inC
    | ClipType.Difference   -> inS && not inC
    | _                     -> inS <> inC
```

For every edge: `insideLeft = combine op (isInside ruleS leftS) (isInside ruleC leftC)`, the
same for the right side. The edge is part of the result if `insideLeft <> insideRight`. Its
output direction puts the inside on the left: canonical `a -> b` if `insideLeft`, else
`b -> a`. Stored as `edgeOut : int[]` with values `0`, `+1`, `-1`.

Because insideness is a property of faces and edges are face boundaries, every vertex has as
many selected edges leaving as entering. Linking therefore always closes.

### 4.8 Link

Start at any unused selected edge, follow it, and at each vertex choose the next unused
selected edge that leaves the vertex and is the **first one counter clockwise after the
reversed incoming direction**, which is the tightest right turn keeping the inside on the
left. This is a scan over the vertex's angular range, which is already sorted. Mark used,
continue until back at the start vertex. Write the vertices into a new `Polyline2D`, closing
it by repeating the first point.

The tightest turn rule is what makes contours that touch at a vertex come out as separate
simple contours instead of one self touching contour. If the pseudo angle order of two nearly
parallel edges is wrong, the walk still closes, the result region is still correct under
`Positive`, only the split into contours at that vertex may differ.

Output orientation follows from "inside on the left": outer contours counter clockwise with
positive `SignedArea`, holes clockwise. The result `Kontur` gets `FillRule.Positive`.

With `RemoveCollinear` set, a vertex whose two output edges are collinear within tolerance is
skipped while writing.

## 5. The private BVH

**Decision:** Euclid.Kontur depends on Euclid only. It carries its own internal BVH, modelled on
`Bvh2d` in Euclid.BVH (flattened node array, median split along the longer axis with an in
place quickselect, leaf size 4), but stored as flat float arrays instead of `BRect` structs so
that neither .NET nor Fable allocates one object per rectangle:

```fsharp
type internal Bvh =                     // engine owned, reused between executions
    // per item, indexed by item id:
    mutable MinX : float[]
    mutable MinY : float[]
    mutable MaxX : float[]
    mutable MaxY : float[]
    // per node, flattened, root at 0:
    mutable NodeMinX / NodeMinY / NodeMaxX / NodeMaxY : float[]
    mutable NodeLeftOrStart : int[]   // leaf: start into ItemIndices, else left child
    mutable NodeRightChild  : int[]   // -1 for a leaf
    mutable NodeCount       : int[]   // > 0 for a leaf
    mutable ItemIndices     : int[]   // permutation of item ids
    mutable Keys            : float[] // scratch for the split keys
    mutable NodeItemCount   : int
```

The pipeline builds a tree over the graph edges only when the graph has more than one
component, for the winding seed ray casts (query with a half infinite rectangle). The tree
over the input segments is built only by the per edge ray casting oracle of the tests, from
the rectangles the intersect phase sorted. The intersect phase itself sweeps those rectangles
(section 4.2) and the cluster phase sweeps the vertices (section 4.4); both replaced a tree
because at these sizes the tree build and the branchy traversal cost more than the sorted
sweep, on .NET as under Node.

The traversals are `inline` members taking `[<InlineIfLambda>]` visitors, so the per pair
and per item callbacks compile to loop bodies rather than closure calls:

```fsharp
member inline VisitClosePairs : maxDistance: float * [<InlineIfLambda>] visit: (int -> int -> unit) -> unit
member inline VisitInRect     : minX * minY * maxX * maxY * [<InlineIfLambda>] visit: (int -> unit) -> unit
```

Mutable state a visitor needs lives in engine fields, not in captured `let mutable` locals,
because F# turns captured mutable locals into heap ref cells.

Elongated segment rectangles are a known weakness of any AABB tree on polygon edges. It is
acceptable for version 1. If profiling shows it, a later version can split long segments
virtually or use a grid for the intersect phase.

When the tree has stabilised it can be offered back to Euclid.BVH as a float array variant.

## 6. Allocation and performance rules

- Both targets are performance targets: .NET and Fable to JavaScript. Benchmarks run under both `dotnet` and Node.
- One `KonturEngine` holds every buffer. All buffers are raw `float[]` and `int[]` with a count and a doubling "ensure capacity" helper, never `ResizeArray`. Fable compiles `float[]` to `Float64Array` and `int[]` to `Int32Array`, while `ResizeArray<float>` becomes a plain JavaScript array of boxed numbers. `Clear` resets counts and keeps capacity.
- Every index into those buffers goes through `Arr.get` and `Arr.set`, never `arr.[i]`. Fable 4.12 and later compile every array index into a bounds checked call of one shared library function, and that call took two thirds of the running time under Node: the function sees typed and untyped arrays from every call site, so V8 cannot specialise it. `Arr.get` and `Arr.set` emit a plain JavaScript index under Fable and are the ordinary inlined indexer on .NET. The change made the union of the polysXY dataset seven times faster under Node and did not change .NET. `ResizeArray` indexing (the input `Polyline2D.XYs`) is not hot and keeps the plain indexer.
- No per vertex, per segment or per edge objects. No tuples, no struct tuples, no struct records in hot loops (Fable allocates them). Functions return one primitive and write extra results into engine fields.
- Ingest copies from `Polyline2D.XYs` with a plain loop; that is the one place a `ResizeArray` is read.
- No `int64` keys (Fable). No `Span`, `stackalloc`, `ArrayPool`, `CollectionsMarshal` (net472 and Fable).
- No `Array.Sort(keys, items)` overloads (not in Fable). In place quicksort and insertion sort helpers on parallel `int[]`/`float[]` arrays live in one internal module, following `BvhUtil.selectNth` in Euclid.BVH.
- No `atan2`, no `sqrt` in the hot loops. Squared distances against squared tolerance, pseudo angles for sorting.
- Hot inner functions are `inline`; visitor parameters use `[<InlineIfLambda>]` where the callee is inline.
- Recursion in the BVH traversals is bounded by tree depth (log n). Everything else is loops.
- Complexity: ingest O(N), intersect O(N log N + P) with P the pairs overlapping in x, cluster O(V log V), graph sort O(E log E), winding O(E) plus one ray cast per component, link O(E). Memory O(N + K) with K intersections.
- Expected cost split on real input: the intersect sweep and the segment classification dominate. This is the thing to benchmark first. Measured on the polysXY dataset under Node after the changes of 2026-09-19: intersect a third, cluster a sixth, the graph sorts a quarter, winding a tenth, the rest small. `Test/Scripts/console/phases-polysXY.mjs` prints that split.
- Time a phase inside the whole pipeline, never in a tight loop of its own. The same traversal took 0.026 ms repeated by itself and 0.067 ms inside the pipeline: repeating identical input lets the branch predictor learn every data dependent branch and keeps the caches warm, which the real pipeline does not. Isolated numbers rank the phases wrong.

## 7. Where robustness comes from

A boolean algorithm on floats breaks in one of two ways: it computes an inconsistent
topology (a crossing detected by one test and not by another) or it loops or corrupts memory
on a degenerate input. The design avoids both like this:

- Topology never depends on the sign of a determinant. Crossings are decided by tolerance tests, the intersection point is one shared vertex, and coincidence is decided by clustering. Every later phase works on integer vertex ids.
- The angular order is a key sort of pseudo angles: always a valid total order, so sorting cannot misbehave and linking cannot loop. Geometric mistakes for nearly parallel edges are confined to the wedge between them.
- Winding deltas sum to zero around every vertex by construction, so propagation errors do not spread.
- The ray cast uses the half open rule, which is exact for any float input: a vertex exactly on the ray is counted once, a horizontal segment on the ray never.
- Linking always terminates because every selected edge is used at most once and in degree equals out degree at every vertex.
- `Graph.validate` function used in tests: parent pointers form a forest, CSR ranges are contiguous, delta sums are zero, every vertex has equal in and out degree in the selection.

What it does not guarantee: features smaller than `tolerance` may disappear or become slivers,
and the residual crossings described in section 2 are not resolved.

## 8. Testing

- Scriptorium (Quill for the test DSL, Nib for the assertions) runs the same test files unchanged on .NET and on Node.
- Oracle tests: for random points, `result.Contains pt` must equal `combine op (subject.Contains pt) (clip.Contains pt)` for points farther than `tolerance` from any input edge. This tests the region, not its contour decomposition, and catches almost every bug in phases 2 to 8.
- Area tests: `area (A union B) = area A + area B - area (A intersect B)`, xor and difference likewise.
- Winding propagation against per edge ray casting on every test input.
- Degenerate fixtures: squares sharing an edge, sharing a vertex, identical squares, one square inside another, a spike, a figure eight under each fill rule, a star polygon under NonZero and EvenOdd, coincident opposite edges that cancel, three segments through nearly one point, vertices within tolerance of an edge, all with both orientations.
- Randomized stress: random polygons with many self intersections, checked by the point oracle and by the internal validators.
- The tests of the Klip repository, ported to this API in `Test/TestKlip.fs`: the touching union, bridge, tolerance, sliver and nesting cases, and the 195 Clipper2 fixtures of `Test/data-Clipper2/Polygons.txt`, each checked against the expected area and against the point oracle. The contour counts of the fixtures are only checked within a wide band: Euclid.Kontur splits contours touching at a vertex and merges polygons sharing an edge, while the integer reference does the opposite, and the same region has many contour decompositions.
- Benchmarks on .NET against Clipper2 (through its .NET package) for the same input, at least for union of two large random polygons and union of many small rectangles.
- An SVG visualisation like the one in Euclid.BVH: input, the graph with winding numbers per edge, the result. Worth doing early, it is the fastest way to debug this kind of code.

## 9. Extension points kept open

- Open paths: segments with delta 0 in a third group. They never separate regions, and are selected by the insideness of their own face. Needs the link phase to also emit open results.
- N groups instead of subject and clip: the per edge delta and winding arrays get a stride, `combine` becomes a predicate over N booleans.
- Nesting tree: from the output orientation and one containment test per contour, using the segment BVH.
- Robust `orient2d`: if the tolerance model turns out insufficient, a Shewchuk style adaptive predicate can replace the tolerance tests in the intersect classification and the pseudo angle comparison, without touching the rest of the pipeline.

## 10. Project layout

```
Src/Euclid.Kontur.fsproj   net6.0;net472, Fable content, references Euclid only
Src/FillRule.fs             FillRule, ClipType, isInside, combine
Src/Kontur.fs                Kontur
Src/Buffers.fs              ensureCapacity helpers, in place sorts on parallel arrays
Src/Bvh.fs                  the private float array BVH, section 5
Src/Ingest.fs               phase 1
Src/Intersect.fs            phases 2 and 3
Src/Graph.fs                phases 4 and 5, pseudo angle
Src/Winding.fs              phase 6, ray cast
Src/Link.fs                 phases 7 and 8
Src/Engine.fs               KonturEngine, owns every buffer, runs the phases
Src/KonturModule.fs          public companion module of convenience functions
Test/                       Scriptorium.Quill + Scriptorium.Nib, fixtures, oracle tests, benchmarks
Docs/                       fsdocs, plus the SVG visualisation (pending)
```

The engine owns the buffers and each phase module is a set of functions taking the engine.
This keeps every phase testable on its own and keeps the buffers in one place.

## 11. Decisions taken

Reviewed on 2026-09-19. These replace the earlier open questions.

| # | question | decision |
|---|---|---|
| 1 | meaning of exact | tolerance model (section 2), no exact predicates in version 1 |
| 2 | topology independent of determinant signs | accepted as the robustness foundation (section 7) |
| 3 | finding segment pairs | sweep and prune over the rectangles sorted by min x, each unordered pair once; replaced the dual tree self traversal on 2026-09-19, section 4.2 |
| 4 | tree | private BVH inside Euclid.Kontur, four float arrays per item, no Euclid.BVH dependency (section 5) |
| 5 | fill rule | on the `Kontur`, results are always `Positive` and oriented; the reason is spelled out in the `Kontur` docstring, section 3.1 |
| 5b | tolerance | on the `KonturEngine` constructor, with a default and a `...With tolerance` variant on the convenience functions; kept as is for now |
| 6 | Fable | both targets must be fast: raw typed arrays, no struct returns in hot loops, Node benchmarks (section 6) |
| 7 | winding | per edge ray casting was built first as the test oracle, O(E) propagation is implemented and shipped (section 4.6) |

Still at their defaults, not yet discussed: absolute tolerance `1e-6` per engine, open input
polylines fail, collinear output vertices preserved, no nesting tree in version 1,
`namespace Euclid`.
