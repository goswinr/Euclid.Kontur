# Fable array indexing: `arr.[i]` against an emitted `arr[i]`

Measurement of 2026-11-03. Question: does the `#if FABLE_COMPILER_JAVASCRIPT` switch in `Src/Arr.fs`, which emits a
plain JavaScript index instead of the F# indexer, make a difference, and what would an inline bounds check cost?

## Summary

Under Node the emitted `arr[i]` is about 3 to 8 times faster than the F# indexer `arr.[i]`. An inline JavaScript bounds
check keeps most of that gain but still costs about 2.4 times the unchecked index. On .NET all variants compile to the
same `arr.[i]`, so there is nothing to compare there.

| Variant | What Fable emits | 25 fixtures, geometric mean against unchecked | Range |
|---|---|---|---|
| Unchecked (current `Arr.get`) | `arr[i]` | 1.00 | |
| Inline JS bounds check | `(i >>> 0) < (arr.length >>> 0) ? arr[i] : Arr_outOfRange(i)` | 2.43 | 1.85 to 3.62 |
| F# syntax `arr.[i]` | `item(i, arr)`, one shared library function for every array type | 6.20 | 3.99 to 8.84 |

The results (areas, path counts, fixture checksums) are identical in all three builds, only the time changes.
The inline check is about 2.6 times faster than the F# `item(i, arr)` call (geometric mean over the same 25 fixtures).

## Why `item(i, arr)` is slow

Fable compiles `arr.[i]` into a call of `item` in its library, which checks the bounds and throws. Every call site in
the program goes through that one function with `Float64Array`, `Int32Array` and plain arrays, so V8 sees a megamorphic
access in it and cannot specialise or inline it. The emitted `arr[i]` and the inline check both keep the access in the
caller, where the array type is known and monomorphic. See `DESIGN.md` section 6.

## The three variants

1. **Unchecked.** The code as committed: `Fable.Core.JsInterop.emitJsExpr (arr, i) "$0[$1]"` and
   `emitJsStatement (arr, i, v) "$0[$1] = $2"`.
2. **F# syntax.** The `#if` forced off (the symbol renamed to one that is never defined), so Fable sees plain
   `arr.[i]` and `arr.[i] <- v`.
3. **Inline JS check.** A bounds check written in F# around the emit, with a non generic throwing helper, so the
   failure path is a single shared function and there is no generic `item`:

   ```fsharp
   let outOfRange (i: int) : 'T = raise (System.IndexOutOfRangeException(string i))

   let inline get (arr: 'T[]) (i: int) : 'T =
       if uint32 i < uint32 arr.Length then Fable.Core.JsInterop.emitJsExpr (arr, i) "$0[$1]" else outOfRange i
   ```

   `set` is the same with `emitJsStatement`. Fable emitted the expected code, for example
   `v = (((i_1 >>> 0) < (arr.length >>> 0)) ? (arr[i_1]) : Arr_outOfRange(i_1));`, and the engine contains 627 call
   sites of `Arr_outOfRange`.

## Setup

- Node v26.12.1, fable-library-js 5.26.0, Release build of `Bench.fsproj`, Windows 11, one machine.
- Each variant compiled with `dotnet fable Bench.fsproj --outDir <dir> --noCache --configuration Release` into a
  directory of its own. The Node scripts were run against it by copying them with `./_js/` replaced by the directory.
- The variants were run one after the other, never in parallel. The unchecked build was run again next to the inline
  check build, so the two comparisons each have a baseline from their own session. The unchecked numbers of the two
  sessions differ by up to about 25 percent on the small union cases (0.1 to 0.4 ms), by under 5 percent on the
  fixtures.
- The control: Klip and clipper2-ts, which do not use `Arr`, ran in `union-polysXY.mjs` in each pass and stayed
  within noise (for example Klip 0.276 against 0.280 ms at scale 100000).
- `Src/Arr.fs` was edited for each build and restored with `git checkout`, the build directories and script copies
  were deleted afterwards.

## Results

### Engine phases, `phases-polysXY.mjs`, scale 1, 2000 runs, ms per run

| Phase | Unchecked | F# syntax |
|---|---:|---:|
| ingest | 0.0063 | 0.0094 |
| sortRects | 0.0074 | 0.0384 |
| findIntersections | 0.0719 | 0.1618 |
| splitSegments | 0.0166 | 0.0476 |
| cluster | 0.0277 | 0.1023 |
| buildEdges | 0.0215 | 0.1343 |
| buildRings | 0.0170 | 0.0987 |
| propagate | 0.0201 | 0.0373 |
| select | 0.0040 | 0.0055 |
| link | 0.0077 | 0.0113 |
| **sum** | **0.2001** | **0.6466** |
| Simplify through the engine | 0.2065 | 0.6501 |

The inline check run (separate session, its own unchecked baseline): unchecked 0.2254 ms per run and 0.1979 ms per
call, inline check 0.5088 ms per run and 0.4874 ms per call.

Every phase is slower with the F# indexer. `buildEdges`, `buildRings`, `cluster` and `sortRects` suffer most (4 to 6
times), `ingest`, `select` and `link` least.

### Union of `polysXY.json`, `union-polysXY.mjs`, reused engine, ms per call

