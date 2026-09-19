module Kontur.Tests

open Scriptorium.Quill
open type Scriptorium.Quill.Runner

#if !FABLE_COMPILER
open System.Globalization
open System.Threading
Thread.CurrentThread.CurrentCulture   <- CultureInfo.GetCultureInfo "en-US" // so that a float never has a comma as decimal separator
Thread.CurrentThread.CurrentUICulture <- CultureInfo.GetCultureInfo "en-US"
#endif

/// The geometric tests build trees and graphs of thousands of edges,
/// so they need much more than the default five seconds per test.
let private config = timeout 600_000

/// runTests returns the exit code on .NET and calls process.exit with it under Node.
[<EntryPoint>]
let main _ =
    runTestsWith (config, [
        TestFillRule.tests
        TestKontur.tests
        TestBuffers.tests
        TestBvh.tests
        TestEngine.tests
        TestKlip.tests
        ])
