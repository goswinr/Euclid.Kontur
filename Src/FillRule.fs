namespace BoolOps

open System

/// Shorthand for the OptionalAttribute on method arguments.
type internal OPT = Runtime.InteropServices.OptionalAttribute

/// Shorthand for the DefaultParameterValueAttribute on method arguments.
type internal DEF = Runtime.InteropServices.DefaultParameterValueAttribute

/// <summary>How the winding number of a point decides whether the point is inside a Shape.
/// The winding number counts how often the paths of a Shape wind around a point,
/// counter clockwise turns count +1, clockwise turns count -1.
/// The fill rule belongs to the Shape, not to the boolean operation, unlike in Clipper.
/// Subject and clip get separate winding numbers on every edge, and each fill rule is applied to
/// its own winding number before the boolean combines them. So two different fill rules never
/// conflict, they are two independent decisions.
/// What this buys is one pass instead of two: an EvenOdd glyph unioned with a NonZero CAD outline
/// works in one operation. With a fill rule per operation the glyph would have to be simplified
/// first, then unioned.</summary>
type FillRule =
    /// A point is inside if the winding number is odd. The orientation of the paths does not matter.
    /// This is the rule of fonts and of SVG's 'evenodd'. A path inside a path is a hole, a path inside that is filled again.
    | EvenOdd  = 0
    /// A point is inside if the winding number is not zero. Overlapping paths of the same orientation merge.
    /// This is the rule of SVG's 'nonzero' and the default of most CAD software.
    | NonZero  = 1
    /// A point is inside if the winding number is positive.
    /// Needs consistently oriented paths: counter clockwise outer boundaries, clockwise holes.
    /// Shapes returned by a boolean operation always use this rule.
    | Positive = 2
    /// A point is inside if the winding number is negative.
    /// Needs consistently oriented paths: clockwise outer boundaries, counter clockwise holes.
    | Negative = 3

/// The kind of boolean operation between a subject and a clip Shape.
type ClipType =
    /// Everything that is inside the subject or inside the clip.
    | Union        = 0
    /// Everything that is inside the subject and inside the clip.
    | Intersection = 1
    /// Everything that is inside the subject but not inside the clip.
    | Difference   = 2
    /// Everything that is inside exactly one of subject and clip.
    | Xor          = 3

/// Functions on the FillRule enum.
module FillRule =

    /// Applies the fill rule to a winding number.
    /// Returns TRUE if a point with this winding number is inside.
    let inline isInside (rule: FillRule) (windingNumber: int) : bool =
        if   rule = FillRule.EvenOdd  then windingNumber % 2 <> 0 // also TRUE for negative odd numbers, since -3 % 2 = -1
        elif rule = FillRule.NonZero  then windingNumber <> 0
        elif rule = FillRule.Positive then windingNumber > 0
        else                               windingNumber < 0

/// Functions on the ClipType enum.
module ClipType =

    /// Combines the inside test of the subject and of the clip according to the operation.
    /// Returns TRUE if a point that is inside the subject if 'inSubject' and inside the clip if 'inClip'
    /// is inside the result of the operation.
    let inline combine (op: ClipType) (inSubject: bool) (inClip: bool) : bool =
        if   op = ClipType.Union        then inSubject || inClip
        elif op = ClipType.Intersection then inSubject && inClip
        elif op = ClipType.Difference   then inSubject && not inClip
        else                                 inSubject <> inClip
