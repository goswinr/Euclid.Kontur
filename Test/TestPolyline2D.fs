module TestPolyline2D

open Euclid
open TestUtil
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

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

/// The sum of the signed areas of all contours, holes count negative.
let private area (paths: ResizeArray<Polyline2D>) =
    let mutable a = 0.0
    for p in paths do
        a <- a + p.SignedArea
    a

/// Checks that the result contours are closed and have at least 3 distinct points.
let private checkResult (paths: ResizeArray<Polyline2D>) =
    for p in paths do
        assertThat p.IsClosed (tag "result path is closed" >> isTrue)
        assertThat p.PointCount (tag "result path has at least 3 distinct points" >> isGreaterOrEqual 4)

let tests =
    testList ("Polyline2D extensions", [

        test ("instance members on two overlapping squares", fun _ ->
            let a = square 0.0 0.0 10.0
            let b = square 5.0 5.0 10.0
            let uni = a.Union b
            checkResult uni
            assertThat uni.Count (tag "union is one contour" >> isEqualTo 1)
            assertThat (area uni) (tag "union area" >> isCloseTo Accuracy.high 175.0)
            let inter = a.Intersection b
            assertThat inter.Count (tag "intersection is one contour" >> isEqualTo 1)
            assertThat (area inter) (tag "intersection area" >> isCloseTo Accuracy.high 25.0)
            assertThat (area (a.Difference b)) (tag "difference area" >> isCloseTo Accuracy.high 75.0)
            assertThat (area (b.Difference a)) (tag "difference the other way" >> isCloseTo Accuracy.high 75.0)
            let xor = a.Xor b
            assertThat xor.Count (tag "xor is two contours" >> isEqualTo 2)
            assertThat (area xor) (tag "xor area" >> isCloseTo Accuracy.high 150.0)
        )

        test ("static members on two overlapping squares", fun _ ->
            let a = square 0.0 0.0 10.0
            let b = square 5.0 5.0 10.0
            assertThat (area (Polyline2D.union a b)) (tag "union area" >> isCloseTo Accuracy.high 175.0)
            assertThat (area (Polyline2D.intersection a b)) (tag "intersection area" >> isCloseTo Accuracy.high 25.0)
            assertThat (area (Polyline2D.difference a b)) (tag "difference area" >> isCloseTo Accuracy.high 75.0)
            assertThat (area (Polyline2D.xor a b)) (tag "xor area" >> isCloseTo Accuracy.high 150.0)
        )

        test ("the With variants take the tolerance first", fun _ ->
            let a = square 0.0 0.0 10.0
            let b = square 5.0 5.0 10.0
            assertThat (area (a.UnionWith 1e-4 b)) (tag "UnionWith" >> isCloseTo Accuracy.high 175.0)
            assertThat (area (a.IntersectionWith 1e-4 b)) (tag "IntersectionWith" >> isCloseTo Accuracy.high 25.0)
            assertThat (area (a.DifferenceWith 1e-4 b)) (tag "DifferenceWith" >> isCloseTo Accuracy.high 75.0)
            assertThat (area (a.XorWith 1e-4 b)) (tag "XorWith" >> isCloseTo Accuracy.high 150.0)
            assertThat (area (Polyline2D.unionWith 1e-4 a b)) (tag "unionWith" >> isCloseTo Accuracy.high 175.0)
            assertThat (area (Polyline2D.intersectionWith 1e-4 a b)) (tag "intersectionWith" >> isCloseTo Accuracy.high 25.0)
            assertThat (area (Polyline2D.differenceWith 1e-4 a b)) (tag "differenceWith" >> isCloseTo Accuracy.high 75.0)
            assertThat (area (Polyline2D.xorWith 1e-4 a b)) (tag "xorWith" >> isCloseTo Accuracy.high 150.0)
        )

        test ("the tolerance decides if a gap is bridged", fun _ ->
            let a = square 0.0 0.0 10.0
            let b = square 10.001 0.0 10.0 // a gap of 0.001
            assertThat (a.Union b).Count (tag "default tolerance keeps both squares apart" >> isEqualTo 2)
            assertThat (a.UnionWith 0.01 b).Count (tag "a bigger tolerance merges them" >> isEqualTo 1)
            assertThat (Polyline2D.unionMany [ a; b ]).Count (tag "unionMany with the default tolerance" >> isEqualTo 2)
            assertThat (Polyline2D.unionManyWith 0.01 [ a; b ]).Count (tag "unionManyWith merges them" >> isEqualTo 1)
        )

        test ("the orientation of the inputs does not matter", fun _ ->
            let a = square 0.0 0.0 10.0
            let b = (square 5.0 5.0 10.0).Reverse () // clockwise
            let uni = a.Union b
            assertThat (area uni) (tag "union with a clockwise square" >> isCloseTo Accuracy.high 175.0)
            assertThat uni.[0].IsCounterClockwise (tag "outer contour is counter clockwise" >> isTrue)
            assertThat (area (b.Intersection a)) (tag "intersection from a clockwise square" >> isCloseTo Accuracy.high 25.0)
            assertThat (area (b.Difference a)) (tag "difference from a clockwise square" >> isCloseTo Accuracy.high 75.0)
        )

        test ("difference with a nested square gives a hole", fun _ ->
            let diff = (square 0.0 0.0 10.0).Difference (square 3.0 3.0 4.0)
            checkResult diff
            assertThat diff.Count (tag "outer contour and hole" >> isEqualTo 2)
            assertThat (area diff) (tag "100 - 16" >> isCloseTo Accuracy.high 84.0)
        )

        test ("disjoint squares do not intersect", fun _ ->
            let a = square 0.0 0.0 1.0
            let b = square 5.0 5.0 1.0
            assertThat (a.Intersection b).Count (tag "intersection is empty" >> isEqualTo 0)
            assertThat (a.Union b).Count (tag "union keeps both" >> isEqualTo 2)
        )

        test ("Simplify resolves a bowtie", fun _ ->
            let bowtie = poly [ (0.0, 0.0); (10.0, 10.0); (10.0, 0.0); (0.0, 10.0) ]
            let r = bowtie.Simplify ()
            checkResult r
            assertThat r.Count (tag "two triangles" >> isEqualTo 2)
            assertThat (area r) (tag "both triangles are filled" >> isCloseTo Accuracy.high 50.0)
            assertThat (area (bowtie.SimplifyWith 1e-4)) (tag "SimplifyWith" >> isCloseTo Accuracy.high 50.0)
            assertThat (area (Polyline2D.simplify bowtie)) (tag "simplify" >> isCloseTo Accuracy.high 50.0)
            assertThat (area (Polyline2D.simplifyWith 1e-4 bowtie)) (tag "simplifyWith" >> isCloseTo Accuracy.high 50.0)
        )

        test ("UnionMany merges many polylines of mixed orientation", fun _ ->
            let a = square 0.0 0.0 10.0
            let b = (square 5.0 0.0 10.0).Reverse () // clockwise, would cancel the overlap in one NonZero Kontur
            let c = square 10.0 0.0 10.0
            let far = square 100.0 100.0 2.0
            let r = a.UnionMany [ b; c; far ]
            checkResult r
            assertThat r.Count (tag "one merged bar and the far square" >> isEqualTo 2)
            assertThat (area r) (tag "20 * 10 + 4" >> isCloseTo Accuracy.high 204.0)
            assertThat (area (a.UnionManyWith 1e-4 [ b; c; far ])) (tag "UnionManyWith" >> isCloseTo Accuracy.high 204.0)
            assertThat (area (Polyline2D.unionMany [ a; b; c; far ])) (tag "unionMany" >> isCloseTo Accuracy.high 204.0)
            assertThat (area (Polyline2D.unionManyWith 1e-4 [ a; b; c; far ])) (tag "unionManyWith" >> isCloseTo Accuracy.high 204.0)
            assertThat (area (a.UnionMany [])) (tag "no others simplifies this polyline" >> isCloseTo Accuracy.high 100.0)
            assertThat (Polyline2D.unionMany []).Count (tag "union of nothing is empty" >> isEqualTo 0)
        )

        test ("the results match the Kontur module", fun _ ->
            let a = poly [ (0.0, 0.0); (8.0, 1.0); (9.0, 7.0); (4.0, 4.0); (1.0, 9.0) ]
            let b = poly [ (3.0, -2.0); (12.0, 3.0); (6.0, 11.0); (5.0, 2.0) ]
            let ka = Kontur.createSingleton a
            let kb = Kontur.createSingleton b
            assertThat (area (a.Union b)) (tag "union" >> isEqualTo (Kontur.union ka kb).SignedArea)
            assertThat (area (a.Intersection b)) (tag "intersection" >> isEqualTo (Kontur.intersection ka kb).SignedArea)
            assertThat (area (a.Difference b)) (tag "difference" >> isEqualTo (Kontur.difference ka kb).SignedArea)
            assertThat (area (a.Xor b)) (tag "xor" >> isEqualTo (Kontur.xor ka kb).SignedArea)
            assertThat (area (a.UnionMany [ b ])) (tag "union many" >> isEqualTo (Kontur.unionAll [ ka; kb ]).SignedArea)
        )

        test ("open polylines and bad tolerances fail", fun _ ->
            let a = square 0.0 0.0 10.0
            let openPath = Polyline2D 3
            openPath.AddXY (0.0, 0.0)
            openPath.AddXY (1.0, 0.0)
            openPath.AddXY (1.0, 1.0)
            assertThat (fun () -> a.Union openPath |> ignore) (tag "open other" >> throws)
            assertThat (fun () -> openPath.Union a |> ignore) (tag "open subject" >> throws)
            assertThat (fun () -> openPath.Simplify () |> ignore) (tag "open simplify" >> throws)
            assertThat (fun () -> a.UnionMany [ square 1.0 1.0 2.0; openPath ] |> ignore) (tag "open path among many" >> throws)
            assertThat (fun () -> a.UnionWith (-1.0) (square 1.0 1.0 2.0) |> ignore) (tag "negative tolerance" >> throws)
        )
    ])
