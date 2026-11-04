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

        test ("createSingleton defaults to NonZero", fun _ ->
            let s = Kontur.createSingleton (square 0.0 0.0 1.0)
            assertThat s.FillRule (tag "default rule" >> isEqualTo FillRule.NonZero)
            assertThat s.PathCount (tag "one path" >> isEqualTo 1)
        )

        test ("open or too short paths fail", fun _ ->
            let openPath = Polyline2D 3
            openPath.AddXY (0.0, 0.0)
            openPath.AddXY (1.0, 0.0)
            openPath.AddXY (1.0, 1.0)
            assertThat (fun () -> Kontur.createSingleton openPath |> ignore) (tag "open path" >> throws)
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
            let cw = Polyline2D.reverse ccw
            let pt = Pt (5.0, 5.0)
            assertThat ((Kontur.createSingleton (ccw, FillRule.Positive)).Contains pt) (tag "ccw positive" >> isTrue)
            assertThat ((Kontur.createSingleton (ccw, FillRule.Negative)).Contains pt) (tag "ccw negative" >> isFalse)
            assertThat ((Kontur.createSingleton (cw,  FillRule.Positive)).Contains pt) (tag "cw positive" >> isFalse)
            assertThat ((Kontur.createSingleton (cw,  FillRule.Negative)).Contains pt) (tag "cw negative" >> isTrue)
        )

        test ("SignedArea sums the paths", fun _ ->
            let outer = square 0.0 0.0 10.0
            let hole = Polyline2D.reverse (square 3.0 3.0 4.0)
            let s = Kontur.create ([outer; hole], FillRule.Positive)
            assertThat s.SignedArea (tag "100 - 16" >> isCloseTo Accuracy.high 84.0)
            assertThat (Kontur.empty FillRule.NonZero).SignedArea (tag "empty" >> isEqualTo 0.0)
        )

        test ("Outers and Holes split a boolean result by orientation, whatever the order of Paths", fun _ ->
            // the order of the result paths is not defined, so check the split, not the positions:
            let ring = Kontur.xor (Kontur.createSingleton (square 4.0 4.0 2.0)) (Kontur.createSingleton (square 0.0 0.0 10.0))
            assertThat ring.PathCount (tag "outer and hole" >> isEqualTo 2)
            assertThat ring.Outers.Count (tag "one outer" >> isEqualTo 1)
            assertThat ring.Holes.Count (tag "one hole" >> isEqualTo 1)
            assertThat ring.Outers.[0].SignedArea (tag "outer area" >> isCloseTo Accuracy.high 100.0)
            assertThat ring.Holes.[0].SignedArea (tag "hole area" >> isCloseTo Accuracy.high -4.0)
            assertThat (obj.ReferenceEquals (ring.Outers.[0], ring.Paths |> Seq.find (fun p -> p.SignedArea > 0.0))) (tag "not copied" >> isTrue)
            assertThat (Kontur.outers ring).Count (tag "function outers" >> isEqualTo 1)
            assertThat (Kontur.holes ring).Count (tag "function holes" >> isEqualTo 1)

            let other = Kontur.difference (Kontur.createSingleton (square 20.0 0.0 10.0)) (Kontur.createSingleton (square 22.0 2.0 2.0))
            let two = Kontur.union ring other
            assertThat two.Outers.Count (tag "two outers" >> isEqualTo 2)
            assertThat two.Holes.Count (tag "two holes" >> isEqualTo 2)
            assertThat (Kontur.empty FillRule.Positive).Outers.Count (tag "empty has no outers" >> isEqualTo 0)
        )

        test ("iter and iteri visit every path in order", fun _ ->
            let a = square 0.0 0.0 1.0
            let b = square 5.0 0.0 2.0
            let s = Kontur.create ([a; b], FillRule.NonZero)
            let seen = ResizeArray<Polyline2D> ()
            s |> Kontur.iter (fun p -> seen.Add p)
            assertThat seen.Count (tag "iter count" >> isEqualTo 2)
            assertThat (obj.ReferenceEquals (seen.[0], a) && obj.ReferenceEquals (seen.[1], b)) (tag "iter order" >> isTrue)
            let indices = ResizeArray<int> ()
            s |> Kontur.iteri (fun i p -> if obj.ReferenceEquals (p, s.Paths.[i]) then indices.Add i)
            assertThat (List.ofSeq indices) (tag "iteri indices" >> isEqualTo [0; 1])
        )

        test ("map replaces every path and keeps the fill rule", fun _ ->
            let s = Kontur.create ([square 0.0 0.0 1.0; square 5.0 0.0 2.0], FillRule.EvenOdd)
            let moved = s |> Kontur.map (Polyline2D.move (Vc (10.0, 0.0)))
            assertThat moved.PathCount (tag "same count" >> isEqualTo 2)
            assertThat moved.FillRule (tag "same rule" >> isEqualTo FillRule.EvenOdd)
            assertThat moved.BoundingRectangle.MinX (tag "moved" >> isCloseTo Accuracy.high 10.0)
            assertThat s.BoundingRectangle.MinX (tag "input unchanged" >> isEqualTo 0.0)
            let openPath (_: Polyline2D) =
                let p = Polyline2D 3
                p.AddXY (0.0, 0.0)
                p.AddXY (1.0, 0.0)
                p.AddXY (1.0, 1.0)
                p
            assertThat (fun () -> s |> Kontur.map openPath |> ignore) (tag "open result fails" >> throws)
        )

        test ("filter keeps the matching paths and the fill rule", fun _ ->
            let big = square 0.0 0.0 10.0
            let tiny = square 20.0 0.0 0.1
            let s = Kontur.create ([big; tiny], FillRule.Positive)
            let kept = s |> Kontur.filter (fun p -> abs p.SignedArea > 1.0)
            assertThat kept.PathCount (tag "tiny dropped" >> isEqualTo 1)
            assertThat (obj.ReferenceEquals (kept.Paths.[0], big)) (tag "not copied" >> isTrue)
            assertThat kept.FillRule (tag "same rule" >> isEqualTo FillRule.Positive)
            assertThat s.PathCount (tag "input unchanged" >> isEqualTo 2)
            assertThat (s |> Kontur.filter (fun _ -> false)).IsEmpty (tag "all dropped" >> isTrue)
        )

        test ("exists and forall test the paths", fun _ ->
            let outer = square 0.0 0.0 10.0
            let hole = Polyline2D.reverse (square 3.0 3.0 4.0)
            let s = Kontur.create ([outer; hole], FillRule.Positive)
            let isHole (p: Polyline2D) = p.SignedArea < 0.0
            assertThat (s |> Kontur.exists isHole) (tag "has a hole" >> isTrue)
            assertThat (s |> Kontur.forall isHole) (tag "not only holes" >> isFalse)
            assertThat (s |> Kontur.forall (fun p -> p.IsClosed)) (tag "all closed" >> isTrue)
            let empty = Kontur.empty FillRule.Positive
            assertThat (empty |> Kontur.exists isHole) (tag "exists on empty" >> isFalse)
            assertThat (empty |> Kontur.forall isHole) (tag "forall on empty" >> isTrue)
        )
    ])
