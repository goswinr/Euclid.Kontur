// Self union of the noisy data/polysXY.json dataset at ten scales, through the Fable compiled JavaScript of BoolOps,
// compared against the Fable compiled Klip nuget and against clipper2-ts (https://github.com/countertype/clipper2-ts)
// at the matching precision, in result and in time per call. The Node counterpart of union-polysXY.fsx.
// One off exploratory script, not part of CI.
//
// Build the JavaScript first:  cd Test && npm install && npm run testJS && npm run buildKlipJs
// Then run:                    node Test/Scripts/console/union-polysXY.mjs

import { readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

import { simplify, defaultTolerance } from "../../dist/testsRelease/Src/BoolOps.js";
import { Shape_create_6F27E24C as createShape, Shape__get_PathCount as pathCount, Shape__get_SignedArea as signedArea } from "../../dist/testsRelease/Src/Shape.js";
import { BoolOpsEngine_$ctor_5E38073B as createEngine, BoolOpsEngine__Simplify_133820C6 as engineSimplify } from "../../dist/testsRelease/Src/Engine.js";
import {
    Polyline2D_createDirectly_EF0D882 as createPolyline,
    Polyline2D__CloseInPlace_5E38073B as closeInPlace,
    Polyline2D__ReverseInPlace as reverseInPlace,
    Polyline2D__get_SignedArea as polylineSignedArea,
} from "../../dist/testsRelease/fable_modules/Euclid.0.51.0/Src/Polyline2D.fs.js";

import { toPaths as toKlipPaths, unionSelfChecked as klipUnionSelfChecked } from "../klipjs/_js/KlipJs.js";

import { booleanOpD, ClipType, FillRule } from "clipper2-ts";

const NonZero = 1; // BoolOps.FillRule.NonZero

/** How often each timed call is repeated after one warmup call. */
const repetitions = 20;

/** Runs the function once untimed, then repetitions times timed. Returns the last result and the average milliseconds per call. */
function timed(f) {
    let result = f();
    const start = performance.now();
    for (let i = 0; i < repetitions; i++) result = f();
    const ms = (performance.now() - start) / repetitions;
    return [result, ms.toFixed(3)];
}

/** Scales every coordinate by the factor, so BoolOps with its absolute tolerance sees the same geometry that Clipper2 sees at the matching precision. */
function scaled(scale, xy) {
    return xy.map(path => path.map(p => ({ x: p.x * scale, y: p.y * scale })));
}

/** One interleaved x y coordinate array per path, the layout of both Euclid.Polyline2D and Klip.Path64. */
function toXYs(xy) {
    return xy.map(path => {
        const xys = new Array(path.length * 2);
        for (let i = 0; i < path.length; i++) {
            xys[2 * i] = path[i].x;
            xys[2 * i + 1] = path[i].y;
        }
        return xys;
    });
}

/** Like Klip.unionSelfChecked: reverse clockwise paths so none of them count as holes under NonZero. */
function toShape(xy) {
    const paths = toXYs(xy).map(xys => {
        const pl = createPolyline(xys);
        closeInPlace(pl);
        if (polylineSignedArea(pl) < 0) reverseInPlace(pl);
        return pl;
    });
    return createShape(paths, NonZero);
}

const scriptDir = dirname(fileURLToPath(import.meta.url));
const xyGroups = JSON.parse(await readFile(resolve(scriptDir, "../data/polysXY.json"), "utf8"));
const xy = xyGroups.map(group => group[0]); // only the outer path of each polygon

console.log(`Original Paths: ${xy.length}, ${repetitions} timed runs per call after one warmup, the input is built outside the timing`);
const engine = createEngine(defaultTolerance);
for (let i = -3; i <= 6; i++) {
    const scale = 10 ** i;
    const xys = scaled(scale, xy);

    // BoolOps, a fresh engine per call and one reused engine:
    const shape = toShape(xys);
    const [br, tFresh] = timed(() => simplify(shape));
    const [, tReused] = timed(() => engineSimplify(engine, shape));
    console.log(`BoolOps:     Scale: ${scale}, Result Paths: ${pathCount(br)}, Area: ${signedArea(br) / (scale * scale)}, ${tFresh} ms fresh engine, ${tReused} ms reused engine`);

    // Klip, the Fable compiled nuget:
    const kPaths = toKlipPaths(toXYs(xys));
    const [kr, tKlip] = timed(() => klipUnionSelfChecked(kPaths));
    console.log(`Klip:        Scale: ${scale}, Result Paths: ${kr.length}, ${tKlip} ms`);

    // clipper2-ts, on the unscaled input with the matching precision:
    const [cr, tClipper] = timed(() => booleanOpD(ClipType.Union, xy, null, FillRule.NonZero, i));
    console.log(`clipper2-ts: Scale: ${scale}, Result Paths: ${cr.length}, ${tClipper} ms\n-`);
}
