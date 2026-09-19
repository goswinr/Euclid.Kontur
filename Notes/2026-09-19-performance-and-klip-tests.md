# 2026-09-19: performance review of the engine and the port of the Klip tests

Working notes of one session on the engine of Kontur. Five commits on `main`, on top of `3cd23c0`:

| Commit | Subject |
|---|---|
| `383d7b3` | Speed up the engine by a third on the polysXY dataset |
| `6a76df0` | Find the segment pairs with a sweep and prune instead of the tree |
| `4af1830` | Port the Klip tests to the Kontur API |
| `789fffd` | Add these notes |
| `fa68aff` | Port the Klip benchmarks and run the same fixtures on .NET and under Node |

The benchmark is the self union of the 96 noisy polygons of `Test/Scripts/data/polysXY.json` at ten scales,
`Test/Scripts/console/union-polysXY.mjs` under Node and `union-polysXY.fsx` on .NET, against Klip and Clipper2.
All results below are bit for bit identical to the results before the session at every scale, the printed areas
did not change by one digit.

## Findings

### Where the time went before the session

Measured per phase under Node at scale 1 with `Test/Scripts/console/phases-polysXY.mjs`, 390 input segments,
1566 candidate segment pairs, 735 vertices after intersection:

| Phase | Share |
|---|---|
| intersect: BVH dual traversal and pair classification | a third |
| cluster: a second BVH over all vertices plus a close pairs traversal | a quarter |
| segment BVH build | a tenth |
| winding propagation, half of it a sort of all vertices by X | a tenth |
| the three counting sorts of split, graph and rings | a fifth |

Two things stood out. The cluster phase built a tree over 735 points to find 140 pairs within 1e-6, and that
build alone cost three times the segment tree build. And the bare tree traversal of the intersect phase, node pops
and stack traffic, cost two thirds of that phase, the segment tests themselves only a third.

### Tight loops lie

The same traversal took 0.026 ms repeated by itself and 0.067 ms inside the pipeline. Repeating identical input
lets the branch predictor learn every data dependent branch and keeps the caches warm. Isolated phase timings
ranked the phases wrong and made a leaf size change look like a win that was a wash in the pipeline. Every number
in these notes is measured in place inside a full run. `phases-polysXY.mjs` does that, and DESIGN.md section 6
records the rule.

### The Fable output is not the problem

A hand written JavaScript replica of the traversal ran at the same speed as the Fable output inside the pipeline.
The `Arr.get` and `Arr.set` helpers of the earlier commit removed the bounds checked library call; what remained
was the algorithm, not the compiler.

### The leftmost seed of the winding propagation was not needed

Section 4.6 of the design seeded each component at its leftmost vertex so that all its edges point to +X. The ray
count of the seed wedge is the exact winding of the point just right of the seed, which lies in the wedge above
+X whatever directions the edges take, so any vertex works as the seed. That removed a sort of all vertices.

### Leaf size

Leaf size 8 instead of 4 on the segment tree built faster but tested more items, a wash on polysXY and on a 6000
segment stars case. Left at 4, and the tree is gone from the intersect phase anyway since `6a76df0`.

## Changes

### `383d7b3`: a third faster

- Cluster phase (`Src/Graph.fs`): the vertex pairs within tolerance come from a sweep instead of a tree. The
  vertex ids are sorted by column, `floor (x / (2 * tolerance))`, and then by Y; each vertex is compared with the
  following vertices of its column and with a window of the next column that only moves forward. Linear after
  the sort, no quadratic trap for a column of vertices sharing one X, the same pairs the tree found. The vertex
  tree is removed from `EngineState`.
- Pair classification (`Src/Intersect.fs`): the eight coordinates are read once and three cross products give
  the signed areas of every end against the other line. If no end is within tolerance of the other line, only a
  proper crossing is possible and its point is provably clear of all four ends, so that fast path does no
  endpoint distances. Crossing parameters are the same expressions as before, hence the identical results.
- Winding (`Src/Winding.fs`): seeds in id order, no sort.
- Buffers: one capacity check per appended event, sub segment and vertex instead of three ensure calls; the
  buffers are sized up front where the count is known, so a fresh engine grows in fewer steps; result
  polylines are created at their exact point count; Simplify shares one static empty clip.
- Docs: DESIGN.md 4.2, 4.4, 4.6, 5 and 6, CHANGELOG.md.
- Added `Test/Scripts/console/phases-polysXY.mjs`.

### `6a76df0`: sweep and prune broad phase, a quarter faster again

- `Src/Intersect.fs`: the segment rectangles, expanded by the tolerance, go into four flat arrays sorted by
  minimum X; every segment is paired with the following ones while their rectangles start before its own ends,
  keeping the pairs that overlap in Y. Pairs are visited with the smaller id first, so the crossing point is
  computed from the same segment as before. On polysXY the sweep does 8916 rectangle tests against 6117 for
  the tree, but they are two comparisons on sequential memory. On the stars case with 1.9 million Y tests it was
  still slightly faster than the tree, 23.4 against 25.2 ms.
