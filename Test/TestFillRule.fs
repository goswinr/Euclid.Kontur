module TestFillRule

open Euclid
open TestUtil
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

/// A closed counterclockwise square for the normalized illustration fixtures.
let private square offset size =
    let path = Polyline2D.createFromPts [
        Pt (offset, offset)
        Pt (offset + size, offset)
        Pt (offset + size, offset + size)
        Pt (offset, offset + size)
    ]
    path.CloseInPlace 0.0
    path

let tests =
    testList ("FillRule and ClipType", [

        test ("EvenOdd is inside for odd winding numbers of either sign", fun _ ->
            for w in [ -3; -1; 1; 3; 7 ] do
                assertThat (FillRule.isInside FillRule.EvenOdd w) (tag $"winding {w}" >> isTrue)
            for w in [ -4; -2; 0; 2; 6 ] do
                assertThat (FillRule.isInside FillRule.EvenOdd w) (tag $"winding {w}" >> isFalse)
        )

        test ("NonZero is inside for any non zero winding number", fun _ ->
            for w in [ -3; -1; 1; 2; 7 ] do
                assertThat (FillRule.isInside FillRule.NonZero w) (tag $"winding {w}" >> isTrue)
            assertThat (FillRule.isInside FillRule.NonZero 0) (tag "winding 0" >> isFalse)
        )

        test ("Positive and Negative look at the sign", fun _ ->
            assertThat (FillRule.isInside FillRule.Positive 1)  (tag "positive 1" >> isTrue)
            assertThat (FillRule.isInside FillRule.Positive 0)  (tag "positive 0" >> isFalse)
            assertThat (FillRule.isInside FillRule.Positive -1) (tag "positive -1" >> isFalse)
            assertThat (FillRule.isInside FillRule.Negative -2) (tag "negative -2" >> isTrue)
            assertThat (FillRule.isInside FillRule.Negative 0)  (tag "negative 0" >> isFalse)
            assertThat (FillRule.isInside FillRule.Negative 2)  (tag "negative 2" >> isFalse)
        )

        // The three panels in each README illustration, scaled to outer size 4 and inner size 2.
        // SVG's downward Y axis is reflected to Cartesian Y upwards, preserving the pictured arrows.
        // Source: https://ishape-rust.github.io/iShape-js/overlay/filling_rules/filling_rules.html
        testList ("README fill-rule illustrations", [
            // Expected ring fill, center fill, filled area and contour count, read from the pictures.
            for rule, expectedPanels in [
                FillRule.EvenOdd,  [ true, false, 12.0, 2; true, false, 12.0, 2; true, false, 12.0, 2 ]
                FillRule.NonZero,  [ true, false, 12.0, 2; true, true,  16.0, 1; true, true,  16.0, 1 ]
                FillRule.Positive, [ true, false, 12.0, 2; true, true,  16.0, 1; false, false, 0.0, 0 ]
                FillRule.Negative, [ false, false, 0.0, 0; false, false, 0.0, 0; true, true,  16.0, 1 ]
            ] do
                test ($"{rule} matches all three panels before and after simplify", fun _ ->
                    let panels = [
                        "left: CCW outer, CW inner", true, false, 1, 0
                        "middle: both CCW", true, true, 1, 2
                        "right: both CW", false, false, -1, -2
                    ]
                    for (name, outerCcw, innerCcw, ringWinding, centerWinding),
                        (ringFilled, centerFilled, expectedArea, expectedCount) in List.zip panels expectedPanels do
                        let outer = square 0.0 4.0
                        let inner = square 1.0 2.0
                        let paths = [
                            if outerCcw then outer else outer.Reverse ()
                            if innerCcw then inner else inner.Reverse ()
                        ]
                        let input = Kontur.create (paths, rule)
                        let result = Kontur.simplify input
                        assertThat result.SignedArea (tag $"{name}: filled area" >> isCloseTo Accuracy.high expectedArea)
                        assertThat result.PathCount (tag $"{name}: contour count" >> isEqualTo expectedCount)
                        // Sample every unit cell, including all sides of the ring and the exterior.
                        // Cell centers stay away from boundaries; expectations do not use FillRule.isInside.
                        for x in -1 .. 4 do
                            for y in -1 .. 4 do
                                let point = Pt (float x + 0.5, float y + 0.5)
                                let inOuter = x >= 0 && x < 4 && y >= 0 && y < 4
                                let inInner = x >= 1 && x < 3 && y >= 1 && y < 3
                                let winding = if inInner then centerWinding elif inOuter then ringWinding else 0
                                let filled = if inInner then centerFilled elif inOuter then ringFilled else false
                                assertThat (input.WindingNumber point) (tag $"{name}: winding at cell {x}, {y}" >> isEqualTo winding)
                                assertThat (input.Contains point) (tag $"{name}: input fill at cell {x}, {y}" >> isEqualTo filled)
                                assertThat (result.Contains point) (tag $"{name}: simplified fill at cell {x}, {y}" >> isEqualTo filled)
                )
        ])

        test ("ClipType.combine truth table", fun _ ->
            let table op = [ for s in [false; true] do for c in [false; true] do ClipType.combine op s c ]
            assertThat (table ClipType.Union)        (tag "union"        >> isEqualTo [ false; true;  true;  true  ])
            assertThat (table ClipType.Intersection) (tag "intersection" >> isEqualTo [ false; false; false; true  ])
            assertThat (table ClipType.Difference)   (tag "difference"   >> isEqualTo [ false; false; true;  false ])
            assertThat (table ClipType.Xor)          (tag "xor"          >> isEqualTo [ false; true;  true;  false ])
        )
    ])
