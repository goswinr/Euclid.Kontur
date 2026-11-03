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

/// The largest distance from a vertex of a to the nearest vertex of b.
let private maxVertexDistance (a: Polyline2D) (b: Polyline2D) =
    let ps = a.AsPoints
    let qs = b.AsPoints
    let mutable worst = 0.0
    for p in ps do
        let mutable best = infinity
        for q in qs do
            best <- min best (p.DistanceTo q)
        worst <- max worst best
    worst

/// An inward offset of a half disc, captured in Rhino with Polyline2D.AsFSharpCode.
/// It runs counter clockwise, but the offset left three loops of winding -1 that run clockwise:
/// a thin tail at each bottom corner and a diamond inside the zigzag at the top.
/// A triangle at the right is wound twice, winding 2, it lies inside the main region and stays filled.
let private offsetWithLoops =
    Polyline2D.createFromPts [|
        Pt(25.19068907000115, 23.74933633351885); Pt(25.202661670895353, 23.063426487669293); Pt(25.18622095833124, 23.377133971348783)
        Pt(25.158842027712787, 23.69007658031139); Pt(25.12055823604955, 24.001873042193203); Pt(25.0714162262445, 24.312143481033992)
        Pt(25.01147587026685, 24.62050988009752); Pt(24.940810196207245, 24.926596542426726); Pt(24.859505299304317, 25.230030548572604)
        Pt(24.767660237050883, 25.530442210939093); Pt(24.66538690850765, 25.827465524190462); Pt(24.55280991797145, 26.12073861117245)
        Pt(24.4300664231641, 26.409904163803827); Pt(24.297305968126828, 26.694609878401252); Pt(24.154690301023923, 26.97450888490704)
        Pt(24.00239317707749, 27.249260169496893); Pt(23.840600146873477, 27.518528990052744); Pt(23.669508330296892, 27.78198728399442)
        Pt(23.48932617637156, 28.03931406797339); Pt(23.300273209297096, 28.290195828941528); Pt(23.10257976099251, 28.534326906118473)
        Pt(22.896486690472216, 28.77140986339219); Pt(22.682245090396442, 29.00115585169907); Pt(22.460115981153493, 29.22328496094202)
        Pt(22.23036999284661, 29.437526561017794); Pt(21.993287035572894, 29.643619631538087); Pt(20.367058588020804, 29.99104029496665)
        Pt(20.012519524274524, 28.222121987342987); Pt(21.24094741344884, 30.210548200842474); Pt(20.97748911950717, 30.38164001741906)
        Pt(20.70822029895132, 30.543433047623065); Pt(20.43346901436146, 30.6957301715695); Pt(20.153570007855674, 30.838345838672407)
        Pt(19.86886429325825, 30.971106293709674); Pt(19.57969874062687, 31.09384978851703); Pt(19.286425653644883, 31.206426779053228)
        Pt(18.98940234039351, 31.308700107596458); Pt(18.688990678027025, 31.400545169849895); Pt(18.38555667188115, 31.481850066752823)
        Pt(18.07947000955194, 31.552515740812428); Pt(17.771103610488414, 31.612456096790083); Pt(17.46083317164763, 31.66159810659513)
        Pt(15.098925610376014, 29.54611349609043); Pt(16.83609410080321, 31.727260828876823); Pt(16.188379752616388, 28.357562984021822)
        Pt(16.208296462973273, 31.749184005474945); Pt(15.894206308822833, 31.743701541440934); Pt(15.58049882514334, 31.727260828876823)
        Pt(15.267556216180735, 31.69988189825837); Pt(14.95575975429892, 31.66159810659513); Pt(14.645489315458132, 31.612456096790083)
        Pt(14.337122916394607, 31.55251574081243); Pt(14.031036254065398, 31.481850066752823); Pt(13.72760224791952, 31.400545169849895)
        Pt(13.427190585553031, 31.30870010759646); Pt(13.130167272301664, 31.20642677905323); Pt(12.836894185319675, 31.09384978851703)
        Pt(12.547728632688298, 30.971106293709678); Pt(12.263022918090872, 30.838345838672407); Pt(11.98312391158509, 30.6957301715695)
        Pt(11.708372626995228, 30.54343304762307); Pt(11.43910380643938, 30.38164001741906); Pt(11.175645512497706, 30.210548200842474)
        Pt(10.918318728518734, 30.030366046917138); Pt(10.667436967550593, 29.841313079842674); Pt(10.423305890373651, 29.643619631538087)
        Pt(10.186222933099934, 29.437526561017794); Pt(9.956476944793057, 29.223284960942024); Pt(9.734347835550103, 29.00115585169907)
        Pt(9.52010623547433, 28.77140986339219); Pt(9.314013164954037, 28.534326906118476); Pt(9.116319716649452, 28.29019582894153)
        Pt(8.92726674957499, 28.039314067973393); Pt(8.747084595649653, 27.78198728399442); Pt(8.575992779073065, 27.518528990052744)
        Pt(8.414199748869056, 27.249260169496896); Pt(8.261902624922625, 26.974508884907042); Pt(8.11928695781972, 26.694609878401252)
        Pt(7.98652650278245, 26.40990416380383); Pt(7.863783007975096, 26.12073861117245); Pt(7.751206017438896, 25.827465524190462)
        Pt(7.648932688895664, 25.530442210939093); Pt(7.557087626642231, 25.230030548572607); Pt(7.475782729739301, 24.926596542426726)
        Pt(7.405117055679694, 24.62050988009752); Pt(7.3451766997020425, 24.312143481033992); Pt(7.296034689896995, 24.001873042193207)
        Pt(7.257750898233757, 23.69007658031139); Pt(7.230371967615303, 23.377133971348787); Pt(7.213931255051191, 23.06342648766929)
        Pt(7.225903855945399, 23.74933633351885); Pt(25.19068907000115, 23.74933633351885)
    |]

