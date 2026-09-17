module TestShape

open Euclid
open BoolOps

#if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
open Fable.Mocha
#else
open Expecto
#endif

/// A closed counter clockwise square from (x, y) with the given size.
let private square x y size =
    let p = Polyline2D 5
    p.AddXY (x, y)
    p.AddXY (x + size, y)
    p.AddXY (x + size, y + size)
    p.AddXY (x, y + size)
    p.AddXY (x, y)
    p

let private fails (f: unit -> 'T) =
    try
        f () |> ignore
        false
    with _ -> true

let tests =
    testList "Shape" [

        testCase "create keeps the paths and the fill rule" <| fun _ ->
            let a = square 0.0 0.0 10.0
            let b = square 2.0 2.0 2.0
            let s = Shape.create ([a; b], FillRule.EvenOdd)
            Expect.equal s.PathCount 2 "two paths"
            Expect.equal s.FillRule FillRule.EvenOdd "fill rule"
            Expect.isFalse s.IsEmpty "not empty"
            Expect.isTrue (obj.ReferenceEquals (s.Paths.[0], a)) "paths are not copied"

        testCase "ofPolyline defaults to NonZero" <| fun _ ->
            let s = Shape.ofPolyline (square 0.0 0.0 1.0)
            Expect.equal s.FillRule FillRule.NonZero "default rule"
            Expect.equal s.PathCount 1 "one path"

        testCase "open or too short paths fail" <| fun _ ->
            let openPath = Polyline2D 3
            openPath.AddXY (0.0, 0.0)
            openPath.AddXY (1.0, 0.0)
            openPath.AddXY (1.0, 1.0)
            Expect.isTrue (fails (fun () -> Shape.ofPolyline openPath)) "open path"
            let short = Polyline2D 3
            short.AddXY (0.0, 0.0)
            short.AddXY (1.0, 0.0)
            short.AddXY (0.0, 0.0)
            Expect.isTrue (fails (fun () -> Shape.create ([short], FillRule.NonZero))) "two distinct points"

        testCase "bounding rectangle spans all paths" <| fun _ ->
            let s = Shape.create ([square 0.0 0.0 1.0; square 5.0 -3.0 2.0], FillRule.NonZero)
            let r = s.BoundingRectangle
            Expect.equal r.MinX 0.0 "minX"
            Expect.equal r.MinY -3.0 "minY"
            Expect.equal r.MaxX 7.0 "maxX"
            Expect.equal r.MaxY 1.0 "maxY"
            Expect.isTrue (fails (fun () -> (Shape.empty FillRule.NonZero).BoundingRectangle)) "empty shape has no rectangle"

        testCase "winding number and Contains follow the fill rule" <| fun _ ->
            let outer = square 0.0 0.0 10.0
            let inner = square 3.0 3.0 4.0 // same orientation, nested
            let evenOdd = Shape.create ([outer; inner], FillRule.EvenOdd)
            let nonZero = Shape.create ([outer; inner], FillRule.NonZero)
            let inHole = Pt (5.0, 5.0)
            let inRing = Pt (1.0, 1.0)
            let outside = Pt (20.0, 5.0)
            Expect.equal (evenOdd.WindingNumber inHole) 2 "winding in the nested square"
            Expect.equal (evenOdd.WindingNumber inRing) 1 "winding in the ring"
            Expect.equal (evenOdd.WindingNumber outside) 0 "winding outside"
            Expect.isFalse (evenOdd.Contains inHole) "even odd: nested square is a hole"
            Expect.isTrue  (nonZero.Contains inHole) "non zero: nested square is filled"
            Expect.isTrue  (evenOdd.Contains inRing) "ring is inside under even odd"
            Expect.isTrue  (nonZero.Contains inRing) "ring is inside under non zero"
            Expect.isFalse (nonZero.Contains outside) "outside"

        testCase "Positive and Negative depend on orientation" <| fun _ ->
            let ccw = square 0.0 0.0 10.0
            let cw = ccw.Reverse ()
            let pt = Pt (5.0, 5.0)
            Expect.isTrue  ((Shape.ofPolyline (ccw, FillRule.Positive)).Contains pt) "ccw positive"
            Expect.isFalse ((Shape.ofPolyline (ccw, FillRule.Negative)).Contains pt) "ccw negative"
            Expect.isFalse ((Shape.ofPolyline (cw, FillRule.Positive)).Contains pt) "cw positive"
            Expect.isTrue  ((Shape.ofPolyline (cw, FillRule.Negative)).Contains pt) "cw negative"

        testCase "SignedArea sums the paths" <| fun _ ->
            let outer = square 0.0 0.0 10.0
            let hole = (square 3.0 3.0 4.0).Reverse ()
            let s = Shape.create ([outer; hole], FillRule.Positive)
            Expect.floatClose Accuracy.high s.SignedArea 84.0 "100 - 16"
            Expect.equal (Shape.empty FillRule.NonZero).SignedArea 0.0 "empty"
    ]
