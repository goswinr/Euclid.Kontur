// Draws the smallest input found that failed in the linking on Euclid.Kontur eb789f3, and the graph the engine built from it:
// "Link: no outgoing result edge at vertex v, the winding numbers are inconsistent."
// Fixed by the graph repair of 7141487. The test "a segment snapped through a vertex of its own path is rerouted through
// the crossing on it" in Test/TestEngine.fs runs the same input. no-outgoing-edge.3dm and .png in this folder hold the drawing.
//
// Run it in Rhino 8 or 9 from an F# scripting host that has RhinoCommon loaded, like Caddis or Fesh.
// It needs no build of Euclid.Kontur: the graph of eb789f3 is recorded below, read from its EngineState with reflection.
// A rerun replaces its earlier drawing. It only deletes objects on its own layer tree "Kontur eb789f3 minimal repro".

open System
open System.Drawing
open Rhino
open Rhino.Geometry
open Rhino.DocObjects

// ---------------------- the input: KonturEngine(0.4).Execute (subject, clip, op) ----------------------

let subject = [| 0.0, 1.0; 0.0, 2.0; 9.0, 2.0 |]
let clip = [| 9.0, 2.0; 0.0, 0.0; 8.0, 2.0 |]
let tolerance = 0.4
/// Every operation failed, at the vertex v of the message.
let failures = [| "Union", 4; "Intersection", 1; "Difference", 4; "Xor", 4 |]

// ---------------------- the graph eb789f3 built from it, after the failed Union ----------------------

/// The six input vertices, then the crossing.
let vertices = [| 0.0, 1.0; 0.0, 2.0; 9.0, 2.0; 9.0, 2.0; 0.0, 0.0; 8.0, 2.0; 7.2, 1.8 |]
let inputCount = 6
/// The vertex each vertex was merged into, itself if none.
let representative = [| 0; 1; 2; 2; 4; 5; 6 |]
/// The graph edges: from vertex, to vertex, and the change of the subject and of the clip winding number across the edge.
let edges = [| 0, 1, 1, 0; 0, 6, -1, 0; 1, 5, 1, 0; 4, 5, 0, -1; 4, 6, 0, 1; 5, 6, 1, -1 |]

// ---------------------- the drawing ----------------------

let u = 200.0 // document units per unit of the input
let blue = Color.FromArgb (30, 90, 220)
let red = Color.FromArgb (220, 40, 40)
let violet = Color.FromArgb (150, 70, 200)
let orange = Color.FromArgb (255, 120, 0)
let dark = Color.FromArgb (40, 40, 40)

let num (v: float) = v.ToString "0.###"
let point (x: float, y: float) = $"({num x},{num y})"