/// The expected cleanup of offsetWithLoops, also captured in Rhino with Polyline2D.AsFSharpCode:
/// one counter clockwise contour without the three clockwise loops, the zigzag at the top resolved into two notches
/// on either side of the removed diamond.
let private offsetCleaned =
    Polyline2D.createFromPts [|
        Pt(25.12055823604955, 24.001873042193203); Pt(25.0714162262445, 24.312143481033992); Pt(25.01147587026685, 24.62050988009752)
        Pt(24.940810196207245, 24.926596542426726); Pt(24.859505299304317, 25.230030548572604); Pt(24.767660237050883, 25.530442210939093)
        Pt(24.66538690850765, 25.827465524190462); Pt(24.55280991797145, 26.12073861117245); Pt(24.4300664231641, 26.409904163803827)
        Pt(24.297305968126828, 26.694609878401252); Pt(24.154690301023923, 26.97450888490704); Pt(24.00239317707749, 27.249260169496893)
        Pt(23.840600146873477, 27.518528990052744); Pt(23.669508330296892, 27.78198728399442); Pt(23.48932617637156, 28.03931406797339)
        Pt(23.300273209297096, 28.290195828941528); Pt(23.10257976099251, 28.534326906118473); Pt(22.896486690472216, 28.77140986339219)
        Pt(22.682245090396442, 29.00115585169907); Pt(22.460115981153493, 29.22328496094202); Pt(22.23036999284661, 29.437526561017794)
        Pt(21.993287035572894, 29.643619631538087); Pt(21.01925918394368, 29.851706879624476); Pt(21.24094741344884, 30.210548200842474)
        Pt(20.97748911950717, 30.38164001741906); Pt(20.70822029895132, 30.543433047623065); Pt(20.43346901436146, 30.6957301715695)
        Pt(20.153570007855674, 30.838345838672407); Pt(19.86886429325825, 30.971106293709674); Pt(19.57969874062687, 31.09384978851703)
        Pt(19.286425653644883, 31.206426779053228); Pt(18.98940234039351, 31.308700107596458); Pt(18.688990678027025, 31.400545169849895)
        Pt(18.38555667188115, 31.481850066752823); Pt(18.07947000955194, 31.552515740812428); Pt(17.771103610488414, 31.612456096790083)
        Pt(17.46083317164763, 31.66159810659513); Pt(16.690922554155797, 30.972013937648548); Pt(16.188379752616388, 28.357562984021822)
        Pt(16.201156665959292, 30.533346389341933); Pt(15.098925610376014, 29.54611349609043); Pt(16.203503534766018, 30.93299519865085)
        Pt(16.208296462973273, 31.749184005474945); Pt(15.894206308822833, 31.743701541440934); Pt(15.58049882514334, 31.727260828876823)
        Pt(15.267556216180735, 31.69988189825837); Pt(14.95575975429892, 31.66159810659513); Pt(14.645489315458132, 31.612456096790083)
        Pt(14.337122916394607, 31.55251574081243); Pt(14.031036254065398, 31.481850066752823); Pt(13.72760224791952, 31.400545169849895)
        Pt(13.427190585553031, 31.30870010759646); Pt(13.130167272301664, 31.20642677905323); Pt(12.836894185319675, 31.09384978851703)
        Pt(12.547728632688298, 30.971106293709678); Pt(12.263022918090872, 30.838345838672407); Pt(11.98312391158509, 30.6957301715695)
        Pt(11.708372626995228, 30.54343304762307); Pt(11.43910380643938, 30.38164001741906); Pt(11.175645512497706, 30.210548200842474)
        Pt(10.918318728518734, 30.030366046917138); Pt(10.667436967550593, 29.841313079842674); Pt(10.423305890373651, 29.643619631538087)
        Pt(10.186222933099934, 29.437526561017794); Pt(9.956476944793057, 29.223284960942024); Pt(9.734347835550103, 29.00115585169907)
        Pt(9.52010623547433, 28.77140986339219); Pt(9.314013164954037, 28.534326906118476); Pt(9.116319716649452, 28.29019582894153)
        Pt(8.92726674957499, 28.039314067973393); Pt(8.747084595649653, 27.78198728399442); Pt(8.575992779073065, 27.518528990052744)
        Pt(8.414199748869056, 27.249260169496896); Pt(8.261902624922625, 26.974508884907042); Pt(8.11928695781972, 26.694609878401252)
        Pt(7.98652650278245, 26.40990416380383); Pt(7.863783007975096, 26.12073861117245); Pt(7.751206017438896, 25.827465524190462)
        Pt(7.648932688895664, 25.530442210939093); Pt(7.557087626642231, 25.230030548572607); Pt(7.475782729739301, 24.926596542426726)
        Pt(7.405117055679694, 24.62050988009752); Pt(7.3451766997020425, 24.312143481033992); Pt(7.296034689896995, 24.001873042193207)
        Pt(7.26502708101055, 23.74933633351885); Pt(25.151565844935995, 23.74933633351885); Pt(25.12055823604955, 24.001873042193203)
    |]

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
            let b = Polyline2D.reverse (square 5.0 5.0 10.0) // clockwise
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

        test ("Simplify keeps the regions of the main direction of a figure eight", fun _ ->
            // the counter clockwise lobe has area 250 / 7, the clockwise lobe 40 / 7, so the signed area is 30
            let eight = poly [ (0.0, 0.0); (10.0, 10.0); (10.0, 6.0); (0.0, 10.0) ]
            for input, name in [ eight, "counter clockwise"; Polyline2D.reverse eight, "clockwise" ] do
                let r = input.Simplify ()
                checkResult r
                assertThat r.Count (tag $"one lobe from the {name} input" >> isEqualTo 1)
                assertThat (area r) (tag $"the bigger lobe, counter clockwise, from the {name} input" >> isCloseTo Accuracy.high (250.0 / 7.0))
                assertThat (area (input.SimplifyWith 1e-4)) (tag $"SimplifyWith {name}" >> isCloseTo Accuracy.high (250.0 / 7.0))
                assertThat (area (Polyline2D.simplify input)) (tag $"simplify {name}" >> isCloseTo Accuracy.high (250.0 / 7.0))
                assertThat (area (Polyline2D.simplifyWith 1e-4 input)) (tag $"simplifyWith {name}" >> isCloseTo Accuracy.high (250.0 / 7.0))
            // a bowtie with two equal lobes has signed area 0, which counts as counter clockwise
            let bowtie = poly [ (0.0, 0.0); (10.0, 10.0); (10.0, 0.0); (0.0, 10.0) ]
            assertThat (area (bowtie.Simplify ())) (tag "balanced bowtie keeps its counter clockwise lobe" >> isCloseTo Accuracy.high 25.0)
            // NonZero through Kontur keeps both lobes
            let nonZero = Kontur.simplify (Kontur.createSingleton (eight, FillRule.NonZero))
            assertThat nonZero.PathCount (tag "NonZero keeps both lobes" >> isEqualTo 2)
        )

        test ("Simplify removes the inverted loops of an offset, in either orientation", fun _ ->
            let expected = offsetCleaned
            for input, name in [ offsetWithLoops, "counter clockwise"; Polyline2D.reverse offsetWithLoops, "clockwise" ] do
                let r = input.Simplify ()
                checkResult r
                assertThat r.Count (tag $"one contour from the {name} input" >> isEqualTo 1)
                let p = r.[0]
                assertThat p.SignedArea (tag $"counter clockwise and the expected area from the {name} input" >> isCloseTo Accuracy.high expected.SignedArea)
                assertThat p.PointCount (tag $"point count from the {name} input" >> isEqualTo expected.PointCount)
                assertThat (maxVertexDistance p expected) (tag $"every vertex is expected, {name} input" >> isLessOrEqual 1e-9)
                assertThat (maxVertexDistance expected p) (tag $"every expected vertex is there, {name} input" >> isLessOrEqual 1e-9)
            // NonZero through Kontur keeps the three clockwise loops as separate contours
            let nonZero = Kontur.simplify (Kontur.createSingleton (offsetWithLoops, FillRule.NonZero))
            assertThat nonZero.PathCount (tag "NonZero keeps the loops" >> isEqualTo 4)
        )

        test ("boolean operations between the offset loops and its cleanup", fun _ ->
            // The NonZero simplification is the cleaned contour plus the three loops. The diamond touches the
            // contour where their edges continue each other almost collinear, and the intersect phase found a
            // crossing in the rounding noise there, which failed the linking for one order of the arguments.
            let withLoops = Kontur.simplify (Kontur.createSingleton (offsetWithLoops, FillRule.NonZero))
            let cleaned = Kontur.createSingleton offsetCleaned
            let all = withLoops.SignedArea
            let main = cleaned.SignedArea
            for a, b, name in [ withLoops, cleaned, "loops first"; cleaned, withLoops, "cleaned first" ] do
                assertThat (Kontur.union a b).SignedArea (tag $"union, {name}" >> isCloseTo Accuracy.medium all)
                assertThat (Kontur.intersection a b).SignedArea (tag $"intersection, {name}" >> isCloseTo Accuracy.medium main)
                assertThat (Kontur.xor a b).SignedArea (tag $"xor, {name}" >> isCloseTo Accuracy.medium (all - main))
            assertThat (Kontur.difference withLoops cleaned).SignedArea (tag "loops minus cleaned" >> isCloseTo Accuracy.medium (all - main))
            assertThat (Kontur.difference cleaned withLoops).PathCount (tag "cleaned minus loops is empty" >> isEqualTo 0)
        )

        test ("UnionMany merges many polylines of mixed orientation", fun _ ->
            let a = square 0.0 0.0 10.0
            let b = Polyline2D.reverse (square 5.0 0.0 10.0) // clockwise, would cancel the overlap in one NonZero Kontur
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
