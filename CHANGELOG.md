# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Boolean operations failed with `Kontur.Link: no outgoing result edge` or `half edge is used twice` when two segments met end to end almost collinear, as they do between results that share crossing vertices, for example a `NonZero` and a `Positive` simplification of the same polygon. The intersect phase found a crossing in the rounding noise of their determinant. Segments that share an end now never get a crossing, and a crossing must be the same point on both segments within tolerance.

### Changed

- Breaking: `Polyline2D.Simplify`, `SimplifyWith`, `simplify` and `simplifyWith` now keep only the regions a polyline winds around in its main direction, the sign of its signed area, using `FillRule.Positive` for a counter clockwise and `FillRule.Negative` for a clockwise polyline. Loops that wind the other way, like the inverted loops of an offset, are dropped for either input orientation. The old `NonZero` behavior is `Kontur.simplify (Kontur.createSingleton (polyline, FillRule.NonZero))`.

## [0.2.0] - 2026-10-10

### Added

- Extension members on `Polyline2D` for boolean operations without creating a `Kontur`: instance members `Union`, `Intersection`, `Difference`, `Xor`, `Simplify` and `UnionMany`, static functions `union`, `intersection`, `difference`, `xor`, `simplify` and `unionMany`, each with a `...With` variant taking an explicit tolerance. Every polyline is one region under `FillRule.NonZero` and the result is a `ResizeArray<Polyline2D>`.

### Changed

- Breaking: renamed `Kontur.ofPolyline` to `Kontur.createSingleton`.
- Updated to Euclid 0.52.0.

## [0.1.0] - 2026-09-20

### Added

- First public release of Kontur.
- Boolean operations on Euclid `Polyline2D` polygons: union, intersection, difference and xor, plus simplification of self intersections and union of many regions.
- `Kontur`, `FillRule` and `ClipType`, with NonZero, EvenOdd, Positive and Negative fill rules chosen independently for each `Kontur`; point containment, winding number, bounds and signed area queries.
- `KonturEngine` with `Execute`, `Simplify` and `UnionAll`, reusable scratch buffers, and an explicit absolute tolerance; convenience functions in the `Kontur` module use a default tolerance of `1e-6` or accept one through their `...With` variants.
- Floating-point coordinates without integer-grid quantization; tolerance-based handling of touching vertices, T junctions, collinear overlaps, duplicate points, spikes and self intersections, preserving the coordinates of retained input vertices.
- Closed result contours with counterclockwise outer boundaries, clockwise holes and `FillRule.Positive`; collinear input vertices are retained by default.
- A flat-array pipeline with sweep-and-prune segment pairing, vertex clustering, graph construction, winding propagation seeded from graph edges, and contour linking; a private BVH accelerates winding queries across disconnected components.
- Validation of open paths, invalid tolerances and non-finite coordinates, with errors reported through Euclid.
- .NET 6.0 and .NET Framework 4.7.2 targets, with Fable source included in the NuGet package for JavaScript and TypeScript compilation.
- Shared .NET and Node tests covering point-in-region oracles, area identities, random and degenerate polygons, the Klip regression cases and 195 Clipper2 polygon fixtures.
- Shared benchmark fixtures and scripts comparing Kontur with Klip and Clipper2 on .NET and with Klip and clipper2-ts on Node, plus noisy polygon datasets, phase profiling and Rhino reproduction scripts.
- API documentation, usage examples and the reviewed algorithm design.
