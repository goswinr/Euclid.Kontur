module TestKlip

// The tests of the Klip repository (D:\Git\_Euclid_\Klip, an F# port of Clipper2) brought over to the Euclid.Kontur API:
// Test/FSharp/Tests/Tests1/Tests/*.fs and Test/TypeScript/tests/*.test.ts, and the Clipper2 fixture files
// Test/TypeScript/tests/test-data/Polygons.txt and PolytreeHoleOwner2.txt, copied into Test/data-Clipper2.
//
// What is not brought over, because Euclid.Kontur has no counterpart:
// - open path clipping, Z vertex metadata and the Z callback, the Snap pre-pass, ClipType.NoClip
// - the PolyTree output: its cases are checked through the orientation of the result paths (outer counter clockwise,
//   holes clockwise) and through Kontur.Contains instead
// - the Klip tolerance knobs (angle, colinearity, merge, near top, small triangle, split area) and the tests of
//   Klip's internal predicates (isColinear, dotProductSign, isValidClosedPath, buildPath, pointInPolygon on rings)
// - the cases with coordinates at 1e12 (SignedAreasRetainThinGeometryAfterLargeTranslations): a height of 1e-4 at
//   that magnitude is below the resolution of a float
//
// Klip removes collinear vertices from its output, Euclid.Kontur keeps every input vertex. So where Klip expects
// "8 points", these tests count the corners of the result, the vertices that are not collinear with their neighbours.

open System
open Euclid
open Euclid
open TestUtil
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

#nowarn 52

// ---------------------- helpers ----------------------

/// A closed Polyline2D from interleaved x y coordinates. The first point is repeated at the end unless it already is.
let private path (xys: float[]) : Polyline2D =
    let pl = Polyline2D (xys.Length / 2 + 1)
    for i in 0 .. 2 .. xys.Length - 2 do
        pl.AddXY (xys.[i], xys.[i + 1])
    pl.CloseInPlace 0.0
    pl

/// A closed counter clockwise square from (x, y) with the given size.
let private square x y size =
    path [| x; y; x + size; y; x + size; y + size; x; y + size |]

let private nonZero (ps: Polyline2D list) = Kontur.create (ps, FillRule.NonZero)

/// The sum of the absolute areas of the paths, like Klip's totalAbsArea.
let private absArea (s: Kontur) =
    let mutable a = 0.0
    for p in s.Paths do a <- a + abs p.SignedArea
    a

/// Every path with a positive orientation, like Klip.unionSelfChecked: clockwise paths are replaced by reversed copies.
let private positives (ps: Polyline2D list) =
    ps |> List.map (fun p -> if p.SignedArea < 0.0 then p.Reverse () else p)

/// The union of the paths under NonZero after forcing all of them to a positive orientation.
let private unionSelfChecked (ps: Polyline2D list) =
    Kontur.simplify (nonZero (positives ps))

/// The count of corners of a closed path: the vertices that are not collinear with their two neighbours.
/// Collinear means closer to the line through the neighbours than a millionth of their distance.
let private cornerCount (p: Polyline2D) =
    let n = p.PointCount - 1 // without the closing duplicate
    let mutable corners = 0
    for i = 0 to n - 1 do
        let a = p.GetPt ((i + n - 1) % n)
        let b = p.GetPt i
        let c = p.GetPt ((i + 1) % n)
        let ux = c.X - a.X
        let uy = c.Y - a.Y
        let len = sqrt (ux * ux + uy * uy)
        let dist = abs ((b.X - a.X) * uy - (b.Y - a.Y) * ux) / len
        if dist > 1e-6 * len then corners <- corners + 1
    corners

/// The sorted x y coordinates of all result vertices, to compare two results regardless of path order and start vertex.
let private sortedXYs (s: Kontur) =
    [ for p in s.Paths do
        for i in 0 .. p.PointCount - 2 do
            (p.GetX i, p.GetY i) ]
    |> List.sort

let private rotateDegrees (degrees: float) (x: float, y: float) =
    let radians = degrees * Math.PI / 180.0
    let c = cos radians
    let s = sin radians
    (x * c - y * s, x * s + y * c)

/// Reads a text file of Test/data-Clipper2 on either platform. The path is fixed at compile time.
#if FABLE_COMPILER
[<Fable.Core.Import("readFileSync", "fs")>]
let private readFileSync (filePath: string) (encoding: string) : string = Fable.Core.Util.jsNative
let private readData (fileName: string) : string = readFileSync (__SOURCE_DIRECTORY__ + "/data-Clipper2/" + fileName) "utf8"
#else
let private readData (fileName: string) : string = IO.File.ReadAllText (IO.Path.Combine (__SOURCE_DIRECTORY__, "data-Clipper2", fileName))
#endif

/// One test case of a Clipper2 fixture file.
[<NoEquality; NoComparison>]
type private Fixture = {
    Number: int
    Caption: string
    ClipType: ClipType
    FillRule: FillRule
    /// -1 or 0 if not to be checked
    ExpectedArea: float
    /// -1 or 0 if not to be checked
    ExpectedCount: int
    Subjects: Polyline2D list
    Clips: Polyline2D list
    }

/// Parses a Clipper2 fixture file, the same format as clipper2-ts/tests/test-data-parser.ts reads.
/// Paths with fewer than three points are dropped, as Clipper does.
let private parseFixtures (text: string) : Fixture list =
    let cases = ResizeArray<Fixture> ()
    let mutable caption = ""
    let mutable clipType = ClipType.Union
    let mutable fillRule = FillRule.EvenOdd
    let mutable area = 0.0
    let mutable count = 0
    let mutable subjects = ResizeArray<Polyline2D> ()
    let mutable clips = ResizeArray<Polyline2D> ()
    let mutable target = subjects
    let mutable ignoreTarget = false
    let mutable started = false // the caption may be empty
    let flush () =
        if started then
            cases.Add {
                Number = cases.Count + 1; Caption = caption; ClipType = clipType; FillRule = fillRule
                ExpectedArea = area; ExpectedCount = count
                Subjects = List.ofSeq subjects; Clips = List.ofSeq clips }
    for raw in text.Split '\n' do
        let line = raw.Trim ()
        if line.StartsWith "CAPTION:" then
            flush ()
            started <- true
            caption <- line.Substring(8).Trim ()
            clipType <- ClipType.Union
            fillRule <- FillRule.EvenOdd
            area <- 0.0
            count <- 0
            subjects <- ResizeArray ()
            clips <- ResizeArray ()
            target <- subjects
            ignoreTarget <- false
        elif line.StartsWith "CLIPTYPE:" then
            let u = line.ToUpperInvariant ()
            clipType <-
                if u.Contains "INTERSECTION" then ClipType.Intersection
                elif u.Contains "UNION" then ClipType.Union
                elif u.Contains "DIFFERENCE" then ClipType.Difference
                elif u.Contains "XOR" then ClipType.Xor
                else ClipType.Union
        elif line.StartsWith "FILLRULE:" then
            let u = line.ToUpperInvariant ()
            fillRule <-
                if u.Contains "EVENODD" then FillRule.EvenOdd
                elif u.Contains "NONZERO" then FillRule.NonZero
                elif u.Contains "POSITIVE" then FillRule.Positive
                elif u.Contains "NEGATIVE" then FillRule.Negative
                else FillRule.EvenOdd
        elif line.StartsWith "SOL_AREA:" then area <- float (line.Substring(9).Trim ())
        elif line.StartsWith "SOL_COUNT:" then count <- int (line.Substring(10).Trim ())
        elif line.StartsWith "SUBJECTS_OPEN" then ignoreTarget <- true // open paths are not supported, none of the fixtures has them
        elif line.StartsWith "SUBJECTS" then target <- subjects; ignoreTarget <- false
        elif line.StartsWith "CLIPS" then target <- clips; ignoreTarget <- false
        elif line.Length > 0 && (Char.IsDigit line.[0] || line.[0] = '-') && not ignoreTarget then
            let coords = line.Split ([| ','; ' ' |], StringSplitOptions.RemoveEmptyEntries) |> Array.map float
            if coords.Length >= 6 then target.Add (path coords)
    flush ()
    List.ofSeq cases

