namespace Euclid

open System
open Euclid.EuclidErrors

/// Phases 7 and 8: selects the graph edges that separate inside from outside for the requested operation,
/// and links them into closed result contours.
module internal Link =

    /// Phase 7: marks every edge whose two sides differ in insideness, directed so that the inside is on its left.
    let select (s: EngineState) (ruleS: FillRule) (ruleC: FillRule) (op: ClipType) : unit =
        let g = s.GCount
        s.EdgeOut <- Buffers.ensureInt s.EdgeOut 0 g
        for e = 0 to g - 1 do
            let lS = Arr.get s.WindLeftS e
            let lC = Arr.get s.WindLeftC e
            let rS = lS - Arr.get s.GDeltaS e
            let rC = lC - Arr.get s.GDeltaC e
            let inLeft  = ClipType.combine op (FillRule.isInside ruleS lS) (FillRule.isInside ruleC lC)
            let inRight = ClipType.combine op (FillRule.isInside ruleS rS) (FillRule.isInside ruleC rC)
            Arr.set s.EdgeOut e (if inLeft = inRight then 0 elif inLeft then 1 else -1)

    /// TRUE if the half edge h leaves its origin along a result edge in the result's direction.
    let inline private isOutgoing (s: EngineState) (h: int) : bool =
        let out = Arr.get s.EdgeOut (h >>> 1)
        if h &&& 1 = 0 then out = 1 else out = -1

    /// The origin vertex of a half edge.
    let inline private origin (s: EngineState) (h: int) : int =
        if h &&& 1 = 0 then Arr.get s.GA (h >>> 1) else Arr.get s.GB (h >>> 1)

    /// Pass 2 of tryLink: writes out every cycle of the successor mapping in Next once, into a Polyline2D of exactly
    /// the right capacity. The mapping is one to one on the result half edges, so every walk closes.
    /// While s.Track is set, the vertex id of every written vertex is recorded in s.OutVert too.
    let private writeCycles (s: EngineState) (results: ResizeArray<Polyline2D>) : unit =
        let h = 2 * s.GCount
        let next = s.Next
        let track = s.Track
        // every result edge has one outgoing half edge, so there are at most as many result vertices as edges:
        if track then s.OutVert <- Buffers.ensureInt s.OutVert 0 s.GCount
        let mutable outCount = 0
        for h0 = 0 to h - 1 do
            if Arr.get next h0 >= 0 then
                let mutable count = 1
                let mutable hh = Arr.get next h0
                while hh <> h0 do
                    if hh < 0 || count > h then fail $"Kontur.Link: half edge {hh} is used twice, the result edges do not form simple cycles."
                    count <- count + 1
                    hh <- Arr.get next hh
                let pl = Polyline2D (count + 1)
                hh <- h0
                for _ = 1 to count do
                    let v = origin s hh
                    pl.AddXY (s.X v, s.Y v)
                    if track then
                        Arr.set s.OutVert outCount v
                        outCount <- outCount + 1
                    let nh = Arr.get next hh
                    Arr.set next hh (-2) // consumed
                    hh <- nh
                pl.AddXY (s.X (origin s h0), s.Y (origin s h0))
                results.Add pl
        s.OutCount <- outCount

    /// <summary>Phase 8: links the selected edges into closed contours and appends them as Polyline2Ds.
    /// At every vertex the contour continues along the first outgoing result edge found clockwise from the
    /// reversed incoming edge. That is the tightest turn keeping the inside on the left, which separates
    /// contours that only touch at a vertex instead of merging them into one self touching contour.
    /// The successor of every result half edge is computed first, then the cycles of that mapping are written out.
    /// Returns FALSE without appending anything if the result edges do not link into simple cycles: a vertex with
    /// an incoming result edge and no outgoing one, or a result edge that would follow two others. Both happen only
    /// when the winding numbers are inconsistent, because the graph is not planar, see Graph.repair. The reason is
    /// left in LinkError. The successor mapping is checked to be one to one before the first contour is written,
    /// so the second pass cannot fail. While s.Track is set, the vertex id of every written vertex is recorded in
    /// s.OutVert too.</summary>
    let tryLink (s: EngineState) (results: ResizeArray<Polyline2D>) : bool =
        let h = 2 * s.GCount
        s.Next <- Buffers.ensureInt s.Next 0 h
        s.Prev <- Buffers.ensureInt s.Prev 0 h
        let next = s.Next
        let prev = s.Prev
        let vHalf = s.VHalf
        let vStart = s.VHalfStart
        for i = 0 to h - 1 do
            Arr.set next i (-1)
            Arr.set prev i (-1)
        // pass 1: the successor of every outgoing half edge
        let mutable ok = true
        let mutable hh = 0
        while ok && hh < h do
            if isOutgoing s hh then
                let twin = hh ^^^ 1
                let u = origin s twin
                let first = Arr.get vStart u
                let last = Arr.get vStart (u + 1) - 1
                let mutable pos = Arr.get s.RingPos twin
                let mutable found = -1
                let mutable steps = last - first
                while found < 0 && steps > 0 do
                    pos <- if pos = first then last else pos - 1 // clockwise is decreasing pseudo angle
                    let cand = Arr.get vHalf pos
                    if isOutgoing s cand then found <- cand
                    steps <- steps - 1
                if found < 0 then
                    s.LinkError <- $"Kontur.Link: no outgoing result edge at vertex {u}, the winding numbers are inconsistent."
                    ok <- false
                elif Arr.get prev found >= 0 then
                    s.LinkError <- $"Kontur.Link: half edge {found} would follow two result edges, the winding numbers are inconsistent."
                    ok <- false
                else
                    Arr.set next hh found
                    Arr.set prev found hh
            hh <- hh + 1
        if ok then
            // pass 2: write out every cycle once, into a Polyline2D of exactly the right capacity
            writeCycles s results
        ok

    /// <summary>Phase 8 as tryLink, failing with its reason if the result edges do not link into simple cycles.</summary>
    let link (s: EngineState) (results: ResizeArray<Polyline2D>) : unit =
        if not (tryLink s results) then fail s.LinkError
