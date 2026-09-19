namespace Euclid

open System
open Euclid.EuclidErrors

/// Phase 1: copies the paths of the Kontur values into the flat vertex buffer and creates the input segments.
module internal Ingest =

    /// <summary>Adds all paths of the Kontur to the state as the given group.
    /// The closing duplicate of the first point is dropped, so each vertex of a path is stored once and the last
    /// segment runs from the last vertex back to the first. Consecutive vertices closer than the tolerance
    /// are merged into the first of them. Paths with fewer than three distinct vertices are dropped.
    /// A coordinate that is NaN or infinite fails, it would poison every comparison of the pipeline.</summary>
    let addKontur (s: EngineState) (kontur: Kontur) (group: int) : unit =
        let sqTol = s.Tolerance * s.Tolerance
        let mutable points = 0
        for p = 0 to kontur.PathCount - 1 do
            let pl = kontur.Paths.[p]
            Kontur.checkPath "KonturEngine.Execute" p pl // the Polyline2D is mutable, so it may have been opened since the Kontur was created
            points <- points + pl.PointCount - 1
        s.EnsureVertices (s.VertexCount + points) // one growth step for all input vertices, instead of one per doubling
        for p = 0 to kontur.PathCount - 1 do
            let pl = kontur.Paths.[p]
            let xys = pl.XYs
            let n = pl.PointCount - 1 // without the closing duplicate
            let startV = s.VertexCount
            let mutable lastX = 0.0
            let mutable lastY = 0.0
            for i = 0 to n - 1 do
                let x = xys.[2 * i]
                let y = xys.[2 * i + 1]
                if Double.IsNaN x || Double.IsInfinity x || Double.IsNaN y || Double.IsInfinity y then
                    fail $"KonturEngine.Execute: point {i} of path {p} is not finite: ({x}, {y}). Every coordinate must be a finite number."
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
                Arr.set s.PathStart pi startV
                Arr.set s.PathGroup pi group
                s.PathCount <- pi + 1
                let si = s.SegCount
                s.SegA     <- Buffers.ensureInt s.SegA     si (si + count)
                s.SegB     <- Buffers.ensureInt s.SegB     si (si + count)
                s.SegGroup <- Buffers.ensureInt s.SegGroup si (si + count)
                for k = 0 to count - 1 do
                    Arr.set s.SegA (si + k) (startV + k)
                    Arr.set s.SegB (si + k) (startV + (if k = count - 1 then 0 else k + 1))
                    Arr.set s.SegGroup (si + k) group
                s.SegCount <- si + count

    /// Closes the path table after all Kontur values were added.
    let finish (s: EngineState) : unit =
        s.PathStart <- Buffers.ensureInt s.PathStart s.PathCount (s.PathCount + 1)
        Arr.set s.PathStart s.PathCount s.VertexCount
        s.InputVertexCount <- s.VertexCount
