module TestKontur

open Euclid
open TestUtil
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

/// A closed counter clockwise square from (x, y) with the given size.
let private square x y size =
    let p = Polyline2D 5
    p.AddXY (x, y)
    p.AddXY (x + size, y)
    p.AddXY (x + size, y + size)
    p.AddXY (x, y + size)
    p.AddXY (x, y)
    p

let tests =
    testList ("Kontur", [

        test ("create keeps the paths and the fill rule", fun _ ->
            let a = square 0.0 0.0 10.0
            let b = square 2.0 2.0 2.0
            let s = Kontur.create ([a; b], FillRule.EvenOdd)
            assertThat s.PathCount (tag "two paths" >> isEqualTo 2)
            assertThat s.FillRule (tag "fill rule" >> isEqualTo FillRule.EvenOdd)
            assertThat s.IsEmpty (tag "not empty" >> isFalse)
            assertThat (obj.ReferenceEquals (s.Paths.[0], a)) (tag "paths are not copied" >> isTrue)
        )

        test ("ofPolyline defaults to NonZero", fun _ ->
            let s = Kontur.ofPolyline (square 0.0 0.0 1.0)
            assertThat s.FillRule (tag "default rule" >> isEqualTo FillRule.NonZero)
            assertThat s.PathCount (tag "one path" >> isEqualTo 1)
        )

        test ("open or too short paths fail", fun _ ->
            let openPath = Polyline2D 3
            openPath.AddXY (0.0, 0.0)
            openPath.AddXY (1.0, 0.0)
            openPath.AddXY (1.0, 1.0)
            assertThat (fun () -> Kontur.ofPolyline openPath |> ignore) (tag "open path" >> throws)
            let short = Polyline2D 3
            short.AddXY (0.0, 0.0)
            short.AddXY (1.0, 0.0)
            short.AddXY (0.0, 0.0)
            assertThat (fun () -> Kontur.create ([short], FillRule.NonZero) |> ignore) (tag "two distinct points" >> throws)
        )

        test ("bounding rectangle spans all paths", fun _ ->
            let s = Kontur.create ([square 0.0 0.0 1.0; square 5.0 -3.0 2.0], FillRule.NonZero)
            let r = s.BoundingRectangle
            assertThat r.MinX (tag "minX" >> isEqualTo 0.0)
            assertThat r.MinY (tag "minY" >> isEqualTo -3.0)
            assertThat r.MaxX (tag "maxX" >> isEqualTo 7.0)
            assertThat r.MaxY (tag "maxY" >> isEqualTo 1.0)
            assertThat
                (fun () -> (Kontur.empty FillRule.NonZero).BoundingRectangle |> ignore)
                (tag "empty shape has no rectangle" >> throws)
        )

        test ("winding number and Contains follow the fill rule", fun _ ->
            let outer = square 0.0 0.0 10.0
            let inner = square 3.0 3.0 4.0 // same orientation, nested
            let evenOdd = Kontur.create ([outer; inner], FillRule.EvenOdd)
            let nonZero = Kontur.create ([outer; inner], FillRule.NonZero)
            let inHole = Pt (5.0, 5.0)
            let inRing = Pt (1.0, 1.0)
            let outside = Pt (20.0, 5.0)
            assertThat (evenOdd.WindingNumber inHole)  (tag "winding in the nested square" >> isEqualTo 2)
            assertThat (evenOdd.WindingNumber inRing)  (tag "winding in the ring" >> isEqualTo 1)
            assertThat (evenOdd.WindingNumber outside) (tag "winding outside" >> isEqualTo 0)
            assertThat (evenOdd.Contains inHole) (tag "even odd: nested square is a hole" >> isFalse)
            assertThat (nonZero.Contains inHole) (tag "non zero: nested square is filled" >> isTrue)
            assertThat (evenOdd.Contains inRing) (tag "ring is inside under even odd" >> isTrue)
            assertThat (nonZero.Contains inRing) (tag "ring is inside under non zero" >> isTrue)
            assertThat (nonZero.Contains outside) (tag "outside" >> isFalse)
        )

        test ("Positive and Negative depend on orientation", fun _ ->
            let ccw = square 0.0 0.0 10.0
            let cw = ccw.Reverse ()
            let pt = Pt (5.0, 5.0)
            assertThat ((Kontur.ofPolyline (ccw, FillRule.Positive)).Contains pt) (tag "ccw positive" >> isTrue)
            assertThat ((Kontur.ofPolyline (ccw, FillRule.Negative)).Contains pt) (tag "ccw negative" >> isFalse)
            assertThat ((Kontur.ofPolyline (cw,  FillRule.Positive)).Contains pt) (tag "cw positive" >> isFalse)
            assertThat ((Kontur.ofPolyline (cw,  FillRule.Negative)).Contains pt) (tag "cw negative" >> isTrue)
        )

        test ("SignedArea sums the paths", fun _ ->
            let outer = square 0.0 0.0 10.0
            let hole = (square 3.0 3.0 4.0).Reverse ()
            let s = Kontur.create ([outer; hole], FillRule.Positive)
            assertThat s.SignedArea (tag "100 - 16" >> isCloseTo Accuracy.high 84.0)
            assertThat (Kontur.empty FillRule.NonZero).SignedArea (tag "empty" >> isEqualTo 0.0)
        )
    ])