/// The distance from a point to the nearest edge of any path of the Kontur.
let private distToEdges (s: Kontur) (pt: Pt) =
    let mutable d = Double.MaxValue
    for p in s.Paths do
        d <- min d (p.DistanceTo pt)
    d

/// The point in region oracle of TestEngine on a fixture: for random points farther than minDist from every input
/// edge, being inside the result must equal the combination of being inside subject and clip.
let private oracle (rand: Random) (minDist: float) (subject: Kontur) (clip: Kontur) (op: ClipType) (result: Kontur) =
    let br = if clip.IsEmpty then subject.BoundingRectangle else subject.BoundingRectangle.Union clip.BoundingRectangle
    let size = max br.SizeX br.SizeY
    let r = br.Expand (0.02 * size + minDist)
    let mutable tested = 0
    for _ in 1 .. 200 do
        let pt = Pt (r.MinX + rand.NextDouble () * r.SizeX, r.MinY + rand.NextDouble () * r.SizeY)
        if distToEdges subject pt > minDist && (clip.IsEmpty || distToEdges clip pt > minDist) then
            tested <- tested + 1
            let expected = ClipType.combine op (subject.Contains pt) (clip.Contains pt)
            assertThat (result.Contains pt) (tag $"point {pt.AsString} for {op}" >> isEqualTo expected)
    assertThat tested (tag "enough points tested" >> isGreaterThan 50)

// ---------------------- the fixtures of Klip's BooleanTests.fs ----------------------

/// From Test/Scripts/rhino/interactive-union.fsx of Klip: an arch with an open notch in its top edge plus a small
/// square sitting on the left tine, with 1e-14 noise on the shared seam.
let private sharedVertexTouchingPolygons () =
    let subj = path [| 9.0; 35.0;  9.0; 37.0;  7.0; 37.00000000000001;  7.0; 34.0;  14.0; 34.0;  14.0; 37.00000000000001;  12.0; 37.0;  12.0; 35.0 |]
    let clip = path [| 7.0; 37.00000000000001;  8.0; 37.0;  8.0; 38.0;  7.0; 38.0 |]
    subj, clip

/// Two polygons touching along a seam that carries float noise. They must union into one polygon with eight corners
/// whose area is the sum of the inputs, at every scale, orientation and translation of the same shape.
let private touchingCases : (string * float * float[] * float[]) list = [
    // name, tolerance, subject, clip
    "positive coordinates", Kontur.defaultTolerance,
        [| 3.0; 4.0;  5.0; 4.0;  5.0; 6.262690989118975;  3.0; 6.262690989118975 |],
        [| 5.0; 5.0;  7.0; 5.0;  7.0; 7.0;  4.0; 7.0;  4.0; 6.262690989118976;  5.0; 6.262690989118975 |]
    "negative coordinates", Kontur.defaultTolerance,
        [| -3.0000000000000004; -3.9999999999999996;  -5.000000000000001; -3.9999999999999996;  -5.000000000000001; -6.262690989118974;  -3.000000000000001; -6.262690989118975 |],
        [| -5.000000000000001; -4.999999999999999;  -7.000000000000001; -4.999999999999999;  -7.000000000000001; -6.999999999999999;  -4.000000000000001; -6.999999999999999;  -4.000000000000001; -6.262690989118975;  -5.000000000000001; -6.262690989118974 |]
    "large coordinates", Kontur.defaultTolerance,
        [| -40000.0; 30000.000000000004;  -40000.0; 50000.0;  -62626.90989118975; 50000.00000000001;  -62626.90989118975; 30000.000000000004 |],
        [| -50000.0; 50000.0;  -49999.99999999999; 70000.0;  -70000.0; 70000.0;  -70000.0; 40000.00000000001;  -62626.90989118976; 40000.00000000001;  -62626.90989118975; 50000.00000000001 |]
    "large coordinates with closing vertex", Kontur.defaultTolerance,
        [| -40000.0; 30000.000000000004;  -40000.0; 50000.0;  -62626.90989118975; 50000.00000000001;  -62626.90989118975; 30000.000000000004;  -40000.0; 30000.000000000004 |],
        [| -50000.0; 50000.0;  -49999.99999999999; 70000.0;  -70000.0; 70000.0;  -70000.0; 40000.00000000001;  -62626.90989118976; 40000.00000000001;  -62626.90989118975; 50000.00000000001;  -50000.0; 50000.0 |]
    "tiny coordinates", Kontur.defaultTolerance,
        [| -0.004; 0.0030000000000000005;  -0.004; 0.005;  -0.0062626909891189755; 0.005;  -0.0062626909891189755; 0.0030000000000000005 |],
        [| -0.005; 0.005;  -0.005; 0.007;  -0.007; 0.007;  -0.007; 0.004;  -0.006262690989118976; 0.004;  -0.0062626909891189755; 0.005 |]
    "tiny coordinates, shared colinear edge", Kontur.defaultTolerance,
        [| -0.030000000000000002; -0.039999999999999994;  -0.05000000000000001; -0.039999999999999994;  -0.05000000000000001; -0.06262690989118976;  -0.030000000000000006; -0.06262690989118976 |],
        [| -0.05000000000000001; -0.049999999999999996;  -0.07; -0.049999999999999996;  -0.07000000000000002; -0.06999999999999999;  -0.04000000000000001; -0.07;  -0.04000000000000001; -0.06262690989118977;  -0.05000000000000001; -0.06262690989118976 |]
    "scale 0.01, rotated -90 degrees", Kontur.defaultTolerance,
        [| 0.353; -0.030000000000000065;  0.353; -0.050000000000000065;  0.3756269098911898; -0.05000000000000007;  0.3756269098911898; -0.03000000000000007 |],
        [| 0.363; -0.05000000000000007;  0.363; -0.07000000000000008;  0.383; -0.07000000000000008;  0.383; -0.04000000000000007;  0.3756269098911898; -0.04000000000000007;  0.3756269098911898; -0.05000000000000007 |]
    "scale 0.1, rotated 180 degrees", Kontur.defaultTolerance,
        [| -14.090000000000002; -0.3999999999999983;  -14.290000000000001; -0.39999999999999825;  -14.290000000000001; -0.6262690989118957;  -14.090000000000002; -0.6262690989118957 |],
        [| -14.290000000000001; -0.4999999999999982;  -14.490000000000002; -0.4999999999999982;  -14.490000000000002; -0.6999999999999983;  -14.190000000000001; -0.6999999999999983;  -14.190000000000001; -0.6262690989118959;  -14.290000000000001; -0.6262690989118957 |]
    "scale 1, rotated -90 degrees", Kontur.defaultTolerance,
        [| 35.3; -3.0000000000000067;  35.3; -5.000000000000006;  37.56269098911898; -5.000000000000007;  37.56269098911898; -3.000000000000007 |],
        [| 36.3; -5.000000000000007;  36.3; -7.000000000000007;  38.3; -7.000000000000007;  38.3; -4.000000000000007;  37.56269098911898; -4.000000000000007;  37.56269098911898; -5.000000000000007 |]
    // rotated by about 179.999 degrees, so that no two edges are exactly colinear. At the 5e-4 scale the off axis
    // offsets are about 1e-9, so the tolerance must be tighter than that, as Klip needed 1e-10 too:
    "rotated 179.999 degrees, tiny coordinates", 1e-10,
        [| -0.00030000698127131514; -0.00039999476395132075;  -0.0005000069812408534; -0.00039999127329281686;  -0.0005000109303816248; -0.0006262603721702517;  -0.00030001093041208654; -0.0006262638628287556 |],
        [| -0.0005000087265701053; -0.000499991273277586;  -0.0007000087265396436; -0.0004999877826190821;  -0.0007000122171981475; -0.0006999877825886203;  -0.0004000122172438401; -0.0006999930185763761;  -0.00040001093039685566; -0.0006262621174995036;  -0.0005000109303816248; -0.0006262603721702517 |]
    "rotated 179.999 degrees, half unit coordinates", Kontur.defaultTolerance,
        [| -0.30000698127131514; -0.3999947639513207;  -0.5000069812408534; -0.39999127329281686;  -0.5000109303816248; -0.6262603721702515;  -0.3000109304120866; -0.6262638628287555 |],
        [| -0.5000087265701053; -0.49999127327758597;  -0.7000087265396436; -0.4999877826190821;  -0.7000122171981474; -0.6999877825886204;  -0.40001221724384006; -0.6999930185763762;  -0.4000109303968557; -0.6262621174995037;  -0.5000109303816248; -0.6262603721702515 |]
    "rotated 115 degrees, scale 0.01", Kontur.defaultTolerance,
        [| -0.6492368853792919; 1.273982914588338;  -0.6576892506141059; 1.292109070329071;  -0.6758154063548388; 1.283656705094257;  -0.6631368585026178; 1.2564674714831574;  -0.6564545695224803; 1.2595834740086005;  -0.6606807521398872; 1.268646551878967;  -0.6492368853792919; 1.273982914588338 |],
        [| -0.6317214422741114; 1.260082941465012;  -0.6401738075089254; 1.278209097205745;  -0.6606807521398872; 1.268646551878967;  -0.6522283869050733; 1.250520396138234;  -0.6317214422741114; 1.260082941465012 |]
    "rotated 115 degrees, scale 0.01, translated", Kontur.defaultTolerance,
        [| -0.9329112227217633; 1.141703398663499;  -0.9413635879565773; 1.159829554404232;  -0.9594897436973103; 1.151377189169418;  -0.9468111958450893; 1.1241879555583185;  -0.9401289068649517; 1.1273039580837616;  -0.9443550894823587; 1.136367035954128;  -0.9329112227217633; 1.141703398663499 |],
        [| -0.9153957796165828; 1.1278034255401732;  -0.9238481448513969; 1.145929581280906;  -0.9443550894823587; 1.136367035954128;  -0.9359027242475448; 1.1182408802133952;  -0.9153957796165828; 1.1278034255401732 |]
    "rotated 0.01 degrees, moved, scale 1", Kontur.defaultTolerance,
        [| 142.72459223457366; 1005.0249254478591;  144.72459220411193; 1005.0252745137077;  144.7242431382633; 1007.025274483246;  141.7242431839559; 1007.0247508844731;  141.7243718686537; 1006.2874418848219;  142.7243718534228; 1006.2876164177462;  142.72459223457366; 1005.0249254478591 |],
        [| 140.7247667979597; 1004.0245763972414;  142.72476676749795; 1004.02492546309;  142.7243718534228; 1006.2876164177462;  140.72437188388454; 1006.2872673518976;  140.7247667979597; 1004.0245763972414 |]
    ]

