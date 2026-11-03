namespace Euclid

/// <summary>Unchecked indexing of the engine's flat arrays.
/// Fable compiles every array index into a bounds checked call of one shared library function, and that call
/// took two thirds of the running time under Node: the function sees typed and untyped arrays from every call
/// site, so the engine cannot optimize it. These helpers emit a plain JavaScript index instead.
/// On .NET they are the ordinary indexer, inlined, with its usual bounds check.
/// Every index into a float[] or int[] of the engine goes through them, see DESIGN.md section 6.</summary>
module internal Arr =

    /// arr.[i] without a bounds check under Fable.
    let inline get (arr: 'T[]) (i: int) : 'T =
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
        Fable.Core.JsInterop.emitJsExpr (arr, i) "$0[$1]"
        #else
        arr.[i]
        #endif

    /// arr.[i] <- v without a bounds check under Fable.
    let inline set (arr: 'T[]) (i: int) (v: 'T) : unit =
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
        Fable.Core.JsInterop.emitJsStatement (arr, i, v) "$0[$1] = $2"
        #else
        arr.[i] <- v
        #endif


    /// A new float array of the given size that starts with the first count items of arr.
    /// The items from count on are unspecified: under Fable all of arr is copied with one native call.
    let newWithSizeFloat (arr: float[]) (count: int) (size: int) : float[] =
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
        Fable.Core.JsInterop.emitJsStatement (arr, count, size) """
            const newBuffer = new Float64Array($2);
            if ($1 > 0) newBuffer.set($0);
            return newBuffer;"""
        #else
        let bigger = Array.zeroCreate<float> size
        Array.blit arr 0 bigger 0 count
        bigger
        #endif

    /// A new int array of the given size that starts with the first count items of arr.
    /// The items from count on are unspecified: under Fable all of arr is copied with one native call.
    let newWithSizeInt (arr: int[]) (count: int) (size: int) : int[] =
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
        Fable.Core.JsInterop.emitJsStatement (arr, count, size) """
            const newBuffer = new Int32Array($2);
            if ($1 > 0) newBuffer.set($0);
            return newBuffer;"""
        #else
        let bigger = Array.zeroCreate<int> size
        Array.blit arr 0 bigger 0 count
        bigger
        #endif


module internal Rarr =

    /// arr.[i] without a bounds check under Fable.
    let inline get (arr: 'T ResizeArray) (i: int) : 'T =
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
        Fable.Core.JsInterop.emitJsExpr (arr, i) "$0[$1]"
        #else
        arr.[i]
        #endif