- The segment tree is built only by the per edge ray casting oracle of the tests. The overlap traversal added in
  `383d7b3` for the tree was removed again as unused.
- Docs: DESIGN.md 4.2, 5, 6 and decision 3 of section 11, CHANGELOG.md.

### Results, reused engine, milliseconds per call

| Target, scale | Before | `383d7b3` | `6a76df0` | Klip | Clipper2 |
|---|---|---|---|---|---|
| Node, 0.001 | 0.46 | 0.31 | 0.22 | 0.51 | 0.06 |
| Node, 1 | 0.69 | 0.45 | 0.50 | 0.21 | 0.24 |
| Node, 1000 | 0.61 | 0.35 | 0.32 | 0.40 | 0.12 |
| Node, 1e6 | 0.61 | 0.38 | 0.31 | 0.37 | 0.37 |
| .NET, 0.001 | 0.69 | 0.48 | 0.39 | 0.61 | 0.03 |
| .NET, 1 | 0.95 | 0.59 | 0.46 | 0.63 | 0.48 |
| .NET, 1000 | 0.92 | 0.53 | 0.38 | 0.87 | 0.23 |
| .NET, 1e6 | 0.56 | 0.36 | 0.30 | 1.08 | 0.45 |

Fresh engine calls roughly halved. The Node scale 1 row is the recurring outlier of that script, where the reused
timing follows the fresh engine pass and pays its garbage; the phase profiler shows 0.33 ms there. Clipper2 at
the small scales returns no paths, its integer grid swallows the geometry, so those rows are not comparable.

### What is left on the table

At scale 1 under Node: intersect a third, cluster a sixth, the three counting sorts a quarter, winding a tenth.
The cluster sort is the next fixed cost, about 0.03 ms; a column hashed bucket structure could remove it at the
price of a hash table in the engine.

### `4af1830`: the Klip tests

`Test/TestKlip.fs` brings over the F# and TypeScript tests of `D:\Git\_Euclid_\Klip`, 243 new tests, the suite
went from 47 to 290 on .NET and on Node. The Clipper2 fixtures `Polygons.txt` and `PolytreeHoleOwner2.txt` are
copied into `Test/data-Clipper2` and read on either platform through the compile time source directory.

Brought over: the boolean operation cases; the 14 touching union cases with float noise on the seam at every
scale and rotation; the bridge union over 338 combinations of scale, shift and rotation; the sliver triangle of
Clipper2 issue 1067; the bit exact power of two scale equivariance and the decimal scale case; the tolerance
cases; the containment cases near a long diagonal; the PolyTree cases through orientation and `Kontur.Contains`;
the 195 fixtures of `Polygons.txt`, each against the expected area with the clipper2-ts schedule and against the
point in region oracle, all passing both; the hole ownership fixture.

Skipped, with the reasons in the file header: open paths, Z metadata, the Snap pre-pass, NoClip, the Klip
tolerance knobs, the internal predicate tests, and the translated triangle at 1e12.

Findings from the port:

- Klip removes collinear vertices, Kontur keeps every input vertex. Where Klip expects eight points the tests
  count corners. On the bridge union Klip counts eleven points; Kontur has the same eleven vertices, ten of them
  corners, since the shared vertex lies on the straight left side.
- The contour counts of the fixtures differ from the integer reference: about a tenth more on the intersection
  fixtures 120 to 158, Kontur splits contours touching at a vertex; up to a third fewer on the union fixtures 163
  to 179, Kontur merges polygons sharing an edge where Clipper keeps them apart. The areas and the oracle pass
  everywhere, so the count is checked within a band of a third plus two. DESIGN.md section 8 records this.
- A tolerance of one unit on a ten unit square legitimately moves the area by several units depending on the
  start vertex, since the first of consecutive near duplicates survives. Klip asserts an exact vertex set there,
  Kontur asserts one polygon within a tenth of the area.
- Quill runs the tests of a list in parallel. A shared engine across the fixture tests produced zero areas and
  wrong results at random; every test now creates its own engine.
- Ingest now fails on a NaN or infinite coordinate. A NaN already fails the closed path check of `Kontur.create`,
  an infinity fails at execution and the engine recovers for the next call.

### `fa68aff`: the Klip benchmarks, the same fixtures on both runtimes

