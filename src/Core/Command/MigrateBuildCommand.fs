namespace MF.LocalConsole

[<RequireQualifiedAccess>]
module MigrateBuildCommand =
    open System.IO
    open FSharp.Data
    open Fake.Core
    open MF.ConsoleApplication
    open MF.ErrorHandling

    let arguments = [
        Argument.optional "path" "Path to the target repository." (Some ".")
    ]

    let options = [
        Option.optional "alma-version" None "Alma.Build version to pin. Defaults to the newest version published on NuGet." None
        Option.noValue "dry-run" None "Print the planned edits/deletions/process invocations without touching the filesystem or running any process."
    ]

    let private latestPublishedAlmaVersion () =
        try
            "https://api.nuget.org/v3-flatcontainer/alma.build/index.json"
            |> Http.RequestString
            |> JsonValue.Parse
            |> fun json -> json.GetProperty("versions").AsArray()
            |> Array.map _.AsString()
            |> Array.filter (fun v -> v.Contains "-" |> not)
            |> Array.sortBy System.Version
            |> Array.tryLast
            |> Option.map (System.Version >> fun v -> sprintf "%d.%d" v.Major v.Minor)
            |> Result.ofOption "No published Alma.Build version found on NuGet."
        with ex -> Error <| sprintf "Failed to look up the latest Alma.Build version: %s" ex.Message

    [<RequireQualifiedAccess>]
    module private Lines =
        let split (content: string) = content.Replace("\r\n", "\n").Split('\n') |> Array.toList
        let join (lines: string list) = lines |> String.concat "\n"

    [<RequireQualifiedAccess>]
    module private BuildGroup =
        /// Replaces the superseded entries of the `group Build` section by a single `entry` line, put where its entries start.
        let migrate isEntry isSuperseded entry (content: string) =
            let lines = content |> Lines.split

            maybe {
                let! groupIndex = lines |> List.tryFindIndex (fun l -> l.Trim() = "group Build")
                let header, group = lines |> List.splitAt (groupIndex + 1)
                let anchor = group |> List.tryFindIndex isEntry |> Option.defaultValue group.Length
                let beforeEntries, entries = group |> List.splitAt anchor

                return header @ beforeEntries @ [entry] @ (entries |> List.filter (isSuperseded >> not)) |> Lines.join
            }
            |> Option.defaultValue content

    [<RequireQualifiedAccess>]
    module private PaketDependencies =
        let private isEntry (line: string) = line.TrimStart().StartsWith "nuget "

        let private isSuperseded (line: string) =
            let line = line.TrimStart()
            line.StartsWith "nuget Alma.Build " || line.StartsWith "nuget Fake."

        let migrate version =
            BuildGroup.migrate isEntry isSuperseded (sprintf "    nuget Alma.Build ~> %s" version)

    [<RequireQualifiedAccess>]
    module private PaketReferences =
        let private isEntry (line: string) = line.Trim() <> ""

        let private isSuperseded (line: string) =
            let line = line.TrimStart()
            line.StartsWith "Alma.Build" || line.StartsWith "Fake."

        let migrate = BuildGroup.migrate isEntry isSuperseded "    Alma.Build"

    let private supersededBuildFiles = [
        "Utils.fs"; "Targets.fs"; "Commands.fs"; "Command.fs"; "RtkFilter.fs"; "SafeBuildHelpers.fs"
    ]

    [<RequireQualifiedAccess>]
    module private BuildFsproj =
        let migrate (content: string) =
            content
            |> Lines.split
            |> List.filter (fun line ->
                supersededBuildFiles
                |> List.forall (fun file -> line.Contains(sprintf "Include=\"%s\"" file) |> not)
            )
            |> Lines.join

    [<RequireQualifiedAccess>]
    module private BuildFs =
        let private stripHeader lines =
            lines |> List.skipWhile (fun (line: string) -> line.StartsWith "//" || line.Trim() = "")

        let private isBlank (line: string) = line.Trim() = ""

        let private collapseBlanks lines =
            lines
            |> List.fold (fun acc line ->
                match acc with
                | previous :: _ when isBlank previous && isBlank line -> acc
                | _ -> line :: acc
            ) []
            |> List.rev

        let private isImportOrBlank (line: string) = line.StartsWith "open " || isBlank line

        /// Removes the Fake imports, collapsing the blank lines they might leave behind - only within the imports section, to keep the code below formatted as it is.
        let private removeFakeImports lines =
            maybe {
                let! importsStart = lines |> List.tryFindIndex (fun (line: string) -> line.StartsWith "open ")
                let beforeImports, fromImports = lines |> List.splitAt importsStart
                let imports, code = fromImports |> List.splitAt (fromImports |> List.takeWhile isImportOrBlank |> List.length)

                let imports =
                    imports
                    |> List.filter (fun (line: string) -> line.StartsWith "open Fake." |> not)
                    |> collapseBlanks

                return beforeImports @ imports @ code
            }
            |> Option.defaultValue lines

        let migrate (content: string) =
            content
            |> Lines.split
            |> stripHeader
            |> List.map (function
                | "open ProjectBuild" -> "open Alma.Build"
                | line -> line
            )
            |> removeFakeImports
            |> Lines.join

    type private Action =
        | EditFile of path: string * transform: (string -> string)
        | DeleteFile of path: string

    let private buildPlan (almaVersion: string) (repoRoot: string) =
        [
            EditFile (Path.Combine(repoRoot, "paket.dependencies"), PaketDependencies.migrate almaVersion)
            EditFile (Path.Combine(repoRoot, "build", "paket.references"), PaketReferences.migrate)
            EditFile (Path.Combine(repoRoot, "build", "build.fsproj"), BuildFsproj.migrate)
            EditFile (Path.Combine(repoRoot, "build", "Build.fs"), BuildFs.migrate)

            yield!
                supersededBuildFiles
                |> List.map (fun file -> Path.Combine(repoRoot, "build", file))
                |> List.filter File.Exists
                |> List.map DeleteFile
        ]

    let private verificationSteps = [
        "dotnet", "tool restore"
        "dotnet", "tool run paket install"
        "dotnet", "build ./build/build.fsproj"
        "dotnet", "run --no-build --project ./build/build.fsproj -- Bootstrap"
        "./build.sh", "Tests"
    ]

    let private resolveRepoRoot (path: string) =
        let result =
            CreateProcess.fromRawCommandLine "git" "rev-parse --show-toplevel"
            |> CreateProcess.withWorkingDirectory path
            |> CreateProcess.redirectOutput
            |> Proc.run

        if result.ExitCode = 0 then Ok (result.Result.Output.Trim())
        else Error (result.Result.Error.Trim())

    let private runProcess (workDir: string) (exe: string) (args: string) =
        CreateProcess.fromRawCommandLine exe args
        |> CreateProcess.withWorkingDirectory workDir
        |> Proc.run

    let private requireSuccess errorPrefix result =
        if result.ExitCode = 0 then Ok ()
        else Error <| sprintf "%s (exit %d)" errorPrefix result.ExitCode

    let private runSequential f items = result {
        for item in items do
            do! f item
    }

    let private relativeTo (repoRoot: string) (path: string) = Path.GetRelativePath(repoRoot, path)

    let private applyAction (output: Output) repoRoot = function
        | EditFile (path, transform) ->
            let relative = path |> relativeTo repoRoot
            output.Message <| sprintf "<c:cyan>edit</c> %s" relative

            try Ok <| File.WriteAllText(path, path |> File.ReadAllText |> transform)
            with ex -> Error <| sprintf "Failed to edit %s: %s" relative ex.Message
        | DeleteFile path ->
            let relative = path |> relativeTo repoRoot
            output.Message <| sprintf "<c:red>delete</c> %s" relative

            runProcess repoRoot "git" (sprintf "rm -q -- %s" relative)
            |> requireSuccess (sprintf "git rm failed for %s" relative)

    let private runVerificationStep (output: Output) repoRoot (exe, args) =
        output.Section <| sprintf "%s %s" exe args

        runProcess repoRoot exe args
        |> requireSuccess (sprintf "%s %s failed" exe args)

    let private renderDryRun (output: Output) repoRoot actions =
        actions
        |> List.iter (function
            | EditFile (path, _) -> output.Message <| sprintf "<c:cyan>would edit</c> %s" (path |> relativeTo repoRoot)
            | DeleteFile path -> output.Message <| sprintf "<c:red>would delete</c> %s" (path |> relativeTo repoRoot)
        )

        verificationSteps
        |> List.iter (fun (exe, args) -> output.Message <| sprintf "<c:gray>would run</c> %s %s" exe args)

    let execute = Execute <| fun (input, output) ->
        let path = input |> Input.Argument.value "path"

        let isDryRun =
            match input with
            | Input.Option.Has "dry-run" _ -> true
            | _ -> false

        let migration = result {
            let! almaVersion =
                match input with
                | Input.Option.OptionalValue "alma-version" version -> Ok version
                | _ -> latestPublishedAlmaVersion ()

            let! repoRoot =
                path
                |> resolveRepoRoot
                |> Result.mapError (sprintf "Path %A is not a git repository: %s" path)

            let actions = repoRoot |> buildPlan almaVersion

            if isDryRun then
                return actions |> renderDryRun output repoRoot
            else
                do! actions |> runSequential (applyAction output repoRoot)
                return! verificationSteps |> runSequential (runVerificationStep output repoRoot)
        }

        match migration with
        | Ok () ->
            if not isDryRun then output.Success "Done"
            ExitCode.Success
        | Error e ->
            output.Error e
            ExitCode.Error
