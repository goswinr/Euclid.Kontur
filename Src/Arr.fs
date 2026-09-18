namespace BoolOps

/// <summary>Unchecked indexing of the engine's flat arrays.
/// Fable compiles every array index into a bounds checked call of one shared library function, and that call
/// took two thirds of the running time under Node: the function sees typed and untyped arrays from every call
/// site, so the engine cannot optimize it. These helpers emit a plain JavaScript index instead.
/// On .NET they are the ordinary indexer, inlined, with its usual bounds check.
/// Every index into a float[] or int[] of the engine goes through them, see DESIGN.md section 6.</summary>
module internal Arr =

    /// arr.[i] without a bounds check under Fable.
    let inline get (arr: 'T[]) (i: int) : 'T =
        #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
        Fable.Core.JsInterop.emitJsExpr (arr, i) "$0[$1]"
        #else
        arr.[i]
        #endif

    /// arr.[i] <- v without a bounds check under Fable.
    let inline set (arr: 'T[]) (i: int) (v: 'T) : unit =
        #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
        Fable.Core.JsInterop.emitJsStatement (arr, i, v) "$0[$1] = $2"
        #else
        arr.[i] <- v
        #endif
