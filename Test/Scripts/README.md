# Exploratory scripts

Ad-hoc `dotnet fsi` scripts for exploring engine behavior on real data. **Not part of CI.**

The F# scripts reference the compiled library via a relative
`#r "../../../Src/bin/Release/net6.0/BoolOps.dll"`, so build it first:

```bash
dotnet build -c Release Src/BoolOps.fsproj
```

## `console/` - run with plain `dotnet fsi`

| Script | Purpose |
| ------ | ------- |
| `union-polysXY.fsx` | Self union of the noisy `data/polysXY.json` dataset at ten scales, compared against Klip (nuget) and Clipper2 at the matching precision. |

## `data/`

`polysXY.json`: the noisy polygon dataset from the Klip repository, exported from Rhino via its `rhinoToJson.fsx`.
