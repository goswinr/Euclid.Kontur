# BoolOps

[![BoolOps on nuget.org](https://img.shields.io/nuget/v/BoolOps)](https://www.nuget.org/packages/BoolOps/)
[![Build Status](https://github.com/goswinr/BoolOps/actions/workflows/build.yml/badge.svg)](https://github.com/goswinr/BoolOps/actions/workflows/build.yml)
[![Test Status](https://github.com/goswinr/BoolOps/actions/workflows/test.yml/badge.svg)](https://github.com/goswinr/BoolOps/actions/workflows/test.yml)
[![Docs Build Status](https://github.com/goswinr/BoolOps/actions/workflows/docs.yml/badge.svg)](https://github.com/goswinr/BoolOps/actions/workflows/docs.yml)
[![license](https://img.shields.io/github/license/goswinr/BoolOps)](LICENSE)

Boolean operations (union, intersection, difference, xor) on 2D polygons of the
[Euclid](https://github.com/goswinr/Euclid) geometry library.
Like Euclid itself it also compiles to JavaScript and TypeScript via [Fable](https://fable.io/).

**Status: early.** Union, intersection, difference, xor, simplify and union of many shapes work and are
tested against a point in region oracle on random and degenerate input, on .NET and under Node.
Not yet done: the faster winding propagation, benchmarks, the SVG visualisation, and hardening on real world
data. See [DESIGN.md](DESIGN.md) for the full design and the reasoning behind it.

## What is different from Clipper2 and iOverlay

- **Floats only.** Coordinates are never scaled to an integer grid. Input vertices come out of an operation
  unchanged, bit for bit. Only intersection points are new coordinates.
- **Robust by tolerance,** not by exact arithmetic. Everything closer than an absolute tolerance is equal:
  two points, a point and a segment, two collinear segments. This is the model of CAD software like Rhino.
- **No sweep line.** Segment pairs are found with a Bounding Volume Hierarchy, then the paths are cut,
  linked into a planar graph and walked.
- **The fill rule belongs to the Shape,** not to the operation. Subject and clip get separate winding numbers
  on every edge, and each fill rule is applied to its own winding number before the boolean combines them.
  So an EvenOdd glyph unioned with a NonZero CAD outline works in one operation.
- **Low allocation.** All intermediate state lives in flat arrays inside a reusable engine. A boolean operation
  allocates only the output `Polyline2D`s when the engine is reused.

## Usage

```fsharp
open Euclid
open BoolOps

let outline : Polyline2D = ... // closed: first point equals last point
let glyph : Polyline2D list = ...

let a = Shape.ofPolyline outline               // NonZero by default
let b = Shape.create (glyph, FillRule.EvenOdd) // the rule is part of the Shape

a.Contains (Pt (1.0, 2.0))  // point in region test under the Shape's fill rule

let cut = BoolOps.difference a b          // a fresh engine with the default tolerance of 1e-6
let merged = BoolOps.unionAll [ a; b ]    // each shape under its own rule, then one NonZero merge

// for many operations reuse one engine, it keeps all its buffers:
let engine = BoolOpsEngine 1e-4           // absolute tolerance in the units of the coordinates
let r = engine.Execute (a, b, ClipType.Intersection)
```

Results are always simple: no self intersections, no overlaps, outer contours counter clockwise,
holes clockwise, fill rule `Positive`. Input vertices keep their exact coordinates.

## Building and testing

```
dotnet build
dotnet run --project ./Test/Test.fsproj
cd Test && npm install && npm test   # the same tests under Node with Fable
```

## License

[MIT](LICENSE)
