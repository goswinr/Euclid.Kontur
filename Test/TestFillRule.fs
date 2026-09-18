module TestFillRule

open BoolOps
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

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

        test ("ClipType.combine truth table", fun _ ->
            let table op = [ for s in [false; true] do for c in [false; true] do ClipType.combine op s c ]
            assertThat (table ClipType.Union)        (tag "union"        >> isEqualTo [ false; true;  true;  true  ])
            assertThat (table ClipType.Intersection) (tag "intersection" >> isEqualTo [ false; false; false; true  ])
            assertThat (table ClipType.Difference)   (tag "difference"   >> isEqualTo [ false; false; true;  false ])
            assertThat (table ClipType.Xor)          (tag "xor"          >> isEqualTo [ false; true;  true;  false ])
        )
    ])
