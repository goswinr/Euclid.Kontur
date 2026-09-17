namespace BoolOps

open Euclid

/// Phase 1: copies the paths of the Shapes into the flat vertex buffer and creates the input segments.
module internal Ingest =

    /// <summary>Adds all paths of the Shape to the state as the given group.
    /// The closing duplicate of the first point is dropped, so each vertex of a path is stored once and the last
    /// segment runs from the last vertex back to the first. Consecutive vertices closer than the tolerance
    /// are merged into the first of them. Paths with fewer than three distinct vertices are dropped.</summary>
    let addShape (s: EngineState) (shape: Shape) (group: int) : unit =
        let sqTol = s.Tolerance * s.Tolerance
        for p = 0 to shape.PathCount - 1 do
            let pl = shape.Paths.[p]
            Shape.checkPath "BoolOpsEngine.Execute" p pl // the Polyline2D is mutable, so it may have been opened since the Shape was created
            let xys = pl.XYs
            let n = pl.PointCount - 1 // without the closing duplicate
            let startV = s.VertexCount
            let mutable lastX = 0.0
            let mutable lastY = 0.0
            for i = 0 to n - 1 do
                let x = xys.[2 * i]
                let y = xys.[2 * i + 1]
                if i = 0 then
                    s.AddVertex (x, y) |> ignore
                    lastX <- x
                    lastY <- y
                else
                    let dx = x - lastX
                    let dy = y - lastY
                    if dx * dx + dy * dy > sqTol then
                        s.AddVertex (x, y) |> ignore
                        lastX <- x
                        lastY <- y
            // the last kept vertex may be within tolerance of the first one:
            let firstX = s.X startV
            let firstY = s.Y startV
            let mutable go = true
            while go && s.VertexCount - startV > 1 do
                let dx = s.X (s.VertexCount - 1) - firstX
                let dy = s.Y (s.VertexCount - 1) - firstY
                if dx * dx + dy * dy <= sqTol then s.VertexCount <- s.VertexCount - 1
                else go <- false
            let count = s.VertexCount - startV
            if count < 3 then
                s.VertexCount <- startV // drop the path
            else
                let pi = s.PathCount
                s.PathStart <- Buffers.ensureInt s.PathStart pi (pi + 2)
                s.PathGroup <- Buffers.ensureInt s.PathGroup pi (pi + 1)
                s.PathStart.[pi] <- startV
                s.PathGroup.[pi] <- group
                s.PathCount <- pi + 1
                let si = s.SegCount
                s.SegA     <- Buffers.ensureInt s.SegA     si (si + count)
                s.SegB     <- Buffers.ensureInt s.SegB     si (si + count)
                s.SegGroup <- Buffers.ensureInt s.SegGroup si (si + count)
                for k = 0 to count - 1 do
                    s.SegA.[si + k]     <- startV + k
                    s.SegB.[si + k]     <- startV + (if k = count - 1 then 0 else k + 1)
                    s.SegGroup.[si + k] <- group
                s.SegCount <- si + count

    /// Closes the path table after all Shapes were added.
    let finish (s: EngineState) : unit =
        s.PathStart <- Buffers.ensureInt s.PathStart s.PathCount (s.PathCount + 1)
        s.PathStart.[s.PathCount] <- s.VertexCount
        s.InputVertexCount <- s.VertexCount
