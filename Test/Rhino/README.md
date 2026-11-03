# Exploratory scripts

Ad-hoc scripts for exploring engine behavior on real data. **Not part of CI.**
The benchmark and profiling scripts live in [`Bench/`](../../Bench/README.md).

## `rhino/` - run inside Rhino 8

The scripts reference the compiled library, so build it first: `bridge-repro.fsx` uses the Release build,
`pick.fsx` the Debug build.

| Script | Purpose |
| ------ | ------- |
| `bridge-repro.fsx` | The union of two polygons that touch through a narrow bridge, swept over shifts, rotations and scales, with Euclid.Kontur, Klip and Clipper2; draws the results into the Rhino document. |
| `pick.fsx` | A minimal script that loads Euclid.Kontur and Rhino.Scripting inside Rhino, a starting point for experiments. |