| Scale | Unchecked | F# syntax | Unchecked (2nd session) | Inline check |
|---:|---:|---:|---:|---:|
| 0.001 | 0.209 | 0.574 | 0.234 | 0.296 |
| 0.01 | 0.098 | 0.582 | 0.124 | 0.354 |
| 0.1 | 0.273 | 0.638 | 0.113 | 0.374 |
| 1 | 0.371 | 1.187 | 0.401 | 0.455 |
| 10 | 0.222 | 1.172 | 0.221 | 0.530 |
| 100 | 0.239 | 1.144 | 0.182 | 0.476 |
| 1000 | 0.256 | 1.139 | 0.183 | 0.476 |
| 10000 | 0.202 | 1.140 | 0.173 | 0.476 |
| 100000 | 0.203 | 1.132 | 0.188 | 0.474 |
| 1000000 | 0.200 | 1.352 | 0.195 | 0.514 |

A fresh engine per call is slower by the same relative amount in every variant.

### The 25 Klip fixtures, `bench-fixtures.mjs`, ms per call

Columns 2 to 4 come from one session (unchecked against F# syntax), columns 5 to 7 from the next (unchecked against the
inline check).

| Case | Unchecked | F# syntax | × | Unchecked | Inline check | × |
|---|---:|---:|---:|---:|---:|---:|
| intersection, medium overlapping polygons | 0.0238 | 0.1796 | 7.55 | 0.0237 | 0.0670 | 2.83 |
| intersection, large overlapping polygons | 0.1185 | 0.9384 | 7.92 | 0.1140 | 0.3478 | 3.05 |
| intersection, grid with rectangle | 0.0242 | 0.2101 | 8.68 | 0.0239 | 0.0726 | 3.04 |
| difference, medium overlapping polygons | 0.0250 | 0.1792 | 7.17 | 0.0239 | 0.0651 | 2.72 |
| difference, large overlapping polygons | 0.1193 | 0.9419 | 7.90 | 0.1199 | 0.3486 | 2.91 |
| xor, medium overlapping polygons | 0.0254 | 0.1835 | 7.22 | 0.0249 | 0.0674 | 2.71 |
| xor, large overlapping polygons | 0.1236 | 0.9786 | 7.92 | 0.1220 | 0.3585 | 2.94 |
| union, medium complex polygon | 0.0121 | 0.0867 | 7.17 | 0.0121 | 0.0310 | 2.56 |
| union, large complex polygon | 0.0592 | 0.4661 | 7.87 | 0.0598 | 0.1686 | 2.82 |
| union, very large complex polygon | 0.2595 | 2.0680 | 7.97 | 0.2562 | 0.7868 | 3.07 |
| union, medium grid of 25 rectangles | 0.0206 | 0.1669 | 8.10 | 0.0202 | 0.0572 | 2.83 |
| union, large grid of 100 rectangles | 0.1058 | 0.9350 | 8.84 | 0.1026 | 0.3710 | 3.62 |
| union, 3 disjoint rectangles | 0.0025 | 0.0124 | 4.96 | 0.0024 | 0.0049 | 2.04 |
| union, 2 overlapping rectangles | 0.0018 | 0.0087 | 4.83 | 0.0018 | 0.0034 | 1.89 |
| union, 4 simple circles | 0.0056 | 0.0375 | 6.70 | 0.0055 | 0.0133 | 2.42 |
| union, geo scale complex polygon | 0.0126 | 0.0868 | 6.89 | 0.0121 | 0.0312 | 2.58 |
| intersection, geo scale overlapping | 0.0250 | 0.1788 | 7.15 | 0.0246 | 0.0664 | 2.70 |
| intersection, 100 edge random polygon | 2.1677 | 8.6580 | 3.99 | 2.0149 | 3.7954 | 1.88 |
| union, 100 edge random polygons | 1.9128 | 8.4705 | 4.43 | 1.9403 | 3.6779 | 1.90 |
| difference, 100 edge random polygons | 2.0134 | 8.5490 | 4.25 | 2.0014 | 3.7784 | 1.89 |
| xor, 100 edge random polygons | 2.0657 | 8.8782 | 4.30 | 2.0937 | 3.8660 | 1.85 |
| intersection, 500 edge random polygon | 52.8736 | 242.1704 | 4.58 | 52.7696 | 102.9491 | 1.95 |
| union, 500 edge random polygons | 51.3679 | 229.2951 | 4.46 | 50.8063 | 100.0151 | 1.97 |
| difference, 500 edge random polygons | 52.7093 | 259.3378 | 4.92 | 52.2113 | 102.5998 | 1.97 |
| xor, 500 edge random polygons | 53.2865 | 244.2624 | 4.58 | 53.0510 | 104.0067 | 1.96 |
| **geometric mean** | | | **6.20** | | | **2.43** |

The factor is largest for the small and medium cases and smaller for the 100 and 500 edge random polygons, where
more of the time goes into work that does not index the flat arrays.

## Conclusions

- The `#if` switch with the emitted `arr[i]` is worth keeping. It is the difference between 0.2 and 0.65 ms for the
  polysXY union, and between a factor of 1 and 6 on the fixtures.
- `DESIGN.md` section 6 says the change made the polysXY union seven times faster. Today's measurement is about 3
  times on the engine phases and 3 to 5 times on the union, 6.2 times on the fixtures. The old figure was probably
  taken on another Fable or Node version, and the text could say "three to eight times" instead.
- An inline bounds check recovers most of the loss of the F# indexer (6.2 times down to 2.4 times) but still costs
  more than twice the unchecked index, so it is not a good default for the hot paths. It could be an option for
  debug builds or for development, behind an additional compiler symbol.
- The reason why the check is so expensive was not measured. Candidates are the extra compare and branch on every
  access of the tight loops, and the 627 additional call sites growing the functions beyond what V8 inlines. A
  variant that checks once per loop, or only the first and last index, was not tried.
- Not measured: .NET (all variants are the same code there), other Node versions, other machines, and the
  TypeScript target (same emit, so the same result is expected).
