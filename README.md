# BoolOps

[![BoolOps on nuget.org](https://img.shields.io/nuget/v/BoolOps)](https://www.nuget.org/packages/BoolOps/)
[![Build Status](https://github.com/goswinr/BoolOps/actions/workflows/build.yml/badge.svg)](https://github.com/goswinr/BoolOps/actions/workflows/build.yml)
[![Test Status](https://github.com/goswinr/BoolOps/actions/workflows/test.yml/badge.svg)](https://github.com/goswinr/BoolOps/actions/workflows/test.yml)
[![Docs Build Status](https://github.com/goswinr/BoolOps/actions/workflows/docs.yml/badge.svg)](https://github.com/goswinr/BoolOps/actions/workflows/docs.yml)
[![license](https://img.shields.io/github/license/goswinr/BoolOps)](LICENSE)

Boolean operations (union, intersection, difference, xor) on 2D polygons of the
[Euclid](https://github.com/goswinr/Euclid) geometry library.
Like Euclid itself it also compiles to JavaScript and TypeScript via [Fable](https://fable.io/).

**Status: under construction.** The public types `Shape`, `FillRule` and `ClipType` and the internal
foundations exist, the boolean operations themselves do not yet. See [DESIGN.md](DESIGN.md) for the
full design and the reasoning behind it.

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
```

Boolean operations will follow the API laid out in [DESIGN.md](DESIGN.md).

## Building and testing

```
dotnet build
dotnet run --project ./Test/Test.fsproj
cd Test && npm install && npm test   # the same tests under Node with Fable
```

## License

[MIT](LICENSE)
