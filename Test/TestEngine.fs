module TestEngine

open System
open Euclid
open BoolOps

#if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
open Fable.Mocha
#else
open Expecto
#endif

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

let private shape rule (pls: Polyline2D list) = Shape.create (pls, rule)

let private area (s: Shape) = s.SignedArea

let private fails (f: unit -> 'T) =
    try
        f () |> ignore
        false
    with _ -> true

/// Checks that a result Shape is well formed: closed paths, at least 3 distinct points, Positive rule.
let private checkResult (r: Shape) =
    Expect.equal r.FillRule FillRule.Positive "result rule"
    for p in r.Paths do
        Expect.isTrue p.IsClosed "result path is closed"
        Expect.isTrue (p.PointCount >= 4) "result path has at least 3 distinct points"

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
    let r = (subject.BoundingRectangle.Union (if clip.IsEmpty then subject.BoundingRectangle else clip.BoundingRectangle)).Expand 2.0
    let mutable tested = 0
    for _ in 1 .. 400 do
        let pt = Pt (r.MinX + rand.NextDouble () * r.SizeX, r.MinY + rand.NextDouble () * r.SizeY)
        if distToEdges subject pt > 1e-3 && (clip.IsEmpty || distToEdges clip pt > 1e-3) then
            tested <- tested + 1
            let expected = ClipType.combine op (subject.Contains pt) (clip.Contains pt)
            Expect.equal (result.Contains pt) expected $"point {pt.AsString} for {op}"
    Expect.isTrue (tested > 100) "enough points tested"

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
    Expect.floatClose acc (area uni) (sA + sB - area inter) "area of union"
    Expect.floatClose acc (area diff) (sA - area inter) "area of difference"
    Expect.floatClose acc (area xor) (sA + sB - 2.0 * area inter) "area of xor"

let tests =
    testList "Engine" [

        testCase "two overlapping squares" <| fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            let b = Shape.ofPolyline (square 5.0 5.0 10.0)
            let uni = BoolOps.union a b
            checkResult uni
            Expect.equal uni.PathCount 1 "union is one contour"
            Expect.equal uni.Paths.[0].PointCount 9 "union has 8 corners"
            Expect.floatClose Accuracy.high (area uni) 175.0 "union area"
            let inter = BoolOps.intersection a b
            Expect.equal inter.PathCount 1 "intersection is one contour"
            Expect.equal inter.Paths.[0].PointCount 5 "intersection has 4 corners"
            Expect.floatClose Accuracy.high (area inter) 25.0 "intersection area"
            let diff = BoolOps.difference a b
            Expect.equal diff.PathCount 1 "difference is one contour"
            Expect.floatClose Accuracy.high (area diff) 75.0 "difference area"
            let xor = BoolOps.xor a b
            Expect.equal xor.PathCount 2 "xor is two contours touching at the crossings"
            Expect.floatClose Accuracy.high (area xor) 150.0 "xor area"
            checkAll (Random 1) (BoolOpsEngine 1e-6) a b

        testCase "disjoint squares" <| fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 1.0)
            let b = Shape.ofPolyline (square 5.0 5.0 1.0)
            let uni = BoolOps.union a b
            Expect.equal uni.PathCount 2 "union keeps both"
            Expect.floatClose Accuracy.high (area uni) 2.0 "union area"
            Expect.isTrue (BoolOps.intersection a b).IsEmpty "intersection is empty"
            Expect.floatClose Accuracy.high (area (BoolOps.difference a b)) 1.0 "difference is a"
            Expect.floatClose Accuracy.high (area (BoolOps.xor a b)) 2.0 "xor is both"

        testCase "square minus nested square gives a hole" <| fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            let b = Shape.ofPolyline (square 3.0 3.0 4.0)
            let diff = BoolOps.difference a b
            checkResult diff
            Expect.equal diff.PathCount 2 "outer and hole"
            Expect.floatClose Accuracy.high (area diff) 84.0 "area with hole"
            let outer = diff.Paths |> Seq.find (fun p -> p.SignedArea > 0.0)
            let hole = diff.Paths |> Seq.find (fun p -> p.SignedArea < 0.0)
            Expect.floatClose Accuracy.high outer.SignedArea 100.0 "outer is counter clockwise"
            Expect.floatClose Accuracy.high hole.SignedArea -16.0 "hole is clockwise"
            Expect.isFalse (diff.Contains (Pt (5.0, 5.0))) "hole is outside"
            Expect.isTrue (diff.Contains (Pt (1.0, 1.0))) "ring is inside"
            Expect.floatClose Accuracy.high (area (BoolOps.union a b)) 100.0 "union is the outer"
            Expect.floatClose Accuracy.high (area (BoolOps.intersection a b)) 16.0 "intersection is the inner"

        testCase "identical squares" <| fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            let b = Shape.ofPolyline (square 0.0 0.0 10.0)
            Expect.floatClose Accuracy.high (area (BoolOps.union a b)) 100.0 "union"
            Expect.equal (BoolOps.union a b).PathCount 1 "union is one path"
            Expect.floatClose Accuracy.high (area (BoolOps.intersection a b)) 100.0 "intersection"
            Expect.isTrue (BoolOps.difference a b).IsEmpty "difference is empty"
            Expect.isTrue (BoolOps.xor a b).IsEmpty "xor is empty"

        testCase "squares sharing an edge" <| fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            let b = Shape.ofPolyline (square 10.0 0.0 10.0)
            let uni = BoolOps.union a b
            checkResult uni
            Expect.equal uni.PathCount 1 "one contour"
            Expect.floatClose Accuracy.high (area uni) 200.0 "area"
            Expect.equal uni.Paths.[0].PointCount 7 "the two shared corners stay as collinear vertices"
            Expect.isTrue (BoolOps.intersection a b).IsEmpty "no overlap"
            Expect.floatClose Accuracy.high (area (BoolOps.difference a b)) 100.0 "difference is a"

        testCase "squares touching at a corner stay two contours" <| fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            let b = Shape.ofPolyline (square 10.0 10.0 10.0)
            let uni = BoolOps.union a b
            checkResult uni
            Expect.equal uni.PathCount 2 "two contours"
            Expect.floatClose Accuracy.high (area uni) 200.0 "area"

        testCase "bowtie simplifies into two lobes under both rules" <| fun _ ->
            let bowtie = poly [ (0.0, 0.0); (10.0, 10.0); (10.0, 0.0); (0.0, 10.0) ]
            for rule in [ FillRule.NonZero; FillRule.EvenOdd ] do
                let r = BoolOps.simplify (Shape.ofPolyline (bowtie, rule))
                checkResult r
                Expect.equal r.PathCount 2 $"two lobes under {rule}"
                Expect.floatClose Accuracy.high (area r) 50.0 $"area under {rule}"
                for p in r.Paths do
                    Expect.isTrue (p.SignedArea > 0.0) "both lobes are counter clockwise"
            let r = BoolOps.simplify (Shape.ofPolyline (bowtie, FillRule.Positive))
            Expect.equal r.PathCount 1 "only the counter clockwise lobe under Positive"
            Expect.floatClose Accuracy.high (area r) 25.0 "area under Positive"

        testCase "pentagram center depends on the fill rule" <| fun _ ->
            let star = poly [ for i in 0 .. 4 do
                                let a = Math.PI / 2.0 + float (i * 2) * 2.0 * Math.PI / 5.0
                                (10.0 * cos a, 10.0 * sin a) ]
            let center = Pt (0.0, 0.0)
            let nonZero = BoolOps.simplify (Shape.ofPolyline (star, FillRule.NonZero))
            let evenOdd = BoolOps.simplify (Shape.ofPolyline (star, FillRule.EvenOdd))
            checkResult nonZero
            checkResult evenOdd
            Expect.equal nonZero.PathCount 1 "non zero fills the center"
            Expect.isTrue (nonZero.Contains center) "center inside under non zero"
            Expect.equal evenOdd.PathCount 5 "even odd leaves the five tips, separated at the crossings"
            Expect.isFalse (evenOdd.Contains center) "center outside under even odd"
            Expect.isTrue (area nonZero > area evenOdd) "even odd area is smaller"
            let tip = Pt (0.0, 9.0)
            Expect.isTrue (nonZero.Contains tip && evenOdd.Contains tip) "tips are inside under both"

        testCase "an even odd glyph unioned with a non zero outline in one pass" <| fun _ ->
            // a letter O: outer and inner square with the same orientation, a hole only under EvenOdd
            let glyph = Shape.create ([ square 0.0 0.0 10.0; square 3.0 3.0 4.0 ], FillRule.EvenOdd)
            let bar = Shape.ofPolyline (square 8.0 4.0 10.0, FillRule.NonZero)
            let uni = BoolOps.union glyph bar
            checkResult uni
            Expect.isFalse (uni.Contains (Pt (5.0, 5.0))) "the hole of the glyph survives"
            Expect.isTrue (uni.Contains (Pt (15.0, 8.0))) "the bar is inside"
            Expect.isTrue (uni.Contains (Pt (9.0, 5.0))) "the overlap is inside"
            Expect.floatClose Accuracy.high (area uni) (84.0 + 100.0 - 12.0) "area"
            oracle (Random 2) glyph bar ClipType.Union uni
            // the same glyph under NonZero has no hole:
            let solid = Shape.create (glyph.Paths, FillRule.NonZero)
            Expect.floatClose Accuracy.high (area (BoolOps.union solid bar)) (100.0 + 100.0 - 12.0) "no hole under non zero"

        testCase "input vertices come out unchanged" <| fun _ ->
            let a = Shape.ofPolyline (poly [ (0.1, 0.2); (10.3, 0.7); (9.9, 10.1); (0.4, 9.6) ])
            let b = Shape.ofPolyline (poly [ (5.5, 5.5); (15.5, 5.7); (15.1, 15.3); (5.2, 15.4) ])
            let uni = BoolOps.union a b
            let resultXYs = [ for p in uni.Paths do for i in 0 .. p.PointCount - 2 do (p.GetX i, p.GetY i) ]
            for (x, y) in [ (0.1, 0.2); (10.3, 0.7); (0.4, 9.6); (15.5, 5.7); (15.1, 15.3); (5.2, 15.4) ] do
                Expect.isTrue (List.contains (x, y) resultXYs) $"outer input corner {x}, {y} is in the result bit for bit"
            Expect.equal uni.Paths.[0].PointCount 9 "6 input corners and 2 intersections"

        testCase "a vertex within tolerance of an edge snaps onto it" <| fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            let b = Shape.ofPolyline (poly [ (5.0, 10.0 + 1e-9); (15.0, 12.0); (15.0, 20.0); (5.0, 20.0) ]) // touches the top edge of a within tolerance
            let uni = BoolOps.union a b
            checkResult uni
            Expect.equal uni.PathCount 2 "the shapes touch at one vertex, so the union is two contours meeting there"
            Expect.floatClose Accuracy.high (area uni) 190.0 "square plus trapezoid"
            Expect.isTrue (BoolOps.intersection a b).IsEmpty "no area in common"
            let top = uni.Paths |> Seq.find (fun p -> p.Contains (Pt (5.0, 5.0)))
            Expect.equal top.PointCount 6 "the square gained the snapped vertex on its top edge"
            let xyIn = [ for p in uni.Paths do for i in 0 .. p.PointCount - 2 do (p.GetX i, p.GetY i) ]
            Expect.isTrue (List.contains (5.0, 10.0 + 1e-9) xyIn) "the input vertex is kept, not moved onto the edge"

        testCase "empty shapes" <| fun _ ->
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            let e = Shape.empty FillRule.NonZero
            Expect.floatClose Accuracy.high (area (BoolOps.union a e)) 100.0 "a union empty"
            Expect.floatClose Accuracy.high (area (BoolOps.union e a)) 100.0 "empty union a"
            Expect.isTrue (BoolOps.intersection a e).IsEmpty "a intersect empty"
            Expect.isTrue (BoolOps.difference e a).IsEmpty "empty minus a"
            Expect.floatClose Accuracy.high (area (BoolOps.difference a e)) 100.0 "a minus empty"
            Expect.isTrue (BoolOps.union e e).IsEmpty "empty union empty"

        testCase "unionAll merges many shapes with mixed rules" <| fun _ ->
            let shapes =
                [ Shape.create ([ square 0.0 0.0 10.0; square 3.0 3.0 4.0 ], FillRule.EvenOdd)
                  Shape.ofPolyline (square 8.0 0.0 10.0)
                  Shape.ofPolyline ((square 30.0 30.0 5.0).Reverse (), FillRule.NonZero)
                  Shape.ofPolyline (square 4.0 4.0 2.0) ]
            let all = BoolOps.unionAll shapes
            checkResult all
            Expect.isTrue (all.Contains (Pt (5.0, 5.0))) "the small square fills the hole"
            Expect.isFalse (all.Contains (Pt (3.5, 5.0))) "the rest of the hole stays"
            Expect.isTrue (all.Contains (Pt (32.0, 32.0))) "the clockwise square counts under non zero"
            Expect.floatClose Accuracy.high (area all) (84.0 + 100.0 - 20.0 + 25.0 + 4.0) "area"

        testCase "random star polygons against the point oracle" <| fun _ ->
            let rand = Random 11
            let engine = BoolOpsEngine 1e-6
            for i in 1 .. 25 do
                let a = Shape.ofPolyline (randomStar rand 0.0 0.0 10.0 (5 + rand.Next 20) (i % 2 = 0), FillRule.NonZero)
                let b = Shape.ofPolyline (randomStar rand (rand.NextDouble () * 10.0) (rand.NextDouble () * 10.0) 8.0 (5 + rand.Next 20) (i % 3 = 0), FillRule.NonZero)
                checkAll rand engine a b

        testCase "random self intersecting polygons against the point oracle" <| fun _ ->
            let rand = Random 12
            let engine = BoolOpsEngine 1e-6
            for i in 1 .. 25 do
                let ruleA = if i % 2 = 0 then FillRule.NonZero else FillRule.EvenOdd
                let ruleB = if i % 3 = 0 then FillRule.NonZero else FillRule.EvenOdd
                let a = Shape.ofPolyline (randomTangle rand 0.0 0.0 10.0 (4 + rand.Next 10), ruleA)
                let b = Shape.ofPolyline (randomTangle rand 3.0 3.0 10.0 (4 + rand.Next 10), ruleB)
                checkAll rand engine a b

        testCase "many paths per shape against the point oracle" <| fun _ ->
            let rand = Random 13
            let engine = BoolOpsEngine 1e-6
            for _ in 1 .. 10 do
                let a = Shape.create ([ for _ in 1 .. 4 do randomStar rand (rand.NextDouble () * 20.0) (rand.NextDouble () * 20.0) 6.0 8 true ], FillRule.NonZero)
                let b = Shape.create ([ for _ in 1 .. 4 do randomTangle rand (rand.NextDouble () * 20.0) (rand.NextDouble () * 20.0) 6.0 6 ], FillRule.EvenOdd)
                checkAll rand engine a b

        testCase "the graph invariants hold after every random operation" <| fun _ ->
            let rand = Random 14
            let engine = BoolOpsEngine 1e-6
            for _ in 1 .. 20 do
                let a = Shape.ofPolyline (randomTangle rand 0.0 0.0 10.0 (4 + rand.Next 8))
                let b = Shape.ofPolyline (randomStar rand 2.0 2.0 9.0 (4 + rand.Next 8) true)
                engine.Execute (a, b, ClipType.Xor) |> ignore
                Graph.validate engine.State

        testCase "degenerate inputs" <| fun _ ->
            // all points collinear, zero area:
            let flat = Shape.ofPolyline (poly [ (0.0, 0.0); (5.0, 0.0); (10.0, 0.0); (5.0, 0.0) ])
            Expect.isTrue (BoolOps.simplify flat).IsEmpty "a flat polygon has no area"
            Expect.floatClose Accuracy.high (area (BoolOps.union flat (Shape.ofPolyline (square 0.0 0.0 4.0)))) 16.0 "flat polygon adds nothing"
            // duplicate consecutive points and a spike:
            let spiky = Shape.ofPolyline (poly [ (0.0, 0.0); (0.0, 0.0); (10.0, 0.0); (10.0, 10.0); (15.0, 15.0); (10.0, 10.0); (0.0, 10.0) ])
            let r = BoolOps.simplify spiky
            checkResult r
            Expect.floatClose Accuracy.high (area r) 100.0 "spike and duplicate removed"
            Expect.equal r.Paths.[0].PointCount 5 "just the square"
            // a square traced twice: winding 2, filled under NonZero, empty under EvenOdd
            let twice = poly [ (0.0, 0.0); (10.0, 0.0); (10.0, 10.0); (0.0, 10.0); (0.0, 0.0); (10.0, 0.0); (10.0, 10.0); (0.0, 10.0) ]
            Expect.floatClose Accuracy.high (area (BoolOps.simplify (Shape.ofPolyline (twice, FillRule.NonZero)))) 100.0 "twice under non zero"
            Expect.isTrue (BoolOps.simplify (Shape.ofPolyline (twice, FillRule.EvenOdd))).IsEmpty "twice under even odd"
            // a square and its reverse in one NonZero shape cancel, as two shapes they do not:
            let sq = square 0.0 0.0 10.0
            let both = Shape.create ([ sq; sq.Reverse () ], FillRule.NonZero)
            Expect.isTrue (BoolOps.simplify both).IsEmpty "opposite copies cancel under non zero"
            Expect.floatClose Accuracy.high (area (BoolOps.union (Shape.ofPolyline sq) (Shape.ofPolyline (sq.Reverse ())))) 100.0 "as separate shapes both count"
            // tiny polygon below the tolerance:
            let tiny = Shape.ofPolyline (square 0.0 0.0 1e-8)
            Expect.isTrue (BoolOps.simplify tiny).IsEmpty "smaller than the tolerance vanishes"

        testCase "propagated winding numbers agree with per edge ray casting on clean input" <| fun _ ->
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
                Expect.equal byPropagation byRayCast $"winding numbers of run {i}"

        testCase "two stars with thousands of thin spikes" <| fun _ ->
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

        testCase "engine rejects bad tolerances and open paths" <| fun _ ->
            Expect.isTrue (fails (fun () -> BoolOpsEngine -1.0)) "negative"
            Expect.isTrue (fails (fun () -> BoolOpsEngine nan)) "nan"
            let a = Shape.ofPolyline (square 0.0 0.0 10.0)
            a.Paths.[0].SetPt (4, Pt (1.0, 1.0)) // open it after the Shape was created
            Expect.isTrue (fails (fun () -> BoolOps.simplify a)) "opened path fails at execution"
    ]
