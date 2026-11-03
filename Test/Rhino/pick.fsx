#r "RhinoCommon"
#r "../../../Src/bin/Debug/net6.0/Euclid.Kontur.dll"

#r "nuget: Rhino.Scripting.FSharp,  0.14.0"
#r "nuget: Euclid.Rhino, 0.51.0"

open Euclid
open Rhino.Scripting
open Rhino.Scripting.FSharp
type rs = RhinoScriptSyntax

rs.Print "Ok"