`Test/Bench.fs` holds the fixtures of both Klip benchmark suites and the timing harness. The file is part of the
test project, so Fable compiles it to JavaScript with the tests; `bench-fixtures.fsx` loads it as source and
`bench-fixtures.mjs` imports the compiled JavaScript, so both runtimes run the identical workload. 25 cases:
the overlapping pairs, the complex polygons of 100, 500 and 2000 vertices, the grids of 25 and 100 rectangles,
the simple rectangles and four circles, the geo scale coordinates, and the dense random polygons of 100 and 500
edges from the BenchmarkDotNet suite. Not ported: the PolyTree cases, the reused instance cases (a
`KonturEngine` is always reused, that is what it is for) and the clipper2-wasm column.

Two details were needed to make the fixtures identical across the runtimes:

- Coordinates are rounded with `floor (v + 0.5)`, the rule of JavaScript's `Math.round`. `System.Math.Round`
  rounds a half to even, which Klip's own C# port of these fixtures documents as a known divergence from its
  JavaScript twin. Using the JavaScript rule on both targets removes it.
- The dense random polygons use a Lehmer generator, state times 16807 modulo `2^31 - 1`. The intermediate
  product stays below `2^53`, so the float arithmetic is exact on both targets. `System.Random` differs between
  Fable and .NET and between .NET versions, so it cannot produce a shared fixture.

**Verified**: the fixture checksum, the result path count and the result area printed per case are identical in
the .NET and the Node run for all 25 cases. Only the millisecond column differs. So the two runtimes really do
run the same workload and the engine computes the same result on both.

### Results of the ported fixtures

Milliseconds per call, reused engine, the mean of the fastest of five batches. A ratio below `1.00x` means the
other library is faster than Kontur.

| Case | .NET Kontur | .NET Klip | .NET Clipper2 | Node Kontur | Node Klip | Node clipper2-ts |
|---|---:|---:|---:|---:|---:|---:|
| union, 2 overlapping rectangles | 0.0010 | 0.0011 | 0.0013 | 0.0024 | 0.0025 | 0.0018 |
| union, 3 disjoint rectangles | 0.0015 | 0.0020 | 0.0024 | 0.0030 | 0.0024 | 0.0023 |
| union, medium complex polygon (100) | 0.0082 | 0.0051 | 0.0051 | 0.0156 | 0.0066 | 0.0060 |
| union, very large complex polygon (2000) | 0.2570 | 0.1074 | 0.1103 | 0.4164 | 0.1388 | 0.1445 |
| union, large grid of 100 rectangles | 0.1271 | 0.0576 | 0.0434 | 0.1970 | 0.0581 | 0.0607 |
| intersection, large overlapping polygons | 0.1027 | 0.0380 | 0.0395 | 0.1898 | 0.0474 | 0.0460 |
| union, geo scale complex polygon | 0.0084 | 0.0063 | 0.0066 | 0.0160 | 0.0070 | 0.0079 |
| union, 100 edge random polygons | 1.60 | 0.74 | 0.81 | 2.13 | 1.00 | 1.37 |
| union, 500 edge random polygons | 43.2 | 21.8 | 20.3 | 60.3 | 24.3 | 29.8 |

Findings:

- **On these fixtures Kontur is the slower library**, about `0.4x` to `0.6x` the speed of Klip and Clipper2 on
  .NET and about `0.3x` to `0.5x` under Node. That is the opposite of the polysXY result above, where Kontur
  wins on both runtimes. The two datasets are different problems: polysXY is noisy float data at ten scales,
  where an integer clipper has to scale and where Kontur' tolerance model is at home; these fixtures are
  well conditioned integer polygons with many intersections, which is exactly what a sweep line is built for.
  Both numbers are true, and quoting only one of them would be misleading.
- Kontur wins the smallest cases on .NET, two and three rectangles, where the reused engine has no setup cost.
- The dense random polygons are the worst case: 500 random points in an 800 by 600 box give thousands of
  crossings and thousands of output contours, and Kontur takes twice the time of Klip. The intersect phase
  dominates; this is the case to profile if the sweep is ever revisited.
- Node costs Kontur a factor of about 1.4 to 1.6 against .NET on the same fixture, and Klip about 1.2. Fable
  output is not free, but it is the same order.
- The geo scale fixture, coordinates up to `5.4e8`, works with the default tolerance of `1e-6`, which is only
  about eight float ulps at that magnitude, and both runtimes agree exactly.
- Only 2 of 25 cases disagree on the region area by more than half a percent, the two densest random ones, and
  there Klip and Clipper2 also disagree with each other (`62487` against `62310`). That is the tolerance model
  against the integer grid, as designed.
- **A methodological trap**: comparing a sum of *absolute* path areas across libraries is wrong as soon as a
  result has holes, since it counts every hole as positive. The first version of the cross check did that and
  flagged six false disagreements, because clipper2-ts's `areaPaths` returns the net signed area while the other
  two helpers summed absolute areas. The net signed area is the comparable quantity: all three libraries return
  positively oriented results with clockwise holes.

