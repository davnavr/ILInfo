#r "paket:
nuget Fake.Core.Target
nuget Fake.DotNet.Cli
nuget Fake.IO.FileSystem //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators

let rootDir = __SOURCE_DIRECTORY__
let docsDir = rootDir </> "docs"
let outDir = rootDir </> "out"
let testDir = rootDir </> "test"

let slnFile = rootDir </> "FSharpIL.sln"

let handleErr msg: ProcessResult -> _ =
    function
    | { ExitCode = ecode } when ecode <> 0 ->
        failwithf "Process exited with code %i: %s" ecode msg
    | { ExitCode = 0; Results = messages } ->
        for { IsError = err; Message = msg } in messages do
            if err || msg.Contains "Test failed" then failwith msg
    | _ -> ()

Target.create "Clean" <| fun _ ->
    [
        !!(docsDir </> "content" </> "exout" </> "*.dll")
        !!(outDir </> "*.nupkg")
        !!(outDir </> "docs" </> "**" </> "*.*")
    ]
    |> List.iter File.deleteAll

    // TODO Clean bin and obj folders instead of calling "dotnet clean", since it doesn't clear out outputs for netstandard2.1 folders.
    DotNet.exec id "clean" slnFile |> handleErr "Error occured while cleaning project output"

Target.create "Build" <| fun _ ->
    DotNet.build
        (fun opt ->
            { opt with
                Configuration = DotNet.Release
                NoRestore = true })
        slnFile

Target.create "Generate Examples" <| fun _ ->
    docsDir </> "FSharpIL.Documentation.Examples" </> "FSharpIL.Documentation.Examples.fsproj"
    |> sprintf "-p %s -c Release --no-build --no-restore"
    |> DotNet.exec id "run"
    |> handleErr "One or more examples is invalid"

Target.create "Build Documentation" <| fun _ ->
    sprintf
        "-p %s -c Release --no-build --no-restore -- --content-directory %s --style-directory %s --output-directory %s"
        (docsDir </> "FSharpIL.Documentation" </> "FSharpIL.Documentation.fsproj")
        (docsDir </> "content")
        (docsDir </> "style")
        (outDir </> "docs")
    |> DotNet.exec id "run"
    |> handleErr "Error occured while generating documentation"

Target.create "Test" <| fun _ ->
    let proj = testDir </> "FSharpIL.Tests" </> "FSharpIL.Tests.fsproj"
    for tfm in [ "netcoreapp3.1"; "net5.0" ] do
        sprintf
            "-p %s -c Release -f %s --no-build --no-restore"
            proj
            tfm
        |> DotNet.exec id "run"
        |> handleErr "One or more tests failed"

Target.create "Test Examples" <| fun _ ->
    testDir </> "FSharpIL.Tests.Examples" </> "FSharpIL.Tests.Examples.fsproj"
    |> sprintf "-p %s -c Release --no-build --no-restore"
    |> DotNet.exec id "run"
    |> handleErr "One or more tests failed"

Target.create "Publish" <| fun _ ->
    Trace.trace "Publishing..."

"Clean"
==> "Build"
==> "Test"
==> "Publish"

"Build"
==> "Generate Examples"
==> "Test Examples"
==> "Build Documentation"
==> "Publish"

Target.runOrDefault "Publish"
