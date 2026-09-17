module TestBvh

open System
open BoolOps

#if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
open Fable.Mocha
#else
open Expecto
#endif

/// A deterministic pseudo random generator so tests are repeatable.
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
        Expect.equal bvh.NodeCount 0 "no nodes for an empty tree"
    else
        let seen = Array.zeroCreate<int> n
        let rec check node depth =
            Expect.isTrue (depth <= bvh.Depth) "depth is a bound"
            let count = bvh.NodeItemCount.[node]
            if count > 0 then
                Expect.isTrue (count <= bvh.LeafSize) "leaf size"
                let start = bvh.NodeLeftOrStart.[node]
                for i = start to start + count - 1 do
                    let ii = bvh.ItemIndices.[i]
                    seen.[ii] <- seen.[ii] + 1
                    Expect.isTrue (bvh.MinX.[ii] >= bvh.NodeMinX.[node] && bvh.MaxX.[ii] <= bvh.NodeMaxX.[node]) "item inside node X"
                    Expect.isTrue (bvh.MinY.[ii] >= bvh.NodeMinY.[node] && bvh.MaxY.[ii] <= bvh.NodeMaxY.[node]) "item inside node Y"
            else
                let l = bvh.NodeLeftOrStart.[node]
                let r = bvh.NodeRightChild.[node]
                Expect.isTrue (l > node && r > l && r < bvh.NodeCount) "children after the parent"
                for c in [ l; r ] do
                    Expect.isTrue (bvh.NodeMinX.[c] >= bvh.NodeMinX.[node] && bvh.NodeMaxX.[c] <= bvh.NodeMaxX.[node]) "child inside node X"
                    Expect.isTrue (bvh.NodeMinY.[c] >= bvh.NodeMinY.[node] && bvh.NodeMaxY.[c] <= bvh.NodeMaxY.[node]) "child inside node Y"
                check l (depth + 1)
                check r (depth + 1)
        check 0 1
        for i = 0 to n - 1 do
            Expect.equal seen.[i] 1 $"item {i} in exactly one leaf"

let tests =
    testList "Bvh" [

        testCase "tree invariants hold for many sizes and leaf sizes" <| fun _ ->
            for n in [ 0; 1; 2; 3; 4; 5; 17; 100; 1000 ] do
                for leafSize in [ 1; 2; 4; 8 ] do
                    let bvh = build (randomRects n) leafSize
                    checkTree bvh

        testCase "close pairs match brute force" <| fun _ ->
            for n in [ 0; 1; 2; 5; 50; 500 ] do
                let rects = randomRects n
                let bvh = build rects 4
                for d in [ 0.0; 0.5; 3.0 ] do
                    Expect.equal (treePairs bvh d) (brutePairs rects d) $"pairs for n = {n}, distance {d}"

        testCase "items in rect match brute force, including degenerate and half infinite query rectangles" <| fun _ ->
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
                    Expect.equal (treeInRect bvh q) (bruteInRect rects q) "items in rect"

        testCase "close pairs between two trees match brute force" <| fun _ ->
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
                    Expect.equal found expected $"pairs for {n} x {m} at distance {d}"

        testCase "identical and zero size rectangles" <| fun _ ->
            let same = Array.create 40 { MinX = 1.0; MinY = 1.0; MaxX = 1.0; MaxY = 1.0 }
            let bvh = build same 4
            checkTree bvh
            Expect.equal (treePairs bvh 0.0) (brutePairs same 0.0) "all pairs at distance 0"
            Expect.equal (treeInRect bvh { MinX = 1.0; MinY = 1.0; MaxX = 1.0; MaxY = 1.0 }) [ 0 .. 39 ] "all found by the point"
            Expect.equal (treeInRect bvh { MinX = 1.5; MinY = 1.0; MaxX = 2.0; MaxY = 1.0 }) [] "none found next to it"

        testCase "rebuilding reuses the instance for different item counts" <| fun _ ->
            let bvh = Bvh 4
            for n in [ 300; 5; 0; 700; 12 ] do
                let rects = randomRects n
                fill bvh rects
                checkTree bvh
                Expect.equal (treePairs bvh 1.0) (brutePairs rects 1.0) $"pairs after rebuild with n = {n}"

        testCase "leaf size below one falls back to the default" <| fun _ ->
            Expect.equal (Bvh 0).LeafSize Bvh.DefaultLeafSize "default"
            Expect.equal (Bvh 7).LeafSize 7 "given"
    ]
