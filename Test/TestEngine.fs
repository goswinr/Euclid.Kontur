module TestEngine

open System
open Euclid
open BoolOps
open TestUtil
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

#nowarn "52" // The value has been copied to ensure the original is not mutated by this operation or because the copy is implicit when returning a struct from a member and another member is then accessed

/// A closed Polyline2D from points. The first point is repeated at the end.
let private poly (pts: (float * float) list) =
    let p = Polyline2D (pts.Length + 1)
    for (x, y) in pts do p.AddXY (x, y)
    let (x0, y0) = pts.Head
    p.AddXY (x0, y0)
    p

/// A closed counter clockwise square from (x, y) with the given size.
let private square x y size =
    poly [ (x, y); (x + size, y); (x + size, y + size); (x, y + size) ]

let private area (s: Shape) = s.SignedArea

/// Checks that a result Shape is well formed: closed paths, at least 3 distinct points, Positive rule.
let private checkResult (r: Shape) =
    assertThat r.FillRule (tag "result rule" >> isEqualTo FillRule.Positive)
    for p in r.Paths do
        assertThat p.IsClosed (tag "result path is closed" >> isTrue)
        assertThat p.PointCount (tag "result path has at least 3 distinct points" >> isGreaterOrEqual 4)

/// A random star shaped simple polygon around (cx, cy), counter clockwise if ccw.
let private randomStar (rand: Random) cx cy radius corners ccw =
    let pts =
        [ for i in 0 .. corners - 1 do
            let a = 2.0 * Math.PI * float i / float corners
            let r = radius * (0.3 + 0.7 * rand.NextDouble ())
            (cx + r * cos a, cy + r * sin a) ]
    poly (if ccw then pts else List.rev pts)

/// A random polygon of random points, usually self intersecting.
let private randomTangle (rand: Random) cx cy radius corners =
    poly [ for _ in 1 .. corners do (cx + radius * (2.0 * rand.NextDouble () - 1.0), cy + radius * (2.0 * rand.NextDouble () - 1.0)) ]

/// The distance from a point to the nearest edge of any path of the Shape.
let private distToEdges (s: Shape) (pt: Pt) =
    let mutable d = Double.MaxValue
    for p in s.Paths do
        d <- min d (p.DistanceTo pt)
    d

/// Checks a result against the point in region oracle: for random points not too close to any input edge,
/// being inside the result must equal the combination of being inside subject and clip.
let private oracle (rand: Random) (subject: Shape) (clip: Shape) (op: ClipType) (result: Shape) =
    checkResult result
    let br = subject.BoundingRectangle.Union (if clip.IsEmpty then subject.BoundingRectangle else clip.BoundingRectangle)
    let r = br.Expand 2.0
    let mutable tested = 0
    for _ in 1 .. 400 do
        let pt = Pt (r.MinX + rand.NextDouble () * r.SizeX, r.MinY + rand.NextDouble () * r.SizeY)
        if distToEdges subject pt > 1e-3 && (clip.IsEmpty || distToEdges clip pt > 1e-3) then
            tested <- tested + 1
            let expected = ClipType.combine op (subject.Contains pt) (clip.Contains pt)
            assertThat (result.Contains pt) (tag $"point {pt.AsString} for {op}" >> isEqualTo expected)
    assertThat tested (tag "enough points tested" >> isGreaterThan 100)

/// Runs all four operations with one engine and checks them against the oracle and the area identities.
let private checkAll (rand: Random) (engine: BoolOpsEngine) (subject: Shape) (clip: Shape) =
    let uni = engine.Execute (subject, clip, ClipType.Union)
    let inter = engine.Execute (subject, clip, ClipType.Intersection)
    let diff = engine.Execute (subject, clip, ClipType.Difference)
    let xor = engine.Execute (subject, clip, ClipType.Xor)
    oracle rand subject clip ClipType.Union uni
    oracle rand subject clip ClipType.Intersection inter
    oracle rand subject clip ClipType.Difference diff
    oracle rand subject clip ClipType.Xor xor
    let sA = area (engine.Simplify subject)
    let sB = area (engine.Simplify clip)
    let acc = Accuracy.medium
    assertThat (area uni)  (tag "area of union"      >> isCloseTo acc (sA + sB - area inter))
    assertThat (area diff) (tag "area of difference" >> isCloseTo acc (sA - area inter))
    assertThat (area xor)  (tag "area of xor"        >> isCloseTo acc (sA + sB - 2.0 * area inter))

