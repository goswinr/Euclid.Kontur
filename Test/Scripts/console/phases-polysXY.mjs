// Time per phase of the Euclid.Kontur engine on the polysXY dataset, through the Fable compiled JavaScript.
// Every phase is timed in place inside a full pipeline run, so it sees the real input and the real cache and
// branch predictor state. Timing a phase in a loop of its own reports half the cost, see DESIGN.md section 6.
// One off exploratory script, not part of CI.
//
// Build the JavaScript first:  cd Test && npm install && npm run testJS
// Then run:                    node Test/Scripts/console/phases-polysXY.mjs [scale] [runs]

import { readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

import * as E from "../../dist/testsRelease/Src/EngineState.js";
import * as Ingest from "../../dist/testsRelease/Src/Ingest.js";
import * as Intersect from "../../dist/testsRelease/Src/Intersect.js";
import * as Graph from "../../dist/testsRelease/Src/Graph.js";
import * as Winding from "../../dist/testsRelease/Src/Winding.js";
import * as Link from "../../dist/testsRelease/Src/Link.js";
import { Kontur_create_1C811A3C as createKontur, Kontur_empty_Z6955A6A8 as emptyKontur } from "../../dist/testsRelease/Src/Kontur.js";
import { KonturEngine_$ctor_5E38073B as createEngine, KonturEngine__get_State as engineState, KonturEngine__Simplify_418445A0 as engineSimplify } from "../../dist/testsRelease/Src/Engine.js";
import { defaultTolerance } from "../../dist/testsRelease/Src/KonturModule.js";
import {
    Polyline2D_createDirectly_EF0D882 as createPolyline,
    Polyline2D__CloseInPlace_5E38073B as closeInPlace,
    Polyline2D__ReverseInPlace as reverseInPlace,
    Polyline2D__get_SignedArea as polylineSignedArea,
} from "../../dist/testsRelease/fable_modules/Euclid.0.51.0/Src/Polyline2D.fs.js";

const NonZero = 1; // Euclid.FillRule.NonZero
const Union = 0; // Euclid.ClipType.Union
const scale = Number(process.argv[2] ?? "1");
const runs = Number(process.argv[3] ?? "2000");

const scriptDir = dirname(fileURLToPath(import.meta.url));
const xyGroups = JSON.parse(await readFile(resolve(scriptDir, "../data/polysXY.json"), "utf8"));
const paths = xyGroups.map(group => group[0]).map(path => {
    const xys = new Array(path.length * 2);
    for (let i = 0; i < path.length; i++) {
        xys[2 * i] = path[i].x * scale;
        xys[2 * i + 1] = path[i].y * scale;
    }
    const pl = createPolyline(xys);
    closeInPlace(pl);
    if (polylineSignedArea(pl) < 0) reverseInPlace(pl);
    return pl;
});
const shape = createKontur(paths, NonZero);
const clip = emptyKontur(NonZero);
const engine = createEngine(defaultTolerance);
const s = engineState(engine);

// the phases of KonturEngine.Execute, see Src/Engine.fs:
const phases = [
    ["ingest", () => { E.EngineState__Clear(s); Ingest.addKontur(s, shape, 0); Ingest.addKontur(s, clip, 1); Ingest.finish(s); }],
    ["sortRects", () => Intersect.sortRects(s)],
    ["findIntersections", () => Intersect.findIntersections(s)],
    ["splitSegments", () => Intersect.splitSegments(s)],
    ["cluster", () => Graph.cluster(s)],
    ["buildEdges", () => Graph.buildEdges(s)],
    ["buildRings", () => Graph.buildRings(s)],
    ["propagate", () => Winding.propagate(s)],
    ["select", () => Link.select(s, NonZero, NonZero, Union)],
    ["link", () => Link.link(s, [])],
];

for (let i = 0; i < 200; i++) for (const [, run] of phases) run(); // warmup
const totals = new Map(phases.map(([name]) => [name, 0]));
for (let i = 0; i < runs; i++) {
    for (const [name, run] of phases) {
        const start = performance.now();
        run();
        totals.set(name, totals.get(name) + (performance.now() - start));
    }
}
let sum = 0;
for (const t of totals.values()) sum += t;
console.log(`Scale ${scale}, ${runs} runs: ${(sum / runs).toFixed(4)} ms per run, the sum of the phases`);
for (const [name, t] of totals) console.log(`  ${name.padEnd(18)} ${(t / runs).toFixed(4)} ms  ${(100 * t / sum).toFixed(1).padStart(5)}%`);
console.log(`Vertices ${E.EngineState__get_VertexCount(s)} (input ${E.EngineState__get_InputVertexCount(s)}), segments ${E.EngineState__get_SegCount(s)}, events ${E.EngineState__get_EvCount(s)}, sub segments ${E.EngineState__get_ECount(s)}, graph edges ${E.EngineState__get_GCount(s)}`);

for (let i = 0; i < 200; i++) engineSimplify(engine, shape); // warmup
const start = performance.now();
for (let i = 0; i < runs; i++) engineSimplify(engine, shape);
console.log(`Simplify through the engine: ${((performance.now() - start) / runs).toFixed(4)} ms per call`);
