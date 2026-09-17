module TestFillRule

open BoolOps

#if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
open Fable.Mocha
#else
open Expecto
#endif

let tests =
    testList "FillRule and ClipType" [

        testCase "EvenOdd is inside for odd winding numbers of either sign" <| fun _ ->
            for w in [ -3; -1; 1; 3; 7 ] do
                Expect.isTrue (FillRule.isInside FillRule.EvenOdd w) $"winding {w}"
            for w in [ -4; -2; 0; 2; 6 ] do
                Expect.isFalse (FillRule.isInside FillRule.EvenOdd w) $"winding {w}"

        testCase "NonZero is inside for any non zero winding number" <| fun _ ->
            for w in [ -3; -1; 1; 2; 7 ] do
                Expect.isTrue (FillRule.isInside FillRule.NonZero w) $"winding {w}"
            Expect.isFalse (FillRule.isInside FillRule.NonZero 0) "winding 0"

        testCase "Positive and Negative look at the sign" <| fun _ ->
            Expect.isTrue  (FillRule.isInside FillRule.Positive 1) "positive 1"
            Expect.isFalse (FillRule.isInside FillRule.Positive 0) "positive 0"
            Expect.isFalse (FillRule.isInside FillRule.Positive -1) "positive -1"
            Expect.isTrue  (FillRule.isInside FillRule.Negative -2) "negative -2"
            Expect.isFalse (FillRule.isInside FillRule.Negative 0) "negative 0"
            Expect.isFalse (FillRule.isInside FillRule.Negative 2) "negative 2"

        testCase "ClipType.combine truth table" <| fun _ ->
            let table op = [ for s in [false; true] do for c in [false; true] do ClipType.combine op s c ]
            Expect.equal (table ClipType.Union)        [ false; true;  true;  true  ] "union"
            Expect.equal (table ClipType.Intersection) [ false; false; false; true  ] "intersection"
            Expect.equal (table ClipType.Difference)   [ false; false; true;  false ] "difference"
            Expect.equal (table ClipType.Xor)          [ false; true;  true;  false ] "xor"
    ]
