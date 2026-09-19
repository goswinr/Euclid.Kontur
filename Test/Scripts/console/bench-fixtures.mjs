// The benchmark fixtures of the Klip repository under Node: Euclid.Kontur against Klip and clipper2-ts on the same
// input. The fixtures and the timing harness are imported from the Fable compiled Test/Bench.fs, the very file
// the .NET counterpart bench-fixtures.fsx loads, so both runtimes run the identical workload. The printed
// fixture checksums must match between the two runs.
// One off exploratory script, not part of CI.
//
// Build the JavaScript first:  cd Test && npm install && npm run testJS && npm run buildKlipJs
// Then run:                    node Test/Scripts/console/bench-fixtures.mjs

import {
    cases,
    timePerCall,
    toKontur,
    opName,
    runKontur,
} from "../../dist/testsRelease/Bench.js";
import { defaultTolerance } from "../../dist/testsRelease/Src/KonturModule.js";
import { Kontur__get_Paths } from "../../dist/testsRelease/Src/Kontur.js";
import {
    KonturEngine_$ctor_5E38073B as createEngine,
    KonturEngine__Execute_2778F657 as execute,
} from "../../dist/testsRelease/Src/Engine.js";
import { Polyline2D__get_SignedArea as polylineSignedArea } from "../../dist/testsRelease/fable_modules/Euclid.0.51.0/Src/Polyline2D.fs.js";

import { toPaths as toKlipPaths, booleanOp as klipBooleanOp, netArea as klipNetArea } from "../klipjs/_js/KlipJs.js";

import { booleanOp as clipperBooleanOp, areaPaths as clipperAreaPaths } from "clipper2-ts";

const NonZero = 1; // Euclid.FillRule.NonZero, and the same value in Klip and in clipper2-ts

/** The ClipType of Klip and of clipper2-ts for one of the four Kontur operations. Both use the Clipper numbering. */
function clipperOp(op) {
    switch (op) {
        case 1: return 1; // Intersection
        case 2: return 3; // Difference
        case 3: return 4; // Xor
        default: return 2; // Union
    }
}

/** Klip's flat buffer paths from the interleaved x y arrays of a fixture. An empty list means no clip. */
function toKlip(paths) {
    return toKlipPaths(paths.map(p => Array.from(p)));
}

/** clipper2-ts paths: one array of {x, y} per path. Every fixture coordinate is a whole number, so all three
 *  libraries see the same shape on the integer grid. */
function toClipper(paths) {
    return paths.map(p => {
        const pts = new Array(p.length / 2);
        for (let i = 0; i < pts.length; i++) pts[i] = { x: p[2 * i], y: p[2 * i + 1] };
        return pts;
    });
}

/** The net area of a result: the sum of the signed areas of its paths. All three libraries return positively
 *  oriented results with clockwise holes, so the signed sum is the area of the region and can be compared
 *  between them. A sum of absolute areas could not: it also counts every hole as positive. */
function konturArea(shape) {
    let a = 0;
    for (const p of Kontur__get_Paths(shape)) a += polylineSignedArea(p);
    return a;
}

const nodeVersion = process.version;
console.log("# The Klip benchmark fixtures under Node");
console.log("");
console.log(`${cases.length} cases, Euclid.Kontur (this build) through Fable, Klip 3.1.1 through Fable, clipper2-ts, Node ${nodeVersion}`);
console.log("Time per call: the mean of the fastest of five batches after a warmup, see Bench.timePerCall.");

// the same table the .NET script prints, so the two runtimes can be compared case by case:
runKontur(`Node ${nodeVersion}`);

console.log("");
console.log("### Euclid.Kontur against Klip and clipper2-ts, milliseconds per call");
console.log("");
console.log("| Case | Op | Euclid.Kontur | Klip | clipper2-ts | Klip / Euclid.Kontur | clipper2-ts / Euclid.Kontur |");
console.log("| --- | --- | ---: | ---: | ---: | ---: | ---: |");

const engine = createEngine(defaultTolerance);
const disagree = [];
for (const c of cases) {
    const subject = toKontur(c.Subject, NonZero);
    const clip = toKontur(c.Clip, NonZero);
    const kSubject = toKlip(c.Subject);
    const kClip = toKlip(c.Clip);
    const cSubject = toClipper(c.Subject);
    const cClip = c.Clip.length === 0 ? null : toClipper(c.Clip);
    const op = clipperOp(c.Op);

    const bMs = timePerCall(() => { execute(engine, subject, clip, c.Op); });
    const kMs = timePerCall(() => { klipBooleanOp(op, NonZero, kSubject, kClip); });
    const cMs = timePerCall(() => { clipperBooleanOp(op, cSubject, cClip, NonZero); });
    console.log(`| ${c.Name} | ${opName(c.Op)} | ${bMs.toFixed(4)} | ${kMs.toFixed(4)} | ${cMs.toFixed(4)} | ${(kMs / bMs).toFixed(2)}x | ${(cMs / bMs).toFixed(2)}x |`);

    // the areas of the three libraries on the same input, as a correctness cross check:
    const bArea = konturArea(execute(engine, subject, clip, c.Op));
    const kArea = klipNetArea(klipBooleanOp(op, NonZero, kSubject, kClip));
    const cArea = clipperAreaPaths(clipperBooleanOp(op, cSubject, cClip, NonZero));
    const biggest = Math.max(Math.abs(bArea), Math.abs(kArea), Math.abs(cArea));
    if (biggest > 0 && (Math.abs(bArea - kArea) / biggest > 0.005 || Math.abs(bArea - cArea) / biggest > 0.005)) {
        disagree.push(`| ${c.Name} | ${opName(c.Op)} | ${bArea.toFixed(0)} | ${kArea.toFixed(0)} | ${cArea.toFixed(0)} |`);
    }
}

console.log("");
if (disagree.length === 0) {
    console.log("All three libraries agree on the area of every case within half a percent.");
} else {
    console.log("### Cases where the areas differ by more than half a percent");
    console.log("");
    console.log("| Case | Op | Euclid.Kontur area | Klip area | clipper2-ts area |");
    console.log("| --- | --- | ---: | ---: | ---: |");
    for (const line of disagree) console.log(line);
}
