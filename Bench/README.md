# Benchmarks

Benchmark and profiling scripts for Euclid.Kontur on .NET and under Node, against Klip and Clipper2. **Not part of CI**,
except that `Bench.fsproj` is in the solution, so a plain `dotnet build` keeps `Bench.fs` compiling against the library.

The F# scripts reference the compiled library via a relative `#r "../Src/bin/Release/net6.0/Euclid.Kontur.dll"`,
so build it first. All commands run from the repository root:

```bash
dotnet build -c Release Src/Euclid.Kontur.fsproj
dotnet fsi Bench/union-polysXY.fsx
```

The Node scripts import the Fable output of `Bench.fsproj` from `Bench/_js` and of the `klipjs/` wrapper from
`Bench/klipjs/_js`, so build both first. `npm run build` does both, `npm run buildJS` only the first one:

```bash
cd Bench && npm install && npm run build && cd ..
node Bench/union-polysXY.mjs
```

## Scripts

| Script | Purpose |
| ------ | ------- |
| `union-polysXY.fsx` | Self union of the noisy `data/polysXY.json` dataset at ten scales, compared against Klip (nuget) and Clipper2 at the matching precision, with the average time per call of each library. |
| `union-polysXY.mjs` | The same dataset through the Fable compiled JavaScript of Euclid.Kontur, compared against the Fable compiled Klip nuget and [clipper2-ts](https://github.com/countertype/clipper2-ts). |
| `phases-polysXY.mjs` | The time of every phase of the engine on the same dataset, each timed in place inside a full pipeline run. Optional arguments: the scale and the count of runs. Needs only `npm run buildJS`. |
| `bench-fixtures.fsx` | The benchmark fixtures of the Klip repository on .NET: Euclid.Kontur against Klip and Clipper2 on the same input, 25 cases. |
| `bench-fixtures.mjs` | The same 25 cases under Node: Euclid.Kontur against Klip and [clipper2-ts](https://github.com/countertype/clipper2-ts). |

## The same fixtures on both runtimes

`bench-fixtures.fsx` and `bench-fixtures.mjs` both drive `Bench.fs`, which holds the fixture generators and the
timing harness. The `.fsx` loads that file as source, the `.mjs` imports the JavaScript that Fable compiled from
`Bench.fsproj`, so the two runtimes run the identical workload and the numbers can be compared across them.
Two details make that work:

- Coordinates are rounded with `floor (v + 0.5)`, the rule of JavaScript's `Math.round`. `System.Math.Round`
  rounds a half to even, so the two runtimes would disagree on every coordinate landing exactly on `.5`.
- The dense random polygons use a Lehmer generator whose intermediate product stays below `2^53`, so the float
  arithmetic is exact on both targets. `System.Random` differs between Fable and .NET, and between .NET versions.

Each script prints a checksum over the coordinates of every case, and the result path count and area. Those
columns must be identical in both runs; only the millisecond column may differ.

## `klipjs/`

`KlipJs.fsproj` references the Klip nuget and wraps its inline path constructors in plain functions, so that Fable can
compile the package to JavaScript for `union-polysXY.mjs` and `bench-fixtures.mjs`. Build it with
`npm run buildKlipJs` from the `Bench` folder.

## `data/`

Copied from the Klip repository (`Test/Scripts/data`). `polysXY.json` is the noisy polygon dataset and
`polysXYOrig.json` the same polygons without the noise, both exported by Klip's `rhinoToJson.fsx` from the
polylines in `union.3dm`. `unionAllScale.3dm` and `poly.png` came along with them.

## `Notes/`

Working notes of the performance sessions, with the recorded results. The paths in them are as of the session.