let tests =
    testList ("Engine", [

        test ("two overlapping squares", fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            let b = Shape.ofPolyline (square 5.0 5.0 10.0)
            let uni = BoolOps.union a b
            checkResult uni
            assertThat uni.PathCount (tag "union is one contour" >> isEqualTo 1)
            assertThat uni.Paths.[0].PointCount (tag "union has 8 corners" >> isEqualTo 9)
            assertThat (area uni) (tag "union area" >> isCloseTo Accuracy.high 175.0)
            let inter = BoolOps.intersection a b
            assertThat inter.PathCount (tag "intersection is one contour" >> isEqualTo 1)
            assertThat inter.Paths.[0].PointCount (tag "intersection has 4 corners" >> isEqualTo 5)
            assertThat (area inter) (tag "intersection area" >> isCloseTo Accuracy.high 25.0)
            let diff = BoolOps.difference a b
            assertThat diff.PathCount (tag "difference is one contour" >> isEqualTo 1)
            assertThat (area diff) (tag "difference area" >> isCloseTo Accuracy.high 75.0)
            let xor = BoolOps.xor a b
            assertThat xor.PathCount (tag "xor is two contours touching at the crossings" >> isEqualTo 2)
            assertThat (area xor) (tag "xor area" >> isCloseTo Accuracy.high 150.0)
            checkAll (Random 1) (BoolOpsEngine 1e-6) a b
        )

        test ("disjoint squares", fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 1.0)
            let b = Shape.ofPolyline (square 5.0 5.0 1.0)
            let uni = BoolOps.union a b
            assertThat uni.PathCount (tag "union keeps both" >> isEqualTo 2)
            assertThat (area uni) (tag "union area" >> isCloseTo Accuracy.high 2.0)
            assertThat (BoolOps.intersection a b).IsEmpty (tag "intersection is empty" >> isTrue)
            assertThat (area (BoolOps.difference a b)) (tag "difference is a" >> isCloseTo Accuracy.high 1.0)
            assertThat (area (BoolOps.xor a b)) (tag "xor is both" >> isCloseTo Accuracy.high 2.0)
        )

        test ("square minus nested square gives a hole", fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            let b = Shape.ofPolyline (square 3.0 3.0 4.0)
            let diff = BoolOps.difference a b
            checkResult diff
            assertThat diff.PathCount (tag "outer and hole" >> isEqualTo 2)
            assertThat (area diff) (tag "area with hole" >> isCloseTo Accuracy.high 84.0)
            let outer = diff.Paths |> Seq.find (fun p -> p.SignedArea > 0.0)
            let hole = diff.Paths |> Seq.find (fun p -> p.SignedArea < 0.0)
            assertThat outer.SignedArea (tag "outer is counter clockwise" >> isCloseTo Accuracy.high 100.0)
            assertThat hole.SignedArea (tag "hole is clockwise" >> isCloseTo Accuracy.high -16.0)
            assertThat (diff.Contains (Pt (5.0, 5.0))) (tag "hole is outside" >> isFalse)
            assertThat (diff.Contains (Pt (1.0, 1.0))) (tag "ring is inside" >> isTrue)
            assertThat (area (BoolOps.union a b)) (tag "union is the outer" >> isCloseTo Accuracy.high 100.0)
            assertThat (area (BoolOps.intersection a b)) (tag "intersection is the inner" >> isCloseTo Accuracy.high 16.0)
        )

        test ("identical squares", fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            let b = Shape.ofPolyline (square 0.0 0.0 10.0)
            assertThat (area (BoolOps.union a b)) (tag "union" >> isCloseTo Accuracy.high 100.0)
            assertThat (BoolOps.union a b).PathCount (tag "union is one path" >> isEqualTo 1)
            assertThat (area (BoolOps.intersection a b)) (tag "intersection" >> isCloseTo Accuracy.high 100.0)
            assertThat (BoolOps.difference a b).IsEmpty (tag "difference is empty" >> isTrue)
            assertThat (BoolOps.xor a b).IsEmpty (tag "xor is empty" >> isTrue)
        )

        test ("squares sharing an edge", fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            let b = Shape.ofPolyline (square 10.0 0.0 10.0)
            let uni = BoolOps.union a b
            checkResult uni
            assertThat uni.PathCount (tag "one contour" >> isEqualTo 1)
            assertThat (area uni) (tag "area" >> isCloseTo Accuracy.high 200.0)
            assertThat uni.Paths.[0].PointCount (tag "the two shared corners stay as collinear vertices" >> isEqualTo 7)
            assertThat (BoolOps.intersection a b).IsEmpty (tag "no overlap" >> isTrue)
            assertThat (area (BoolOps.difference a b)) (tag "difference is a" >> isCloseTo Accuracy.high 100.0)
        )

        test ("squares touching at a corner stay two contours", fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            let b = Shape.ofPolyline (square 10.0 10.0 10.0)
            let uni = BoolOps.union a b
            checkResult uni
            assertThat uni.PathCount (tag "two contours" >> isEqualTo 2)
            assertThat (area uni) (tag "area" >> isCloseTo Accuracy.high 200.0)
        )

        test ("bowtie simplifies into two lobes under both rules", fun _ ->
            let bowtie = poly [ (0.0, 0.0); (10.0, 10.0); (10.0, 0.0); (0.0, 10.0) ]
            for rule in [ FillRule.NonZero; FillRule.EvenOdd ] do
                let r = BoolOps.simplify (Shape.ofPolyline (bowtie, rule))
                checkResult r
                assertThat r.PathCount (tag $"two lobes under {rule}" >> isEqualTo 2)
                assertThat (area r) (tag $"area under {rule}" >> isCloseTo Accuracy.high 50.0)
                for p in r.Paths do
                    assertThat p.SignedArea (tag "both lobes are counter clockwise" >> isGreaterThan 0.0)
            let r = BoolOps.simplify (Shape.ofPolyline (bowtie, FillRule.Positive))
            assertThat r.PathCount (tag "only the counter clockwise lobe under Positive" >> isEqualTo 1)
            assertThat (area r) (tag "area under Positive" >> isCloseTo Accuracy.high 25.0)
        )

        test ("pentagram center depends on the fill rule", fun _ ->
            let star = poly [ for i in 0 .. 4 do
                                let a = Math.PI / 2.0 + float (i * 2) * 2.0 * Math.PI / 5.0
                                (10.0 * cos a, 10.0 * sin a) ]
            let center = Pt (0.0, 0.0)
            let nonZero = BoolOps.simplify (Shape.ofPolyline (star, FillRule.NonZero))
            let evenOdd = BoolOps.simplify (Shape.ofPolyline (star, FillRule.EvenOdd))
            checkResult nonZero
            checkResult evenOdd
            assertThat nonZero.PathCount (tag "non zero fills the center" >> isEqualTo 1)
            assertThat (nonZero.Contains center) (tag "center inside under non zero" >> isTrue)
            assertThat evenOdd.PathCount (tag "even odd leaves the five tips, separated at the crossings" >> isEqualTo 5)
            assertThat (evenOdd.Contains center) (tag "center outside under even odd" >> isFalse)
            assertThat (area nonZero) (tag "even odd area is smaller" >> isGreaterThan (area evenOdd))
            let tip = Pt (0.0, 9.0)
            assertThat (nonZero.Contains tip && evenOdd.Contains tip) (tag "tips are inside under both" >> isTrue)
        )

        test ("an even odd glyph unioned with a non zero outline in one pass", fun _ ->
            // a letter O: outer and inner square with the same orientation, a hole only under EvenOdd
            let glyph = Shape.create ([ square 0.0 0.0 10.0; square 3.0 3.0 4.0 ], FillRule.EvenOdd)
            let bar = Shape.ofPolyline (square 8.0 4.0 10.0, FillRule.NonZero)
            let uni = BoolOps.union glyph bar
            checkResult uni
            assertThat (uni.Contains (Pt (5.0, 5.0)))  (tag "the hole of the glyph survives" >> isFalse)
            assertThat (uni.Contains (Pt (15.0, 8.0))) (tag "the bar is inside" >> isTrue)
            assertThat (uni.Contains (Pt (9.0, 5.0)))  (tag "the overlap is inside" >> isTrue)
            assertThat (area uni) (tag "area" >> isCloseTo Accuracy.high (84.0 + 100.0 - 12.0))
            oracle (Random 2) glyph bar ClipType.Union uni
            // the same glyph under NonZero has no hole:
            let solid = Shape.create (glyph.Paths, FillRule.NonZero)
            assertThat (area (BoolOps.union solid bar)) (tag "no hole under non zero" >> isCloseTo Accuracy.high (100.0 + 100.0 - 12.0))
        )

        test ("input vertices come out unchanged", fun _ ->
            let a = Shape.ofPolyline (poly [ (0.1, 0.2); (10.3, 0.7); (9.9, 10.1); (0.4, 9.6) ])
            let b = Shape.ofPolyline (poly [ (5.5, 5.5); (15.5, 5.7); (15.1, 15.3); (5.2, 15.4) ])
            let uni = BoolOps.union a b
            let resultXYs = [ for p in uni.Paths do for i in 0 .. p.PointCount - 2 do (p.GetX i, p.GetY i) ]
            for (x, y) in [ (0.1, 0.2); (10.3, 0.7); (0.4, 9.6); (15.5, 5.7); (15.1, 15.3); (5.2, 15.4) ] do
                assertThat resultXYs (tag $"outer input corner {x}, {y} is in the result bit for bit" >> contain (x, y))
            assertThat uni.Paths.[0].PointCount (tag "6 input corners and 2 intersections" >> isEqualTo 9)
        )

        test ("a vertex within tolerance of an edge snaps onto it", fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            let b = Shape.ofPolyline (poly [ (5.0, 10.0 + 1e-9); (15.0, 12.0); (15.0, 20.0); (5.0, 20.0) ]) // touches the top edge of a within tolerance
            let uni = BoolOps.union a b
            checkResult uni
            assertThat uni.PathCount (tag "the shapes touch at one vertex, so the union is two contours meeting there" >> isEqualTo 2)
            assertThat (area uni) (tag "square plus trapezoid" >> isCloseTo Accuracy.high 190.0)
            assertThat (BoolOps.intersection a b).IsEmpty (tag "no area in common" >> isTrue)
            let top = uni.Paths |> Seq.find (fun p -> p.Contains (Pt (5.0, 5.0)))
            assertThat top.PointCount (tag "the square gained the snapped vertex on its top edge" >> isEqualTo 6)
            let xyIn = [ for p in uni.Paths do for i in 0 .. p.PointCount - 2 do (p.GetX i, p.GetY i) ]
            assertThat xyIn (tag "the input vertex is kept, not moved onto the edge" >> contain (5.0, 10.0 + 1e-9))
        )

        test ("empty shapes", fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            let e = Shape.empty FillRule.NonZero
            assertThat (area (BoolOps.union a e)) (tag "a union empty" >> isCloseTo Accuracy.high 100.0)
            assertThat (area (BoolOps.union e a)) (tag "empty union a" >> isCloseTo Accuracy.high 100.0)
            assertThat (BoolOps.intersection a e).IsEmpty (tag "a intersect empty" >> isTrue)
            assertThat (BoolOps.difference e a).IsEmpty (tag "empty minus a" >> isTrue)
            assertThat (area (BoolOps.difference a e)) (tag "a minus empty" >> isCloseTo Accuracy.high 100.0)
            assertThat (BoolOps.union e e).IsEmpty (tag "empty union empty" >> isTrue)
        )

        test ("unionAll merges many shapes with mixed rules", fun _ ->
            let shapes =
                [ Shape.create ([ square 0.0 0.0 10.0; square 3.0 3.0 4.0 ], FillRule.EvenOdd)
                  Shape.ofPolyline (square 8.0 0.0 10.0)
                  Shape.ofPolyline ((square 30.0 30.0 5.0).Reverse (), FillRule.NonZero)
                  Shape.ofPolyline (square 4.0 4.0 2.0) ]
            let all = BoolOps.unionAll shapes
            checkResult all
            assertThat (all.Contains (Pt (5.0, 5.0)))   (tag "the small square fills the hole" >> isTrue)
            assertThat (all.Contains (Pt (3.5, 5.0)))   (tag "the rest of the hole stays" >> isFalse)
            assertThat (all.Contains (Pt (32.0, 32.0))) (tag "the clockwise square counts under non zero" >> isTrue)
            assertThat (area all) (tag "area" >> isCloseTo Accuracy.high (84.0 + 100.0 - 20.0 + 25.0 + 4.0))
        )

        test ("random star polygons against the point oracle", fun _ ->
            let rand = Random 11
            let engine = BoolOpsEngine 1e-6
            for i in 1 .. 25 do
                let a = Shape.ofPolyline (randomStar rand 0.0 0.0 10.0 (5 + rand.Next 20) (i % 2 = 0), FillRule.NonZero)
                let b = Shape.ofPolyline (randomStar rand (rand.NextDouble () * 10.0) (rand.NextDouble () * 10.0) 8.0 (5 + rand.Next 20) (i % 3 = 0), FillRule.NonZero)
                checkAll rand engine a b
        )

        test ("random self intersecting polygons against the point oracle", fun _ ->
            let rand = Random 12
            let engine = BoolOpsEngine 1e-6
            for i in 1 .. 25 do
                let ruleA = if i % 2 = 0 then FillRule.NonZero else FillRule.EvenOdd
                let ruleB = if i % 3 = 0 then FillRule.NonZero else FillRule.EvenOdd
                let a = Shape.ofPolyline (randomTangle rand 0.0 0.0 10.0 (4 + rand.Next 10), ruleA)
                let b = Shape.ofPolyline (randomTangle rand 3.0 3.0 10.0 (4 + rand.Next 10), ruleB)
                checkAll rand engine a b
        )

        test ("many paths per shape against the point oracle", fun _ ->
            let rand = Random 13
            let engine = BoolOpsEngine 1e-6
            for _ in 1 .. 10 do
                let a = Shape.create ([ for _ in 1 .. 4 do randomStar rand (rand.NextDouble () * 20.0) (rand.NextDouble () * 20.0) 6.0 8 true ], FillRule.NonZero)
                let b = Shape.create ([ for _ in 1 .. 4 do randomTangle rand (rand.NextDouble () * 20.0) (rand.NextDouble () * 20.0) 6.0 6 ], FillRule.EvenOdd)
                checkAll rand engine a b
        )

        test ("the graph invariants hold after every random operation", fun _ ->
            let rand = Random 14
            let engine = BoolOpsEngine 1e-6
            for _ in 1 .. 20 do
                let a = Shape.ofPolyline (randomTangle rand 0.0 0.0 10.0 (4 + rand.Next 8))
                let b = Shape.ofPolyline (randomStar rand 2.0 2.0 9.0 (4 + rand.Next 8) true)
                engine.Execute (a, b, ClipType.Xor) |> ignore
                Graph.validate engine.State
        )

        test ("degenerate inputs", fun _ ->
            // all points collinear, zero area:
            let flat = Shape.ofPolyline (poly [ (0.0, 0.0); (5.0, 0.0); (10.0, 0.0); (5.0, 0.0) ])
            assertThat (BoolOps.simplify flat).IsEmpty (tag "a flat polygon has no area" >> isTrue)
            assertThat (area (BoolOps.union flat (Shape.ofPolyline (square 0.0 0.0 4.0)))) (tag "flat polygon adds nothing" >> isCloseTo Accuracy.high 16.0)
            // duplicate consecutive points and a spike:
            let spiky = Shape.ofPolyline (poly [ (0.0, 0.0); (0.0, 0.0); (10.0, 0.0); (10.0, 10.0); (15.0, 15.0); (10.0, 10.0); (0.0, 10.0) ])
            let r = BoolOps.simplify spiky
            checkResult r
            assertThat (area r) (tag "spike and duplicate removed" >> isCloseTo Accuracy.high 100.0)
            assertThat r.Paths.[0].PointCount (tag "just the square" >> isEqualTo 5)
            // a square traced twice: winding 2, filled under NonZero, empty under EvenOdd
            let twice = poly [ (0.0, 0.0); (10.0, 0.0); (10.0, 10.0); (0.0, 10.0); (0.0, 0.0); (10.0, 0.0); (10.0, 10.0); (0.0, 10.0) ]
            assertThat (area (BoolOps.simplify (Shape.ofPolyline (twice, FillRule.NonZero)))) (tag "twice under non zero" >> isCloseTo Accuracy.high 100.0)
            assertThat (BoolOps.simplify (Shape.ofPolyline (twice, FillRule.EvenOdd))).IsEmpty (tag "twice under even odd" >> isTrue)
            // a square and its reverse in one NonZero shape cancel, as two shapes they do not:
            let sq = square 0.0 0.0 10.0
            let both = Shape.create ([ sq; sq.Reverse () ], FillRule.NonZero)
            assertThat (BoolOps.simplify both).IsEmpty (tag "opposite copies cancel under non zero" >> isTrue)
            assertThat (area (BoolOps.union (Shape.ofPolyline sq) (Shape.ofPolyline (sq.Reverse ())))) (tag "as separate shapes both count" >> isCloseTo Accuracy.high 100.0)
            // tiny polygon below the tolerance:
            let tiny = Shape.ofPolyline (square 0.0 0.0 1e-8)
            assertThat (BoolOps.simplify tiny).IsEmpty (tag "smaller than the tolerance vanishes" >> isTrue)
        )

        test ("a segment within tolerance of the seed vertex does not flip the component", fun _ ->
            // The leftmost vertex L of the triangle seeds the winding propagation. The first vertex of the quad
            // is within tolerance of L and its first edge descends to the right, so the input segment crosses
            // the ray from L to +X just right of L, while in the graph that edge leaves L itself.
            // Counting it shifted every winding number of the component by one and inverted the result.
            let check (dx: float) (dy: float) (ux: float) (uy: float) =
                let tri  = poly [ (dx, dy); (dx + 10.0, dy - 5.0); (dx + 10.0, dy + 5.0) ]
                let quad = poly [ (dx + ux, dy + uy); (dx + 5.0, dy - 1.0); (dx + 5.0, dy + 3.0); (dx + 1.0, dy + 3.0) ]
                let r = BoolOps.simplify (Shape.create ([ tri; quad ], FillRule.NonZero))
                checkResult r
                assertThat r.PathCount (tag $"one contour at offset {dx}, {dy}" >> isEqualTo 1)
                assertThat (area r) (tag $"union area at offset {dx}, {dy}" >> isCloseTo Accuracy.medium 57.25)
            check 0.0 0.0 5e-7 5e-7      // the quad's first vertex is merged into L
            check 0.0 0.0 -1e-7 5e-7     // the same, the merged vertex lies left of L
            check 0.0 0.0 0.0 0.0        // control: the vertex is L itself
            check 1e5 1e5 5e-7 5e-7      // large coordinates
            check -1e3 2e3 -1e-7 5e-7
            // the edge only passes within tolerance of L, no shared vertex:
            let tri  = poly [ (0.0, 0.0); (10.0, -5.0); (10.0, 5.0) ]
            let quad = poly [ (-0.5, 5e-7 + 5e-8); (5.0, -5e-7); (5.0, 3.0); (1.0, 3.0) ]
            let a = Shape.ofPolyline tri
            let b = Shape.ofPolyline quad
            let r = BoolOps.union a b
            oracle (Random 17) a b ClipType.Union r
            assertThat r.PathCount (tag "passing edge gives one contour" >> isEqualTo 1)
            assertThat (area r) (tag "passing edge gives a positive area" >> isGreaterThan 57.25)
        )

        test ("propagated winding numbers agree with per edge ray casting on clean input", fun _ ->
            let rand = Random 15
            let engine = BoolOpsEngine 1e-6
            for i in 1 .. 10 do
                let a = Shape.create ([ randomStar rand 0.0 0.0 10.0 (5 + rand.Next 20) true; randomStar rand 3.0 1.0 4.0 7 false ], FillRule.NonZero)
                let b = Shape.ofPolyline (randomTangle rand 2.0 2.0 9.0 (4 + rand.Next 8), FillRule.EvenOdd)
                engine.Execute (a, b, ClipType.Xor) |> ignore
                let st = engine.State
                let byPropagation = Array.init st.GCount (fun e -> (st.WindLeftS.[e], st.WindLeftC.[e]))
                Winding.computeByRayCast st
                let byRayCast = Array.init st.GCount (fun e -> (st.WindLeftS.[e], st.WindLeftC.[e]))
                assertThat byPropagation (tag $"winding numbers of run {i}" >> isEqualTo byRayCast)
        )

        test ("two stars with thousands of thin spikes", fun _ ->
            let rand = Random 16
            let star cx cy corners =
                poly [ for i in 0 .. corners - 1 do
                        let a = 2.0 * Math.PI * float i / float corners
                        let r = 100.0 * (0.5 + 0.5 * rand.NextDouble ())
                        (cx + r * cos a, cy + r * sin a) ]
            let a = Shape.ofPolyline (star 0.0 0.0 3000)
            let b = Shape.ofPolyline (star 30.0 20.0 3000)
            let engine = BoolOpsEngine 1e-6
            checkAll rand engine a b
            Graph.validate engine.State
        )

        test ("engine rejects bad tolerances and open paths", fun _ ->
            assertThat (fun () -> BoolOpsEngine -1.0 |> ignore) (tag "negative" >> throws)
            assertThat (fun () -> BoolOpsEngine nan |> ignore) (tag "nan" >> throws)
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            a.Paths.[0].SetPt (4, Pt (1.0, 1.0)) // open it after the Shape was created
            assertThat (fun () -> BoolOps.simplify a |> ignore) (tag "opened path fails at execution" >> throws)
        )
    ])