// ---------------------- the fixtures of Klip's UnionTouchingBridgeTests.fs ----------------------

/// An H shaped polygon and a small bridge polygon sharing one vertex and touching along an edge, with the shared
/// vertex shifted off the flat run, at a scale and rotation.
let private bridgeInput (shift: float) (rotation: float) (scale: float) =
    let transform (points: (float * float)[]) =
        points |> Array.collect (fun (x, y) -> let x, y = rotateDegrees rotation (x * scale, y * scale) in [| x; y |]) |> path
    [ transform [| 9.0, 35.0;  9.0, 37.0;  7.0, 37.0 + shift;  7.0, 34.0;  14.0, 34.0;  14.0, 37.0 + shift;  12.0, 37.0;  12.0, 35.0 |]
      transform [| 7.0, 37.0 + shift;  8.0, 37.0;  8.0, 38.0;  7.0, 38.0 |] ]

/// The bridge union must be one polygon with eleven vertices whose area is the sum of the two inputs within a percent.
/// Klip counts eleven points too. Only ten of them are corners: the shared vertex sits on the straight left side
/// of the union, where the bridge's left edge continues into the H's left edge.
let private checkBridge (scale: float) (shift: float) (rotation: float) =
    let input = bridgeInput shift rotation scale
    let inputArea = input |> List.sumBy (fun p -> abs p.SignedArea)
    let result = Kontur.simplifyWith (scale * 1e-5) (nonZero input)
    let context = $"scale {scale}, shift {shift}, rotation {rotation}"
    assertThat result.PathCount (tag $"{context}: one polygon" >> isEqualTo 1)
    assertThat (result.Paths.[0].PointCount - 1) (tag $"{context}: eleven vertices" >> isEqualTo 11)
    assertThat (cornerCount result.Paths.[0]) (tag $"{context}: ten corners" >> isEqualTo 10)
    assertThat (abs (absArea result - inputArea) < inputArea * 0.01) (tag $"{context}: area preserved" >> isTrue)

// ---------------------- the sliver triangle fixture of Clipper2 issue 1067 ----------------------

let private sliverSubject () = [ path [| -45077288.0; -27835646.0;  -45216220.0; -27853069.0;  -44996290.0; -28378125.0 |] ]

let private sliverClip () = [
    path [| -45943111.0; -27944226.0;  -45990276.0; -27890686.0;  -46034753.0; -27840198.0 |] // sliver
    path [| -44185329.0; -29939581.0;  -45679436.0; -28243538.0;  -47826654.0; -25806113.0 |] // sliver
    path [| -48000000.0; -29000000.0;  -44185329.0; -29939581.0;  -47826654.0; -25806113.0 |] // big triangle
    path [| -45679436.0; -28243538.0;  -45514581.0; -27890485.0;  -45943111.0; -27944226.0 |] ] // small triangle

let private scalePaths (s: float) (ps: Polyline2D list) =
    ps |> List.map (fun p -> path [| for i in 0 .. p.PointCount - 2 do yield p.GetX i * s; yield p.GetY i * s |])

// ---------------------- the count and area tolerances of Klip's polygons.test.ts ----------------------

/// The area tolerance schedule of clipper2-ts/tests/polygons.test.ts, as a ratio of the expected area.
/// The expected values of Polygons.txt come from an older Clipper version, a few cases are known to differ.
let private areaTolerance (testNum: int) : float =
    if List.contains testNum [ 19; 22; 23; 24 ] then 0.5
    elif testNum = 193 then 0.25
    elif testNum = 63 then 0.1
    elif testNum = 16 then 0.075
    elif List.contains testNum [ 15; 26; 44 ] then 0.05
    elif List.contains testNum [ 52; 53; 54; 59; 60; 117; 118; 119; 184 ] then 0.02
    elif testNum = 64 then 0.05
    elif testNum = 66 then 0.25
    elif testNum = 172 then 0.05
    else 0.01

/// The count tolerance schedule of clipper2-ts/tests/polygons.test.ts in its integer fixture mode, widened for Kontur.
/// The count of contours is a weak criterion, the area and the point oracle below are the real checks: the same
/// region has many contour decompositions. Kontur splits contours that touch at a vertex, which gives about a
/// tenth more contours than the reference on the intersection fixtures 120 to 158, and it merges polygons that share
/// an edge into one contour where Clipper keeps them apart, which gives up to a third fewer on the union fixtures
/// 163 to 179. So the bound is the Klip schedule or a third of the expected count plus two, whichever is bigger.
let private countTolerance (testNum: int) (expectedCount: int) : int =
    let klip =
        if testNum = 181 then 40
        elif testNum = 172 then 17
        elif List.contains testNum [ 120; 128; 129; 130; 131; 132; 145; 146; 150 ] then 12
        elif List.contains testNum [ 140; 165; 166; 173; 176; 177; 179 ] then 9
        elif testNum >= 120 then 7
        elif List.contains testNum [ 17; 18; 22; 62; 27; 121; 126 ] then 2
        elif List.contains testNum [ 23; 24; 37; 43; 45; 87; 102; 111; 118; 119; 16 ] then 1
        else 0
    max klip (expectedCount * 35 / 100 + 2)

