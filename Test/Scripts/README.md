# Exploratory scripts

Ad-hoc `dotnet fsi` and Node scripts for exploring engine behavior on real data. **Not part of CI.**

The F# scripts reference the compiled library via a relative
`#r "../../../Src/bin/Release/net6.0/BoolOps.dll"`, so build it first. All commands run from the repository root:

```bash
dotnet build -c Release Src/BoolOps.fsproj
dotnet fsi Test/Scripts/console/union-polysXY.fsx
```

The Node script imports the Fable output of the test project and of the `klipjs/` wrapper, so build both first.
`npm run testJS` also runs the test suite, that is fine:

```bash
cd Test && npm install && npm run testJS && npm run buildKlipJs && cd ..
node Test/Scripts/console/union-polysXY.mjs
```

## `console/` - run with plain `dotnet fsi` or node

| Script | Purpose |
| ------ | ------- |
| `union-polysXY.fsx` | Self union of the noisy `data/polysXY.json` dataset at ten scales, compared against Klip (nuget) and Clipper2 at the matching precision, with the average time per call of each library. |
| `union-polysXY.mjs` | The same dataset through the Fable compiled JavaScript of BoolOps, compared against the Fable compiled Klip nuget and [clipper2-ts](https://github.com/countertype/clipper2-ts). Reads BoolOps from `Test/dist/testsRelease` and Klip from `klipjs/_js`, see the build commands above. |

## `klipjs/`

`KlipJs.fsproj` references the Klip nuget and wraps its inline path constructors in plain functions, so that Fable can
compile the package to JavaScript for `union-polysXY.mjs`. Build it with `npm run buildKlipJs` from the `Test` folder.

## `data/`

`polysXY.json`: the noisy polygon dataset from the Klip repository, exported from Rhino via its `rhinoToJson.fsx`.
