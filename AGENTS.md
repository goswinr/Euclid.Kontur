# Notes for coding agents

BoolOps is an F# library. It targets .NET (`net6.0` and `net472`) and also compiles to
JavaScript and TypeScript with Fable, so everything has to stay Fable compatible.
Read `DESIGN.md` first: it holds the reviewed design, the pipeline and the reasons behind every decision.

## Conventions

- Follow the coding standards of the Euclid repository: PascalCase types and members, camelCase functions,
  4 spaces, XML docstrings on every public member, errors through `Euclid.EuclidErrors.fail`.
- Hot paths use raw `float[]` and `int[]` with a separate count, never `ResizeArray`, tuples or struct records.
  Fable compiles raw numeric arrays to typed arrays.
- No `int64`, `Span`, `stackalloc`, `ArrayPool` or `Array.Sort(keys, items)`. They are not available on Fable or net472.
- Visitor callbacks are `inline` members with `[<InlineIfLambda>]` parameters. State a visitor needs lives in
  fields, not in captured `let mutable` locals.
- Tests use Scriptorium (Quill for the DSL, Nib for the assertions) and run unchanged on .NET and on Node:
  - `dotnet run --project ./Test/Test.fsproj`
  - `cd Test && npm install && npm test`

## Cloud sessions

In cloud containers the .NET SDK is not preinstalled. `.claude/hooks/session-start.sh` installs it
asynchronously, so `dotnet` may not be on `PATH` in the first minute. Wait and retry.

## CHANGELOG.md must not use wrapped bullets

`Src/BoolOps.fsproj` derives the package version from `CHANGELOG.md` with `Ionide.KeepAChangelog.Tasks` 0.3.3.
Its parser throws on a bullet that wraps onto an indented continuation line, and that failure aborts the
whole build. Keep one bullet on one line, however long, and use nested `- ` sub-bullets for structure.
