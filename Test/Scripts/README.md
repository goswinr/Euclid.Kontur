# Exploratory scripts

Ad-hoc `dotnet fsi` scripts for exploring engine behavior on real data. **Not part of CI.**

The F# scripts reference the compiled library via a relative
`#r "../../../Src/bin/Release/net6.0/BoolOps.dll"`, so build it first:

```bash
dotnet build -c Release Src/BoolOps.fsproj
```

## `console/` - run with plain `dotnet fsi` or node

| Script | Purpose |
| ------ | ------- |
| `union-polysXY.fsx` | Self union of the noisy `data/polysXY.json` dataset at ten scales, compared against Klip (nuget) and Clipper2 at the matching precision, with the average time per call of each library. |
| `union-polysXY.mjs` | The same dataset through the Fable compiled JavaScript of BoolOps, compared against the Fable compiled Klip nuget and [clipper2-ts](https://github.com/countertype/clipper2-ts). Needs `cd Test && npm install && npm run testJS && npm run buildKlipJs` first: the first build writes BoolOps to `Test/dist/testsRelease`, the second writes Klip to `klipjs/_js`. |

## `klipjs/`

`KlipJs.fsproj` references the Klip nuget and wraps its inline path constructors in plain functions, so that Fable can
compile the package to JavaScript for `union-polysXY.mjs`. Build it with `npm run buildKlipJs` from the `Test` folder.

## `data/`

`polysXY.json`: the noisy polygon dataset from the Klip repository, exported from Rhino via its `rhinoToJson.fsx`.