/// Runs f on Rhino's UI thread and waits for it. Hosts like Caddis in background mode run scripts on another thread.
let onUi (f: unit -> 'T) : 'T =
    if RhinoApp.InvokeRequired then
        let result = ref Unchecked.defaultof<'T>
        RhinoApp.InvokeAndWait (Action (fun () -> result.Value <- f ()))
        result.Value
    else
        f ()

let draw () : string =
    let doc = RhinoDoc.ActiveDoc
    let layer (fullPath: string) (color: Color) : int =
        let existing = doc.Layers.FindByFullPath (fullPath, -1)
        if existing >= 0 then existing
        else
            let cut = fullPath.LastIndexOf "::"
            let l = new Layer (Name = (if cut < 0 then fullPath else fullPath.Substring (cut + 2)), Color = color)
            if cut >= 0 then l.ParentLayerId <- doc.Layers.[doc.Layers.FindByFullPath (fullPath.Substring (0, cut), -1)].Id
            doc.Layers.Add l
    let attrs (li: int) = new ObjectAttributes (LayerIndex = li)
    let colored (li: int) (c: Color) = new ObjectAttributes (LayerIndex = li, ColorSource = ObjectColorSource.ColorFromObject, ObjectColor = c)
    let added = Collections.Generic.List<Guid> ()
    let dot (text: string) (pt: Point3d) (height: int) (a: ObjectAttributes) =
        a.DisplayOrder <- 2 // above the lines
        added.Add (doc.Objects.AddTextDot (new TextDot (text, pt, FontHeight = height), a))
    let line (a: Point3d) (b: Point3d) (at: ObjectAttributes) = added.Add (doc.Objects.AddLine (a, b, at))

    let root = "Kontur eb789f3 minimal repro"
    // a rerun replaces the earlier drawing: delete the objects on this layer tree only
    let mutable deleted = 0
    let rootIndex = doc.Layers.FindByFullPath (root, -1)
    if rootIndex >= 0 then
        let rec clear (l: Layer) =
            for o in doc.Objects.FindByLayer l do
                if doc.Objects.Delete (o, true) then deleted <- deleted + 1
            match l.GetChildren () with
            | null -> ()
            | children -> for c in children do clear c
        clear doc.Layers.[rootIndex]
    let lRoot = layer root dark
    let lS = layer (root + "::subject") blue
    let lC = layer (root + "::clip") red
    let lLabels = layer (root + "::labels") dark
    let lGraph = layer (root + "::engine graph") dark
    let lProblem = layer (root + "::problem") orange

    // below everything else in the document
    let existing = doc.Objects.BoundingBox
    let ox, oyTop = if existing.IsValid then existing.Min.X, existing.Min.Y - 600.0 else 0.0, 0.0
    let oyA = oyTop - 4.5 * u            // the input panel, y = 0 of the input
    let oyB = oyA - 6.5 * u              // the graph panel
    let pA (x, y) = Point3d (ox + x * u, oyA + y * u, 0.0)
    let pB (x, y) = Point3d (ox + x * u, oyB + y * u, 0.0)

    let triangle (pts: (float * float)[]) (p: float * float -> Point3d) (at: ObjectAttributes) =
        let pl = Polyline (pts |> Array.map p)
        pl.Add pl.[0]
        added.Add (doc.Objects.AddPolyline (pl, at))

    // ---------------- the title ----------------
    let points (pts: (float * float)[]) = String.Join (" ", pts |> Array.map point)
    let title =
        [ yield "Minimal reproduction: Euclid.Kontur eb789f3 throws in Link for two triangles."
          yield $"subject = {points subject}    clip = {points clip}    KonturEngine({num tolerance}).Execute (subject, clip, op)"
          for op, v in failures do
              yield $"{op}: Euclid.Kontur.Link: no outgoing result edge at vertex {v}, the winding numbers are inconsistent."
          yield "The same at tolerance 1e-6 with all coordinates times 2.5e-6, and for any tolerance from 0.25 to 0.8."
          yield "Fixed by the graph repair of 7141487, tested in Test/TestEngine.fs." ]
    dot (String.Join ("\n", title)) (pA (0.0, 3.6)) 14 (colored lRoot Color.White)

    // ---------------- panel 1: the input ----------------
    dot "1. the input" (pA (-1.4, 2.6)) 14 (colored lRoot Color.White)
    triangle subject pA (new ObjectAttributes (LayerIndex = lS, ObjectDecoration = ObjectDecoration.EndArrowhead))
    triangle clip pA (new ObjectAttributes (LayerIndex = lC, ObjectDecoration = ObjectDecoration.EndArrowhead))
    for i, p in Array.indexed subject do
        if not (Array.contains p clip) then dot $"S{i} {point p}" (pA p) 11 (colored lLabels blue)
    for i, p in Array.indexed clip do
        match Array.tryFindIndex ((=) p) subject with
        | Some k -> dot $"S{k} = C{i} {point p}" (pA p) 11 (colored lLabels violet)
        | None -> dot $"C{i} {point p}" (pA p) 11 (colored lLabels red)
    // the crossing of the subject edge S2-S0 with the clip edge C1-C2
    let cross = vertices.[6]
    added.Add (doc.Objects.AddPoint (pA cross, colored lProblem orange))
    added.Add (doc.Objects.AddCircle (Circle (pA cross, tolerance * u), attrs lProblem))
    line (pA cross) (pA (5.0, 0.45)) (attrs lProblem)
    dot $"crossing {point cross} of subject edge S2-S0 and clip edge C1-C2" (pA (5.0, 0.45)) 11 (colored lProblem orange)
    // C2 is closer than the tolerance to the clip's own edge C0-C1: the snap that starts the trouble
    let (ax, ay), (bx, by), (cx, cy) = clip.[0], clip.[1], clip.[2]
    let t = ((cx - ax) * (bx - ax) + (cy - ay) * (by - ay)) / ((bx - ax) ** 2.0 + (by - ay) ** 2.0)
    let foot = ax + t * (bx - ax), ay + t * (by - ay)
    let d = sqrt ((cx - fst foot) ** 2.0 + (cy - snd foot) ** 2.0)
    added.Add (doc.Objects.AddCircle (Circle (pA clip.[2], tolerance * u), attrs lProblem))
    line (pA clip.[2]) (pA foot) (colored lProblem orange)
    line (pA foot) (pA (12.2, 1.1)) (attrs lProblem)
    dot $"C2 is {num d} from the clip's own edge C0-C1, less than the tolerance {num tolerance}:\nthat edge is snapped through C2 and becomes C0-C2-C1" (pA (12.2, 1.1)) 11 (colored lProblem orange)

    // ---------------- panel 2: the engine graph after snapping ----------------
    dot "2. the graph the engine builds from it" (pB (-1.4, 2.6)) 14 (colored lRoot Color.White)
    // the input triangles as faint dashed outlines, for orientation only
    let ghost =
        let a = colored lGraph (Color.FromArgb (225, 225, 225))
        a.DisplayOrder <- -1 // behind the graph edges that lie on top of them
        let dashed = doc.Linetypes.Find "Dashed"
        if dashed >= 0 then
            a.LinetypeSource <- ObjectLinetypeSource.LinetypeFromObject
            a.LinetypeIndex <- dashed
        a
    triangle subject pB ghost
    triangle clip pB (ghost.Duplicate ())
    let n = vertices.Length
    let reps = [| for i in 0 .. n - 1 do if representative.[i] = i then i |]
    // edges that pass through or within tolerance of a vertex that is not one of their ends
    let throughs =
        [| for e, (va, vb, _, _) in Array.indexed edges do
            let (ax, ay), (bx, by) = vertices.[va], vertices.[vb]
            for w in reps do
                if w <> va && w <> vb then
                    let wx, wy = vertices.[w]
                    let dx, dy = bx - ax, by - ay
                    let t = ((wx - ax) * dx + (wy - ay) * dy) / (dx * dx + dy * dy)
                    if t > 0.0 && t < 1.0 then
                        let dist = sqrt ((wx - ax - t * dx) ** 2.0 + (wy - ay - t * dy) ** 2.0)
                        if dist < tolerance then e, w, dist |]
    let onTop = throughs |> Array.filter (fun (_, _, dist) -> dist < 1e-9) |> Array.map (fun (e, _, _) -> e) |> Set.ofArray
    let legend = ResizeArray<string> ()
    for e, (va, vb, deltaS, deltaC) in Array.indexed edges do
        let a, b = vertices.[va], vertices.[vb]
        let bad = onTop.Contains e
        // an edge lying on top of others is drawn shifted a little to the lower right, so that both stay visible
        let shift = if bad then (0.05, -0.12) else (0.0, 0.0)
        let pa = pB (fst a + fst shift, snd a + snd shift)
        let pb = pB (fst b + fst shift, snd b + snd shift)
        let ea = if bad then colored lProblem orange else colored lGraph dark
        ea.DisplayOrder <- 1
        added.Add (doc.Objects.AddLine (pa, pb, ea))
        let mid = Point3d ((pa.X + pb.X) / 2.0, (pa.Y + pb.Y) / 2.0, 0.0)
        // a short edge gets its label beside it, on a leader, so that the label does not cover the vertex dots
        let side =
            let dv = pb - pa
            let nrm = Vector3d (-dv.Y, dv.X, 0.0)
            nrm.Unitize () |> ignore
            nrm * 0.45 * u
        let at = if pa.DistanceTo pb < 1.5 * u then mid + side else mid
        if at <> mid then line mid at (colored lGraph dark)
        dot $"e{e}" at 10 (colored lGraph (if bad then orange else Color.White))
        let signed (k: int) = k.ToString "+0;-0"
        let parts = [ if deltaS <> 0 then "subject " + signed deltaS
                      if deltaC <> 0 then "clip " + signed deltaC ]
        let partsText = String.Join (", ", parts)
        legend.Add $"e{e}  v{va} - v{vb}  winding change {partsText}"
    for e, w, dist in throughs do
        if dist < 1e-9 then legend.Add $"e{e} runs exactly through v{w}, but has no vertex there: it lies on top of the edges through v{w}"
        else legend.Add $"e{e} passes {num dist} from v{w}, inside the tolerance, without being split"
    legend.Add "e3 is the snapped half C2-C1 of the clip edge C0-C1. It lies on the clip's edge C1-C2, which the crossing split into e4 and e5."
    legend.Add "The clip winding changes of e3 (-1) and of e4, e5 (+1 along v4-v6-v5) should cancel, but they never meet: the windings are inconsistent."
    legend.Add "v2 (9,2): the four edges between it and v5 cancel each other, so it has no edges left."
    dot (String.Join ("\n", legend)) (pB (4.5, -1.6)) 11 (colored lGraph Color.White)
    for w in reps do
        let what = if w >= inputCount then "crossing " else ""
        let merged = [ for i in 0 .. n - 1 do if i <> w && representative.[i] = w then $" = v{i}" ]
        dot $"v{w}{String.Join (String.Empty, merged)} {what}{point vertices.[w]}" (pB vertices.[w]) 11 (colored lLabels (if w >= inputCount then orange else dark))

    // zoom to the drawing
    let mutable bb = BoundingBox.Empty
    for id in added do
        let o = doc.Objects.FindId id
        if not (isNull o) then bb.Union (o.Geometry.GetBoundingBox true)
    bb.Inflate (1.5 * u, 1.0 * u, 0.0)
    doc.Views.ActiveView.ActiveViewport.ZoomBoundingBox bb |> ignore
    doc.Views.Redraw ()
    $"deleted {deleted} objects of the earlier drawing, added {added.Count} objects on layer '{root}'"

printfn "%s" (onUi draw)
