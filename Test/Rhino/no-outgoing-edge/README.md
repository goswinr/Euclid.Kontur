# Rhino drawings of engine failures

Drawings of inputs that made the engine fail, to see what went wrong. **Not part of CI.**

| File | Content |
| ---- | ------- |
| `no-outgoing-edge.fsx` | Draws the reproduction below into the active Rhino document. |
| `no-outgoing-edge.3dm` | The drawing, saved. |
| `no-outgoing-edge.png` | A capture of the drawing. Its last title line predates commit 7141487 and calls the repair uncommitted. |

## Running the scripts

Run them in Rhino 8 or 9 from an F# scripting host that has RhinoCommon loaded, like Caddis or Fesh.
They work on Rhino's UI thread and from a background thread, like Caddis in background mode.
They need no build of Euclid.Kontur: the engine state they draw is written into the script.
A rerun replaces the earlier drawing, and deletes only the objects on the script's own layer tree.

## `no-outgoing-edge`: a snapped segment on a split edge

On eb789f3 every operation on these two triangles threw from `Link`:

```fsharp
let subject = Kontur.createSingleton (poly [ 0.0, 1.0; 0.0, 2.0; 9.0, 2.0 ])
let clip    = Kontur.createSingleton (poly [ 9.0, 2.0; 0.0, 0.0; 8.0, 2.0 ])
KonturEngine(0.4).Execute (subject, clip, op)
```

```text
Union:        Euclid.Kontur.Link: no outgoing result edge at vertex 4, the winding numbers are inconsistent.
Intersection: Euclid.Kontur.Link: no outgoing result edge at vertex 1, the winding numbers are inconsistent.
Difference:   Euclid.Kontur.Link: no outgoing result edge at vertex 4, the winding numbers are inconsistent.
Xor:          Euclid.Kontur.Link: no outgoing result edge at vertex 4, the winding numbers are inconsistent.
```

It fails the same way for any tolerance from 0.25 to 0.8 at these integer coordinates, and at tolerance 1e-6 with all
coordinates times 2.5e-6, the form the test uses. There, 7 of the 8 runs of the four operations in both operand orders threw.
Only Xor with the operands swapped passed.

### Where it came from

Random paths from `Bench.randomPath` on an integer grid of 8 to 16, scaled by 2.5e-6 so that the grid spacing is
2.5 tolerances. On eb789f3, 24 operations of 6 seeds (20, 24, 40, 48, 76, 80) failed in `Link`, and all of them passed
at scale 1. Shrinking them while they kept failing (drop paths, drop vertices, move coordinates towards zero) gave
reproductions of mostly 6 to 10 vertices. This one comes from the Intersection of seed 24 and fails over the widest range of scales.

### What goes wrong

The names are those of the drawing: S subject vertex, C clip vertex, v graph vertex, e graph edge.

1. The clip is a sliver. Its vertex C2 (8,2) is 0.217 from its own edge C0-C1, from (9,2) to (0,0), less than the
   tolerance 0.4. So that edge is snapped through C2 and becomes C0-C2-C1.
2. The snapped half C2-C1, graph edge e3, lies exactly on the clip's other edge C1-C2.
3. The subject edge S2-S0 crosses C1-C2 at (7.2, 1.8). The crossing vertex v6 splits C1-C2 into e4 and e5, but not e3:
   e3 runs through v6 without a vertex there. The subject edge e2, from (0,2) to (8,2), also passes 0.2 from v6 without being split.
4. The graph is no longer planar. The clip winding changes of e3 (-1) and of e4, e5 (+1) should cancel but never meet,
   so the winding numbers are inconsistent and `Link` finds no outgoing result edge.

### The fix

Commit 7141487, "Repair the graph when merged or snapped edges keep it from linking", added `Graph.repair`.
Here it reroutes the edges through the vertices they pass within tolerance, in 2 rounds, without merging any vertices.
The test "a segment snapped through a vertex of its own path is rerouted through the crossing on it" in
`Test/TestEngine.fs` runs this input at scale 2.5e-6 and tolerance 1e-6. It checks that the repair runs for all four
operations, and that both operand orders link and pass `Graph.validate`.

### Open: the result areas

Areas in grid units, at scale 2.5e-6 and tolerance 1e-6, the same on 7141487 and 749dd6c:

| Operation | subject, clip | clip, subject |
| --------- | ------------: | ------------: |
| Union | 3.6 | 3.6 |
| Intersection | 0 | 0 |
| Difference | 3.6 | 0 |
| Xor | 3.6 | 4.4 |

After the snap the clip has no area left: C0-C2-C1-C2-C0 runs back on itself. So ideally Union, Xor and subject minus
clip are the subject, area 4.5, and Intersection and clip minus subject are empty. The empty ones match. The others lose
0.9, or 0.1 for the swapped Xor. Moving vertices within the tolerance can change the area this much here, but Xor
differs between the operand orders: the swapped Xor links without any repair and keeps 4.4, the repaired runs give 3.6.
Not investigated yet. The test cannot see it: its point oracle only tests points 3 tolerances (1.2 grid units) from
every edge, and no point inside these thin triangles is that far.
