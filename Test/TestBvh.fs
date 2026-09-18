module TestBvh

open System
open BoolOps
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

/// A deterministic pseudo random generator so tests are repeatable.
/// Shared by all tests in this file, so they run sequentially.
let private rand = Random 4242

/// A rectangle as four floats, for the brute force checks.
type private Rect = { MinX: float; MinY: float; MaxX: float; MaxY: float }

let private sqRectDist (a: Rect) (b: Rect) =
    let axis aMin aMax bMin bMax =
        if bMin > aMax then bMin - aMax
        elif aMin > bMax then aMin - bMax
        else 0.0
    let dx = axis a.MinX a.MaxX b.MinX b.MaxX
    let dy = axis a.MinY a.MaxY b.MinY b.MaxY
    dx * dx + dy * dy

let private overlaps (a: Rect) (b: Rect) =
    not (b.MinX > a.MaxX || a.MinX > b.MaxX || b.MinY > a.MaxY || a.MinY > b.MaxY)

/// Random small rectangles, clustered unevenly in the plane to mimic real world input.
let private randomRects (count: int) : Rect[] =
    Array.init count (fun _ ->
        let cx = rand.NextDouble () * 100.0
        let cy = rand.NextDouble () * 100.0
        let cx = if rand.NextDouble () < 0.5 then Math.Round (cx / 25.0) * 25.0 + rand.NextDouble () * 3.0 else cx
        let cy = if rand.NextDouble () < 0.5 then Math.Round (cy / 25.0) * 25.0 + rand.NextDouble () * 3.0 else cy
        let sx = rand.NextDouble () * 2.0
        let sy = rand.NextDouble () * 2.0
        { MinX = cx; MinY = cy; MaxX = cx + sx; MaxY = cy + sy })

let private fill (bvh: Bvh) (rects: Rect[]) =
    bvh.Reset rects.Length
    for i = 0 to rects.Length - 1 do
        let r = rects.[i]
        bvh.SetRect (i, r.MinX, r.MinY, r.MaxX, r.MaxY)
    bvh.Build ()

let private build (rects: Rect[]) (leafSize: int) =
    let bvh = Bvh leafSize
    fill bvh rects
    bvh

/// All pairs within maxDistance by brute force, as sorted "i,j" strings with i < j.
let private brutePairs (rects: Rect[]) (maxDistance: float) =
    let sq = maxDistance * maxDistance
    [ for i in 0 .. rects.Length - 1 do
        for j in i + 1 .. rects.Length - 1 do
            if sqRectDist rects.[i] rects.[j] <= sq then $"{i},{j}" ]
    |> List.sort

let private treePairs (bvh: Bvh) (maxDistance: float) =
    let flat = bvh.ClosePairs maxDistance
    [ for k in 0 .. 2 .. flat.Count - 1 do
        let i = flat.[k]
        let j = flat.[k + 1]
        if i >= j then failwith $"pair {i},{j} not ordered"
        $"{i},{j}" ]
    |> List.sort

let private bruteInRect (rects: Rect[]) (q: Rect) =
    [ for i in 0 .. rects.Length - 1 do if overlaps rects.[i] q then i ] |> List.sort

let private treeInRect (bvh: Bvh) (q: Rect) =
    bvh.ItemsInRect (q.MinX, q.MinY, q.MaxX, q.MaxY) |> Seq.toList |> List.sort

/// Checks the invariants of the built tree: every item in exactly one leaf, node rectangles contain their children.
let private checkTree (bvh: Bvh) =
    let n = bvh.ItemCount
    if n = 0 then
        assertThat bvh.NodeCount (tag "no nodes for an empty tree" >> isEqualTo 0)
    else
        let seen = Array.zeroCreate<int> n
        let rec check node depth =
            assertThat depth (tag "depth is a bound" >> isLessOrEqual bvh.Depth)
            let count = bvh.NodeItemCount.[node]
            if count > 0 then
                assertThat count (tag "leaf size" >> isLessOrEqual bvh.LeafSize)
                let start = bvh.NodeLeftOrStart.[node]
                for i = start to start + count - 1 do
                    let ii = bvh.ItemIndices.[i]
                    seen.[ii] <- seen.[ii] + 1
                    assertThat (bvh.MinX.[ii] >= bvh.NodeMinX.[node] && bvh.MaxX.[ii] <= bvh.NodeMaxX.[node]) (tag "item inside node X" >> isTrue)
                    assertThat (bvh.MinY.[ii] >= bvh.NodeMinY.[node] && bvh.MaxY.[ii] <= bvh.NodeMaxY.[node]) (tag "item inside node Y" >> isTrue)
            else
                let l = bvh.NodeLeftOrStart.[node]
                let r = bvh.NodeRightChild.[node]
                assertThat (l > node && r > l && r < bvh.NodeCount) (tag "children after the parent" >> isTrue)
                for c in [ l; r ] do
                    assertThat (bvh.NodeMinX.[c] >= bvh.NodeMinX.[node] && bvh.NodeMaxX.[c] <= bvh.NodeMaxX.[node]) (tag "child inside node X" >> isTrue)
                    assertThat (bvh.NodeMinY.[c] >= bvh.NodeMinY.[node] && bvh.NodeMaxY.[c] <= bvh.NodeMaxY.[node]) (tag "child inside node Y" >> isTrue)
                check l (depth + 1)
                check r (depth + 1)
        check 0 1
        for i = 0 to n - 1 do
            assertThat seen.[i] (tag $"item {i} in exactly one leaf" >> isEqualTo 1)