let private fixtures = parseFixtures (readData "Polygons.txt")

/// Runs a fixture on the engine and returns subject, clip and result.
let private runFixture (engine: KonturEngine) (f: Fixture) =
    let subject = Kontur.create (f.Subjects, f.FillRule)
    let clip = Kontur.create (f.Clips, f.FillRule)
    subject, clip, engine.Execute (subject, clip, f.ClipType)

let tests =
    testList ("Klip", [

        // ---------------------- BooleanTests.fs ----------------------

        test ("union of two touching polygons with a shared vertex gives one polygon", fun _ ->
            let subj, clip = sharedVertexTouchingPolygons ()
            let r = Kontur.union (nonZero [ subj ]) (nonZero [ clip ])
            assertThat r.PathCount (tag "a single merged polygon" >> isEqualTo 1)
            assertThat (absArea r) (tag "area is the sum" >> isCloseTo Accuracy.medium (abs subj.SignedArea + abs clip.SignedArea))
            // as one shape of two paths, like the Clipper64 variant without the snap pre-pass:
            let r2 = Kontur.simplify (nonZero [ subj; clip ])
            assertThat r2.PathCount (tag "a single merged polygon from one shape" >> isEqualTo 1)
        )

        test ("union of two touching polygons with a shared vertex, rotated 90 degrees with 1e-5 noise", fun _ ->
            // the noisy shared seam is near vertical with a 1e-5 deviation, so the tolerance must cover it
            let subj = path [| -35.0; 9.000000000000002;  -37.0; 9.000000000000002;  -37.00001; 7.000000000000003;  -34.0; 7.000000000000002;  -34.0; 14.000000000000002;  -37.00001; 14.000000000000002;  -37.0; 12.000000000000002;  -35.0; 12.000000000000002 |]
            let clip = path [| -37.00001; 7.000000000000003;  -37.0; 8.000000000000002;  -38.0; 8.000000000000002;  -38.0; 7.000000000000003 |]
            let r = Kontur.simplifyWith 1e-4 (nonZero [ subj; clip ])
            assertThat r.PathCount (tag "a single merged polygon" >> isEqualTo 1)
            // with the default tolerance the two contours only touch at the shared vertex and along the noisy seam:
            let tight = Kontur.simplify (nonZero [ subj; clip ])
            assertThat (absArea tight) (tag "the area is the same under the tight tolerance" >> isCloseTo Accuracy.medium (absArea r))
        )

        test ("self union of a horizontal edge continuing into a near horizontal one keeps the left column", fun _ ->
            let subj = path [| 0.0; 0.0;  20.0; 0.0;  20.0; 36.999999999;  60.0; 37.0;  100.0; 37.0;  100.0; 100.0;  0.0; 100.0 |]
            let r = Kontur.simplify (nonZero [ subj ])
            assertThat r.PathCount (tag "a single polygon" >> isEqualTo 1)
            // 100 * 63 above y = 37 plus the 20 wide left column 20 * 37 = 7040
            assertThat (abs (absArea r - 7040.0) < 0.5) (tag "area about 7040" >> isTrue)
        )

        test ("self union of a near horizontal rectangle with an exact midpoint extremum terminates", fun _ ->
            // path 70 of Test/Scripts/data/polysXY.json, minimized: the bottom edge carries 1e-6 noise in Y
            let subj = path [| 17.823630756706656; -3.1776745347575104;  14.000866896598076; -3.177673366258442;  14.000867801900924; -1.4574267995238073;  20.834059175905757; -1.4574258437039758;  20.834058932959618; -3.1776739283677022 |]
            let r = unionSelfChecked [ subj ]
            assertThat r.PathCount (tag "the polygon comes back as a single contour" >> isEqualTo 1)
            assertThat (abs (absArea r - abs subj.SignedArea) < 1e-3) (tag "area preserved" >> isTrue)
            // the same rectangle with a closing vertex and a different Y offset, from union-touching.test.ts:
            let closed = path [| 17.823630670570378; 5.471326923838493;  14.00086720159361; 5.471328036893782;  14.000867265574863; 9.915292532211373;  20.83405924248522; 9.915293067972888;  20.83405950582065; 5.4713271767079;  17.823630670570378; 5.471326923838493 |]
            assertThat (unionSelfChecked [ closed ]).PathCount (tag "closed variant is one contour" >> isEqualTo 1)
        )

        test ("self union of two disjoint rings with near horizontal extrema keeps them separate", fun _ ->
            let a = path [| 31.68114899807879; -3.1776702435233077;  31.681147991813326; -5.232407663485913;  29.05299627590947; -5.23240949946618;  29.052995652930253; -3.177669208459603 |]
            let b = path [| 17.823630756706656; -3.1776745347575104;  14.000866896598076; -3.177673366258442;  14.000867801900924; -1.4574267995238073;  20.834059175905757; -1.4574258437039758;  20.834058932959618; -3.1776739283677022 |]
            let r = unionSelfChecked [ a; b ]
            assertThat r.PathCount (tag "two disjoint polygons" >> isEqualTo 2)
            assertThat (abs (absArea r - (abs a.SignedArea + abs b.SignedArea)) < 1e-3) (tag "area preserved" >> isTrue)
        )

        test ("the four operations on two overlapping squares", fun _ ->
            let subj = nonZero [ square 0.0 0.0 10.0 ]
            let clip = nonZero [ square 5.0 5.0 10.0 ]
            let uni = Kontur.union subj clip
            assertThat uni.PathCount (tag "union is one polygon" >> isEqualTo 1)
            assertThat (absArea uni) (tag "union area 100 + 100 - 25" >> isCloseTo Accuracy.high 175.0)
            let inter = Kontur.intersection subj clip
            assertThat inter.PathCount (tag "intersection is one polygon" >> isEqualTo 1)
            assertThat (absArea inter) (tag "intersection area" >> isCloseTo Accuracy.high 25.0)
            let diff = Kontur.difference subj clip
            assertThat diff.PathCount (tag "difference is one polygon" >> isEqualTo 1)
            assertThat (absArea diff) (tag "difference area" >> isCloseTo Accuracy.high 75.0)
            let xor = Kontur.xor subj clip
            assertThat (absArea xor) (tag "xor area" >> isCloseTo Accuracy.high 150.0)
        )

        test ("union of disjoint squares stays separate", fun _ ->
            let r = Kontur.union (nonZero [ square 0.0 0.0 10.0 ]) (nonZero [ square 100.0 100.0 10.0 ])
            assertThat r.PathCount (tag "two polygons" >> isEqualTo 2)
            assertThat (absArea r) (tag "area" >> isCloseTo Accuracy.high 200.0)
        )

        test ("self union resolves a self intersecting path", fun _ ->
            let bowtie = path [| 0.0; 0.0;  10.0; 10.0;  10.0; 0.0;  0.0; 10.0 |]
            let r = Kontur.simplify (nonZero [ bowtie ])
            assertThat (r.PathCount >= 1) (tag "at least one polygon" >> isTrue)
            assertThat (absArea r) (tag "each triangle is 25" >> isCloseTo Accuracy.high 50.0)
        )

        test ("empty subject", fun _ ->
            let empty = Kontur.empty FillRule.NonZero
            let clip = nonZero [ square 0.0 0.0 10.0 ]
            let uni = Kontur.union empty clip
            assertThat uni.PathCount (tag "union with the clip returns the clip" >> isEqualTo 1)
            assertThat (absArea uni) (tag "clip area" >> isCloseTo Accuracy.high 100.0)
            assertThat (Kontur.intersection empty clip).IsEmpty (tag "intersection is empty" >> isTrue)
            assertThat (Kontur.union empty empty).IsEmpty (tag "union of nothing is empty" >> isTrue)
            assertThat (Kontur.simplify empty).IsEmpty (tag "simplify of nothing is empty" >> isTrue)
        )

        test ("an engine executes and can be called twice under EvenOdd", fun _ ->
            let engine = KonturEngine Kontur.defaultTolerance
            let subj = nonZero [ square 0.0 0.0 10.0 ]
            let clip = nonZero [ square 5.0 5.0 10.0 ]
            assertThat (engine.Execute (subj, clip, ClipType.Union)).PathCount (tag "union" >> isEqualTo 1)
            let evenOdd = Kontur.create ([ square 0.0 0.0 10.0 ], FillRule.EvenOdd)
            let first = engine.Simplify evenOdd
            let second = engine.Simplify evenOdd
            assertThat first.PathCount (tag "first execute produces one polygon" >> isEqualTo 1)
            assertThat second.PathCount (tag "second execute produces the same polygon" >> isEqualTo 1)
            assertThat (absArea second) (tag "area" >> isCloseTo Accuracy.high 100.0)
            assertThat (sortedXYs second) (tag "same vertices" >> isEqualTo (sortedXYs first))
            assertThat engine.Tolerance (tag "the tolerance is the one given" >> isEqualTo Kontur.defaultTolerance)
            assertThat (KonturEngine 1e-8).Tolerance (tag "a custom tolerance is kept" >> isEqualTo 1e-8)
        )

        test ("open paths are rejected", fun _ ->
            let openPath = Polyline2D 3
            openPath.AddXY (0.0, 0.0)
            openPath.AddXY (10.0, 0.0)
            openPath.AddXY (10.0, 10.0)
            assertThat (fun () -> Kontur.create ([ openPath ], FillRule.NonZero) |> ignore) (tag "an open clip path fails" >> throws)
        )

        testList ("union of two touching polygons gives one polygon with eight corners", [
            for (name, tolerance, subj, clip) in touchingCases do
                test (name, fun _ ->
                    let s = path subj
                    let c = path clip
                    let r = Kontur.unionWith tolerance (nonZero [ s ]) (nonZero [ c ])
                    assertThat r.PathCount (tag "a single merged path" >> isEqualTo 1)
                    assertThat (cornerCount r.Paths.[0]) (tag "eight corners" >> isEqualTo 8)
                    let expected = abs s.SignedArea + abs c.SignedArea
                    assertThat (absArea r) (tag "the area is the sum of the inputs, they only touch" >> isCloseTo { Absolute = 0.0; Relative = 1e-6 } expected)
                )
            ])

        test ("union of half snapped touching polygons has eight corners and the input area", fun _ ->
            // a 1e-14 deviation at the shared top left vertex leaves a horizontal U turn in Klip's output
            let subject = [
                path [| -50.0; 50.0;  -40.0; 70.0;  -70.0; 70.0;  -70.0; 40.0;  -62.0; 40.0;  -62.0; 50.00000000000001 |]
                path [| -40.0; 30.0;  -40.0; 50.0;  -62.0; 50.0;  -62.0; 30.0 |] ]
            let r = Kontur.simplify (nonZero subject)
            assertThat r.PathCount (tag "a single merged path" >> isEqualTo 1)
            assertThat (cornerCount r.Paths.[0]) (tag "eight corners" >> isEqualTo 8)
            let expected = subject |> List.sumBy (fun p -> abs p.SignedArea)
            assertThat (abs (absArea r - expected) < 1e-9) (tag "union area matches the input area" >> isTrue)
        )

        // ---------------------- UnionTouchingBridgeTests.fs ----------------------

        test ("union of a bridge touching an H polygon gives one polygon with eleven corners at every scale, shift and rotation", fun _ ->
            for scale in [ 0.1; 1.0; 100.0; 1000.0; 10000.0 ] do
                for shiftPower = -9 to -2 do
                    for rotation in [ 0.0; 0.0001; 0.001; 0.01; 0.1; 45.0; 90.0; 180.0 ] do
                        checkBridge scale (10.0 ** float shiftPower) rotation
        )

        test ("union of a bridge touching an H polygon with the shift around the tolerance", fun _ ->
            for scale in [ 0.1; 1.0; 1000.0 ] do
                for shift in [ 1.05e-5; 1.5e-5; 1.99e-5 ] do
                    for rotation in [ 0.0; 180.0 ] do
                        checkBridge scale shift rotation
        )

        // ---------------------- SliverTriangleTests.fs, ToleranceUnitTests.fs ----------------------

        test ("union with sliver triangles does not produce area outside the inputs", fun _ ->
            let subj = sliverSubject ()
            let clip = sliverClip ()
            let inputArea = (subj @ clip) |> List.sumBy (fun p -> abs p.SignedArea)
            let r = Kontur.union (nonZero subj) (nonZero clip)
            let resultArea = absArea r
            assertThat (resultArea <= inputArea + inputArea * 0.01) (tag $"union area {resultArea} within the input area {inputArea} plus a percent" >> isTrue)
            oracle (Random 21) 1.0 (nonZero subj) (nonZero clip) ClipType.Union r
        )

        test ("scaling the input and the tolerance by a power of two scales the output bit for bit", fun _ ->
            // multiplying floats by 2^-24 only shifts exponents, so every branch of the engine decides identically
            let s = 1.0 / 16777216.0
            let t0 = 1.0 // geometry below one unit is noise at the fixture's 5e7 magnitude
            let baseline = Kontur.unionWith t0 (nonZero (sliverSubject ())) (nonZero (sliverClip ()))
            let scaled = Kontur.unionWith (t0 * s) (nonZero (scalePaths s (sliverSubject ()))) (nonZero (scalePaths s (sliverClip ())))
            assertThat (baseline.PathCount > 0) (tag "baseline union produces output" >> isTrue)
            assertThat scaled.PathCount (tag "path count" >> isEqualTo baseline.PathCount)
            for i in 0 .. baseline.PathCount - 1 do
                assertThat scaled.Paths.[i].PointCount (tag $"point count of path {i}" >> isEqualTo baseline.Paths.[i].PointCount)
                for j in 0 .. baseline.Paths.[i].PointCount - 1 do
                    assertThat (scaled.Paths.[i].GetX j) (tag $"x of point {j} in path {i}" >> isEqualTo (baseline.Paths.[i].GetX j * s))
                    assertThat (scaled.Paths.[i].GetY j) (tag $"y of point {j} in path {i}" >> isEqualTo (baseline.Paths.[i].GetY j * s))
            // keeping the tolerance at one unit on the tiny input collapses it:
            let mangled = Kontur.unionWith t0 (nonZero (scalePaths s (sliverSubject ()))) (nonZero (scalePaths s (sliverClip ())))
            let sameShape = mangled.PathCount = baseline.PathCount && abs (absArea mangled - absArea baseline * s * s) <= absArea baseline * s * s * 1e-9
            assertThat sameShape (tag "an unscaled tolerance at the tiny scale does not reproduce the result" >> isFalse)
        )

        test ("scaling the input and the tolerance by a decimal factor scales the output within float noise", fun _ ->
            let subj = [ path [| 0.0; 0.0;  100.0; 0.0;  100.0; 100.0;  0.0; 100.0 |] ]
            let clip = [ path [| 50.0; -10.0;  160.0; 40.0;  70.0; 120.0 |] ]
            let s = 1e-7
            let t0 = 1e-5
            let baseline = Kontur.unionWith t0 (nonZero subj) (nonZero clip)
            let scaled = Kontur.unionWith (t0 * s) (nonZero (scalePaths s subj)) (nonZero (scalePaths s clip))
            assertThat (baseline.PathCount > 0) (tag "baseline union produces output" >> isTrue)
            assertThat scaled.PathCount (tag "path count" >> isEqualTo baseline.PathCount)
            for i in 0 .. baseline.PathCount - 1 do
                assertThat scaled.Paths.[i].PointCount (tag $"point count of path {i}" >> isEqualTo baseline.Paths.[i].PointCount)
                for j in 0 .. baseline.Paths.[i].PointCount - 1 do
                    assertThat (scaled.Paths.[i].GetX j) (tag $"x of point {j} in path {i}" >> isCloseTo { Absolute = 1e-6 * s; Relative = 0.0 } (baseline.Paths.[i].GetX j * s))
                    assertThat (scaled.Paths.[i].GetY j) (tag $"y of point {j} in path {i}" >> isCloseTo { Absolute = 1e-6 * s; Relative = 0.0 } (baseline.Paths.[i].GetY j * s))
        )

        test ("the default tolerance preserves unit and sub unit triangles", fun _ ->
            for side in [ 1.0; 0.01 ] do
                let r = Kontur.simplify (nonZero [ path [| 0.0; 0.0;  side; 0.0;  0.0; side |] ])
                assertThat r.PathCount (tag $"triangle with side {side} survives" >> isEqualTo 1)
                assertThat r.Paths.[0].PointCount (tag "three points plus the closing one" >> isEqualTo 4)
                assertThat (absArea r) (tag "area" >> isCloseTo { Absolute = 0.0; Relative = 1e-12 } (side * side * 0.5))
            let r = Kontur.simplifyWith Kontur.defaultTolerance (nonZero [ path [| 0.0; 0.0;  1.0; 0.0;  0.0; 1.0 |] ])
            assertThat (absArea r) (tag "the explicit default tolerance gives the same result" >> isEqualTo (absArea (Kontur.simplify (nonZero [ path [| 0.0; 0.0;  1.0; 0.0;  0.0; 1.0 |] ]))))
        )

        test ("the tolerance decides whether a tiny triangle survives", fun _ ->
            let triangle () = nonZero [ path [| 0.0; 0.0;  1e-6; 0.0;  0.0; 1e-6 |] ]
            let fine = KonturEngine 1e-9
            for _ in 1 .. 2 do
                assertThat (fine.Simplify (triangle ())).PathCount (tag "repeated execution keeps the fine tolerance" >> isEqualTo 1)
            assertThat (Kontur.simplify (triangle ())).IsEmpty (tag "the default tolerance of 1e-6 swallows a 1e-6 triangle" >> isTrue)
        )

        test ("the engine rejects negative and non finite tolerances", fun _ ->
            for invalid in [ -Double.Epsilon; -1.0; Double.NaN; Double.PositiveInfinity; Double.NegativeInfinity ] do
                assertThat (fun () -> KonturEngine invalid |> ignore) (tag $"tolerance {invalid}" >> throws)
        )

        test ("near duplicate vertex chains give one polygon of about the input area for every start vertex and winding", fun _ ->
            // Klip asserts the same vertex set for every start, its representative choice is canonical. Kontur keeps the
            // first of consecutive near duplicates and the lowest id of a cluster, so the surviving vertices depend on the
            // start vertex. What must hold: one polygon, and an area that moves by no more than the tolerance along the
            // perimeter allows, a tolerance of one unit on a ten unit square.
            for vertices in [ [| 0.0, 0.0; 0.9, 0.9; 1.8, 0.0; 10.0, 0.0; 10.0, 10.0; 0.0, 10.0 |]
                              [| 0.0, 0.0; 0.75, 0.75; 1.5, 0.75; 2.25, 0.0; 10.0, 0.0; 10.0, 10.0; 0.0, 10.0 |] ] do
                for winding in [ vertices; Array.rev vertices ] do
                    for start = 0 to vertices.Length - 1 do
                        let polygon = path [| for i = 0 to vertices.Length - 1 do
                                                let x, y = winding.[(start + i) % vertices.Length]
                                                yield x; yield y |]
                        let r = Kontur.simplifyWith 1.0 (nonZero [ polygon ])
                        assertThat r.PathCount (tag $"one polygon for start {start}" >> isEqualTo 1)
                        assertThat (abs (absArea r - 100.0) < 10.0) (tag $"area for start {start} within a tenth of the square, got {absArea r}" >> isTrue)
        )

        // ---------------------- GeometryToleranceTests.fs, tolerance-regressions.test.ts ----------------------

        test ("a large thin triangle whose height exceeds the tolerance survives", fun _ ->
            for tolerance in [ 0.0; 1e-5 ] do
                for polygon in [ path [| 0.0; 0.0;  1e6; 1e6;  500000.0; 500001.0 |]
                                 path [| 500000.0; 500001.0;  1e6; 1e6;  0.0; 0.0 |] ] do
                    let r = Kontur.simplifyWith tolerance (nonZero [ polygon ])
                    assertThat r.PathCount (tag $"a small turn angle alone must not erase a real triangle, tolerance {tolerance}" >> isEqualTo 1)
                    assertThat (absArea r) (tag "area" >> isCloseTo { Absolute = 1e-4; Relative = 0.0 } 500000.0)
        )

        test ("containment near a long diagonal does not depend on winding or start vertex", fun _ ->
            let points = [| 0.0, 0.0; 1e6, 1e6; 0.0, 1e6 |]
            for winding in [ points; Array.rev points ] do
                for start = 0 to 2 do
                    let rotated = path [| for i = 0 to 2 do
                                            let x, y = winding.[(start + i) % 3]
                                            yield x; yield y |]
                    let s = nonZero [ rotated ]
                    let context = $"start {start}, signed area {rotated.SignedArea}"
                    assertThat (s.Contains (Pt (100000.0, 100100.0))) (tag $"70 units inside the diagonal, {context}" >> isTrue)
                    assertThat (s.Contains (Pt (100100.0, 100000.0))) (tag $"70 units outside the diagonal, {context}" >> isFalse)
                    assertThat (s.Contains (Pt (1000.0, 1010.0))) (tag $"near the endpoint inside, {context}" >> isTrue)
                    assertThat (s.Contains (Pt (1000.0, 990.0))) (tag $"near the endpoint outside, {context}" >> isFalse)
                    // the same through the engine: the simplified triangle contains the same points
                    let r = Kontur.simplify s
                    assertThat (r.Contains (Pt (100000.0, 100100.0))) (tag $"result inside, {context}" >> isTrue)
                    assertThat (r.Contains (Pt (100100.0, 100000.0))) (tag $"result outside, {context}" >> isFalse)
        )

        test ("points two tolerances off an edge are inside or outside", fun _ ->
            let tol = 1e-5
            let sq = nonZero [ square 0.0 0.0 10.0 ]
            assertThat (sq.Contains (Pt (10.0 + 2.0 * tol, 5.0))) (tag "right of the square" >> isFalse)
            assertThat (sq.Contains (Pt (10.0 - 2.0 * tol, 5.0))) (tag "inside the square" >> isTrue)
            let tri = nonZero [ path [| 0.0; 0.0;  10.0; 10.0;  0.0; 10.0 |] ]
            assertThat (tri.Contains (Pt (5.0, 5.0 + 2.0 * tol))) (tag "above the diagonal" >> isTrue)
            assertThat (tri.Contains (Pt (5.0, 5.0 - 2.0 * tol))) (tag "below the diagonal" >> isFalse)
        )

        test ("a short segment crossing a long diagonal is a proper intersection in either direction", fun _ ->
            // a square of side 2 centered on the diagonal of a big triangle: half of it is inside
            for diagonal in [ path [| 0.0; 0.0;  1e6; 1e6;  0.0; 1e6 |]; path [| 0.0; 1e6;  1e6; 1e6;  0.0; 0.0 |] ] do
                for sq in [ square (5e5 - 1.0) (5e5 - 1.0) 2.0; (square (5e5 - 1.0) (5e5 - 1.0) 2.0).Reverse () ] do
                    let r = Kontur.intersection (nonZero [ diagonal ]) (nonZero [ sq ])
                    assertThat r.PathCount (tag "one polygon" >> isEqualTo 1)
                    assertThat (absArea r) (tag "half the square" >> isCloseTo Accuracy.medium 2.0)
        )

        // ---------------------- PolyTreeTests.fs, polytree.test.ts ----------------------

        test ("union under EvenOdd of nested squares gives an outer contour and a hole", fun _ ->
            let outer = path [| 0.0; 0.0;  100.0; 0.0;  100.0; 100.0;  0.0; 100.0 |]
            let inner = path [| 25.0; 25.0;  75.0; 25.0;  75.0; 75.0;  25.0; 75.0 |]
            let r = Kontur.simplify (Kontur.create ([ outer; inner ], FillRule.EvenOdd))
            assertThat r.PathCount (tag "outer plus hole" >> isEqualTo 2)
            let outers = r.Paths |> Seq.filter (fun p -> p.SignedArea > 0.0) |> List.ofSeq
            let holes = r.Paths |> Seq.filter (fun p -> p.SignedArea < 0.0) |> List.ofSeq
            assertThat outers.Length (tag "one outer contour, counter clockwise" >> isEqualTo 1)
            assertThat holes.Length (tag "one hole, clockwise" >> isEqualTo 1)
            assertThat (outers.[0].Contains (holes.[0].GetPt 0)) (tag "the hole is inside the outer contour" >> isTrue)
            assertThat r.SignedArea (tag "10000 - 2500" >> isCloseTo Accuracy.high 7500.0)
            assertThat (r.Contains (Pt (50.0, 50.0))) (tag "the hole is outside the region" >> isFalse)
        )

        test ("union of disjoint polygons gives two outer contours", fun _ ->
            let a = path [| 0.0; 0.0;  10.0; 0.0;  10.0; 10.0;  0.0; 10.0 |]
            let b = path [| 100.0; 100.0;  110.0; 100.0;  110.0; 110.0;  100.0; 110.0 |]
            let r = Kontur.simplify (nonZero [ a; b ])
            assertThat r.PathCount (tag "two contours" >> isEqualTo 2)
            for p in r.Paths do
                assertThat (p.SignedArea > 0.0) (tag "both are outer contours" >> isTrue)
        )

        test ("nested contours: outer, hole and island under EvenOdd", fun _ ->
            let outer = path [| 0.0; 0.0;  100.0; 0.0;  100.0; 100.0;  0.0; 100.0 |]
            let hole = path [| 20.0; 20.0;  80.0; 20.0;  80.0; 80.0;  20.0; 80.0 |]
            let inner = path [| 30.0; 30.0;  70.0; 30.0;  70.0; 70.0;  30.0; 70.0 |]
            let r = Kontur.simplify (Kontur.create ([ outer; hole; inner ], FillRule.EvenOdd))
            assertThat r.PathCount (tag "three contours" >> isEqualTo 3)
            // outer 10000 - hole 3600 + inner 1600 = 8000
            assertThat r.SignedArea (tag "area" >> isCloseTo Accuracy.high 8000.0)
            assertThat (r.Contains (Pt (50.0, 50.0))) (tag "the island is inside" >> isTrue)
            assertThat (r.Contains (Pt (25.0, 50.0))) (tag "the ring of the hole is outside" >> isFalse)
            assertThat (r.Contains (Pt (10.0, 50.0))) (tag "the outer ring is inside" >> isTrue)
        )

        test ("complex nesting: one outer contour with two holes, one of them with an island", fun _ ->
            let subjects = [
                path [| 1588700.0; -8717600.0;  1616200.0; -8474800.0;  1588700.0; -8474800.0 |]
                path [| 13583800.0; -15601600.0;  13582800.0; -15508500.0;  13555300.0; -15508500.0;  13555500.0; -15182200.0;  13010900.0; -15185400.0 |]
                path [| 956700.0; -3092300.0;  1152600.0; 3147400.0;  25600.0; 3151700.0 |]
                path [|
                    22575900.0; -16604000.0;  31286800.0; -12171900.0
                    31110200.0; 4882800.0;  30996200.0; 4826300.0;  30414400.0; 5447400.0;  30260000.0; 5391500.0
                    29662200.0; 5805400.0;  28844500.0; 5337900.0;  28435000.0; 5789300.0;  27721400.0; 5026400.0
                    22876300.0; 5034300.0;  21977700.0; 4414900.0;  21148000.0; 4654700.0;  20917600.0; 4653400.0
                    19334300.0; 12411000.0;  -2591700.0; 12177200.0;  53200.0; 3151100.0;  -2564300.0; 12149800.0
                    7819400.0; 4692400.0;  10116000.0; 5228600.0;  6975500.0; 3120100.0;  7379700.0; 3124700.0
                    11037900.0; 596200.0;  12257000.0; 2587800.0;  12257000.0; 596200.0;  15227300.0; 2352700.0
                    18444400.0; 1112100.0;  19961100.0; 5549400.0;  20173200.0; 5078600.0;  20330000.0; 5079300.0
                    20970200.0; 4544300.0;  20989600.0; 4563700.0;  19465500.0; 1112100.0;  21611600.0; 4182100.0
                    22925100.0; 1112200.0;  22952700.0; 1637200.0;  23059000.0; 1112200.0;  24908100.0; 4181200.0
                    27070100.0; 3800600.0;  27238000.0; 3800700.0;  28582200.0; 520300.0;  29367800.0; 1050100.0
                    29291400.0; 179400.0;  29133700.0; 360700.0;  29056700.0; 312600.0;  29121900.0; 332500.0
                    29269900.0; 162300.0;  28941400.0; 213100.0;  27491300.0; -3041500.0;  27588700.0; -2997800.0
                    22104900.0; -16142800.0;  13010900.0; -15603000.0;  13555500.0; -15182200.0
                    13555300.0; -15508500.0;  13582800.0; -15508500.0;  13583100.0; -15154700.0
                    1588700.0; -8822800.0;  1588700.0; -8379900.0;  1588700.0; -8474800.0;  1616200.0; -8474800.0
                    1003900.0; -630100.0;  1253300.0; -12284500.0;  12983400.0; -16239900.0 |]
                path [| 198200.0; 12149800.0;  1010600.0; 12149800.0;  1011500.0; 11859600.0 |]
                path [| 21996700.0; -7432000.0;  22096700.0; -7432000.0;  22096700.0; -7332000.0 |] ]
            let r = Kontur.simplify (nonZero subjects)
            let outers = r.Paths |> Seq.filter (fun p -> p.SignedArea > 0.0) |> Seq.sortByDescending (fun p -> p.SignedArea) |> List.ofSeq
            let holes = r.Paths |> Seq.filter (fun p -> p.SignedArea < 0.0) |> List.ofSeq
            assertThat outers.Length (tag "one outer contour and one island" >> isEqualTo 2)
            assertThat holes.Length (tag "two holes" >> isEqualTo 2)
            let outer = outers.[0]
            let island = outers.[1]
            for h in holes do
                assertThat (outer.Contains (h.GetPt 0)) (tag "every hole is inside the outer contour" >> isTrue)
            assertThat (holes |> List.exists (fun h -> h.Contains (island.GetPt 0))) (tag "the island is inside one of the holes" >> isTrue)
            oracle (Random 22) 10.0 (nonZero subjects) (Kontur.empty FillRule.NonZero) ClipType.Union r
        )

        test ("hole ownership fixture: the region is right at points of interest", fun _ ->
            match parseFixtures (readData "PolytreeHoleOwner2.txt") with
            | [ f ] ->
                let engine = KonturEngine Kontur.defaultTolerance
                let subject, clip, r = runFixture engine f
                assertThat (abs r.SignedArea > 330000.0) (tag "area above 330000" >> isTrue)
                for p in r.Paths do
                    assertThat (p.SignedArea <> 0.0) (tag "no degenerate contour" >> isTrue)
                for x, y in [ 21887.0, 10420.0; 21726.0, 10825.0; 21662.0, 10845.0; 21617.0, 10890.0 ] do
                    assertThat (r.Contains (Pt (x, y))) (tag $"({x}, {y}) is outside" >> isFalse)
                for x, y in [ 21887.0, 10430.0; 21843.0, 10520.0; 21810.0, 10686.0; 21900.0, 10461.0 ] do
                    assertThat (r.Contains (Pt (x, y))) (tag $"({x}, {y}) is inside" >> isTrue)
                // every hole lies inside some outer contour:
                let outers = r.Paths |> Seq.filter (fun p -> p.SignedArea > 0.0) |> List.ofSeq
                for h in r.Paths do
                    if h.SignedArea < 0.0 then
                        let pt = h.GetPt 0
                        assertThat (outers |> List.exists (fun o -> o.Contains pt || o.DistanceTo pt < 1e-6)) (tag "hole inside an outer contour" >> isTrue)
                oracle (Random 23) 0.5 subject clip f.ClipType r
            | fs -> failwith $"expected one fixture in PolytreeHoleOwner2.txt, found {fs.Length}"
        )

        // ---------------------- ZCallbackTests.fs, the geometry only ----------------------

        test ("union of two overlapping triangles is a twelve corner star", fun _ ->
            let a = path [| 10.0; 30.0;  80.0; 30.0;  45.0; 90.0 |]
            let b = path [| 10.0; 70.0;  80.0; 70.0;  45.0; 10.0 |]
            let r = Kontur.union (nonZero [ a ]) (nonZero [ b ])
            assertThat r.PathCount (tag "one polygon" >> isEqualTo 1)
            assertThat (cornerCount r.Paths.[0]) (tag "six tips and six crossings" >> isEqualTo 12)
            assertThat (absArea r > max (abs a.SignedArea) (abs b.SignedArea) && absArea r < abs a.SignedArea + abs b.SignedArea) (tag "area between the bigger input and the sum" >> isTrue)
            let star = path [| 100.0; 50.0;  10.0; 79.0;  65.0; 2.0;  65.0; 98.0;  10.0; 21.0 |]
            assertThat (Kontur.simplify (nonZero [ star ])).PathCount (tag "a self intersecting star is one polygon under NonZero" >> isEqualTo 1)
        )

        // ---------------------- ApiContractTests.fs ----------------------

        test ("an empty path is rejected", fun _ ->
            assertThat (fun () -> Kontur.create ([ square 0.0 0.0 10.0; Polyline2D 0 ], FillRule.NonZero) |> ignore) (tag "empty path in create" >> throws)
            let single = Polyline2D 1
            single.AddXY (10.0, 10.0)
            assertThat (fun () -> Kontur.ofPolyline single |> ignore) (tag "single point path" >> throws)
            let two = Polyline2D 2
            two.AddXY (0.0, 0.0)
            two.AddXY (10.0, 10.0)
            assertThat (fun () -> Kontur.ofPolyline two |> ignore) (tag "two point path" >> throws)
        )

        test ("non finite coordinates are rejected and the engine recovers", fun _ ->
            // a NaN coordinate already fails the closed path check of Kontur.create, an infinite one fails at execution
            let engine = KonturEngine 1e-9
            for bad in [ path [| Double.NaN; 0.0;  1.0; 0.0;  0.0; 1.0 |]
                         path [| 0.0; Double.PositiveInfinity;  1.0; 0.0;  0.0; 1.0 |]
                         path [| 0.0; 0.0;  Double.NegativeInfinity; 0.0;  0.0; 1.0 |] ] do
                assertThat (fun () -> engine.Simplify (nonZero [ square 0.0 0.0 10.0; bad ]) |> ignore) (tag "fails" >> throws)
                let valid = engine.Simplify (nonZero [ square 0.0 0.0 10.0 ])
                assertThat (absArea valid) (tag "the next operation on the engine works" >> isCloseTo Accuracy.high 100.0)
        )

        test ("the operations do not mutate their input", fun _ ->
            let neg = (square 0.0 0.0 10.0).Reverse ()
            let pos = square 5.0 5.0 10.0
            let input = nonZero [ neg; pos ]
            let before = sortedXYs input
            let areasBefore = [ neg.SignedArea; pos.SignedArea ]
            let r = unionSelfChecked [ neg; pos ]
            assertThat (r.PathCount > 0) (tag "union produces output" >> isTrue)
            assertThat (obj.ReferenceEquals (input.Paths.[0], neg)) (tag "the shape keeps the original instances" >> isTrue)
            assertThat (sortedXYs input) (tag "the vertices are unchanged" >> isEqualTo before)
            assertThat [ neg.SignedArea; pos.SignedArea ] (tag "the orientations are unchanged" >> isEqualTo areasBefore)
            let oriented = unionSelfChecked [ neg ]
            assertThat (absArea oriented) (tag "a clockwise square counts after the orientation check" >> isCloseTo Accuracy.high 100.0)
            assertThat (absArea (Kontur.simplify (Kontur.ofPolyline (pos, FillRule.Positive)))) (tag "self intersections removed under Positive" >> isCloseTo Accuracy.high 100.0)
            assertThat (absArea (Kontur.simplify (Kontur.ofPolyline (neg, FillRule.Negative)))) (tag "self intersections removed under Negative" >> isCloseTo Accuracy.high 100.0)
        )

        // ---------------------- polygons.test.ts: the 195 Clipper2 fixtures ----------------------

        test ("Polygons.txt holds 195 fixtures and the first one is a union of area 9000", fun _ ->
            assertThat fixtures.Length (tag "fixture count" >> isEqualTo 195)
            let first = fixtures.Head
            assertThat first.ClipType (tag "clip type" >> isEqualTo ClipType.Union)
            assertThat first.FillRule (tag "fill rule" >> isEqualTo FillRule.NonZero)
            assertThat first.ExpectedArea (tag "expected area" >> isEqualTo 9000.0)
            assertThat first.ExpectedCount (tag "expected count" >> isEqualTo 1)
            let r = Kontur.simplify (Kontur.create (first.Subjects, FillRule.NonZero))
            assertThat r.PathCount (tag "one polygon" >> isEqualTo 1)
            assertThat (Math.Round (absArea r)) (tag "area 9000" >> isEqualTo 9000.0)
        )

        testList ("Polygons.txt fixtures against the expected area and count and the point oracle", [
            for f in fixtures do
                test ($"Polygon test {f.Number}: {f.ClipType} {f.FillRule}", fun _ ->
                    // the tests of a list run in parallel, so every test needs its own engine
                    let subject, clip, r = runFixture (KonturEngine Kontur.defaultTolerance) f
                    let measuredCount = r.PathCount
                    let measuredArea = Math.Round (abs r.SignedArea)
                    if f.ExpectedArea > 0.0 then
                        let ratio = abs (f.ExpectedArea - measuredArea) / f.ExpectedArea
                        assertThat (ratio <= areaTolerance f.Number) (tag $"area {measuredArea} within {areaTolerance f.Number} of the expected {f.ExpectedArea}" >> isTrue)
                    if f.ExpectedCount > 0 then
                        let diff = abs (f.ExpectedCount - measuredCount)
                        let bound = countTolerance f.Number f.ExpectedCount
                        assertThat (diff <= bound) (tag $"count {measuredCount} within {bound} of the expected {f.ExpectedCount}" >> isTrue)
                    for p in r.Paths do
                        assertThat p.IsClosed (tag "result path is closed" >> isTrue)
                    oracle (Random f.Number) 0.05 subject clip f.ClipType r
                )
            ])
    ])
