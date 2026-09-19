module TestUtil

open Scriptorium.Nib.Assertion

/// The tolerance of a float comparison: two values count as close when they differ by
/// less than Absolute plus Relative times the bigger magnitude.
type Accuracy = {
    Absolute: float
    Relative: float
    }

[<RequireQualifiedAccess>]
module Accuracy =

    /// Absolute 1e-8, relative 1e-5. For values that went through a boolean operation.
    let medium = { Absolute = 1e-8;  Relative = 1e-5 }

    /// Absolute 1e-10, relative 1e-7. For values that are exact up to the last few bits.
    let high   = { Absolute = 1e-10; Relative = 1e-7 }

/// Asserts that the subject is within the given accuracy of the expected value.
/// Scriptorium.Nib has no tolerant float comparison, so this builds one from the primitive `assertion`.
let isCloseTo (acc: Accuracy) (expected: float) : Assertion<float> =
    assertion
        (fun a -> abs (a - expected) <= acc.Absolute + acc.Relative * max (abs a) (abs expected))
        (fun a -> $"given {a} should be within {acc.Absolute} + {acc.Relative} * magnitude of {expected}")