let tests =
    testSequenced ("Bvh", [

        test ("tree invariants hold for many sizes and leaf sizes", fun _ ->
            for n in [ 0; 1; 2; 3; 4; 5; 17; 100; 1000 ] do
                for leafSize in [ 1; 2; 4; 8 ] do
                    let bvh = build (randomRects n) leafSize
                    checkTree bvh
        )

        test ("close pairs match brute force", fun _ ->
            for n in [ 0; 1; 2; 5; 50; 500 ] do
                let rects = randomRects n
                let bvh = build rects 4
                for d in [ 0.0; 0.5; 3.0 ] do
                    assertThat (treePairs bvh d) (tag $"pairs for n = {n}, distance {d}" >> isEqualTo (brutePairs rects d))
        )

        test ("items in rect match brute force, including degenerate and half infinite query rectangles", fun _ ->
            let rects = randomRects 800
            let bvh = build rects 4
            for _ in 1 .. 50 do
                let x = rand.NextDouble () * 100.0
                let y = rand.NextDouble () * 100.0
                let queries =
                    [ { MinX = x; MinY = y; MaxX = x + rand.NextDouble () * 10.0; MaxY = y + rand.NextDouble () * 10.0 }
                      { MinX = x; MinY = y; MaxX = x; MaxY = y } // a point
                      { MinX = x; MinY = y; MaxX = Double.PositiveInfinity; MaxY = y } // a horizontal ray to +x
                      { MinX = x; MinY = Double.NegativeInfinity; MaxX = x; MaxY = y } ] // a vertical ray to -y
                for q in queries do
                    assertThat (treeInRect bvh q) (tag "items in rect" >> isEqualTo (bruteInRect rects q))
        )

        test ("close pairs between two trees match brute force", fun _ ->
            for (n, m) in [ (0, 10); (10, 0); (1, 1); (50, 300); (400, 60) ] do
                let ra = randomRects n
                let rb = randomRects m
                let a = build ra 4
                let b = build rb 3
                for d in [ 0.0; 1.0; 4.0 ] do
                    let sq = d * d
                    let expected =
                        [ for i in 0 .. n - 1 do
                            for j in 0 .. m - 1 do
                                if sqRectDist ra.[i] rb.[j] <= sq then $"{i},{j}" ] |> List.sort
                    let flat = a.ClosePairsWith (b, d)
                    let found = [ for k in 0 .. 2 .. flat.Count - 1 do $"{flat.[k]},{flat.[k + 1]}" ] |> List.sort
                    assertThat found (tag $"pairs for {n} x {m} at distance {d}" >> isEqualTo expected)
        )

        test ("identical and zero size rectangles", fun _ ->
            let same = Array.create 40 { MinX = 1.0; MinY = 1.0; MaxX = 1.0; MaxY = 1.0 }
            let bvh = build same 4
            checkTree bvh
            assertThat (treePairs bvh 0.0) (tag "all pairs at distance 0" >> isEqualTo (brutePairs same 0.0))
            assertThat (treeInRect bvh { MinX = 1.0; MinY = 1.0; MaxX = 1.0; MaxY = 1.0 }) (tag "all found by the point" >> isEqualTo [ 0 .. 39 ])
            assertThat (treeInRect bvh { MinX = 1.5; MinY = 1.0; MaxX = 2.0; MaxY = 1.0 }) (tag "none found next to it" >> isEmpty)
        )

        test ("rebuilding reuses the instance for different item counts", fun _ ->
            let bvh = Bvh 4
            for n in [ 300; 5; 0; 700; 12 ] do
                let rects = randomRects n
                fill bvh rects
                checkTree bvh
                assertThat (treePairs bvh 1.0) (tag $"pairs after rebuild with n = {n}" >> isEqualTo (brutePairs rects 1.0))
        )

        test ("leaf size below one falls back to the default", fun _ ->
            assertThat (Bvh 0).LeafSize (tag "default" >> isEqualTo Bvh.DefaultLeafSize)
            assertThat (Bvh 7).LeafSize (tag "given" >> isEqualTo 7)
        )
    ])
