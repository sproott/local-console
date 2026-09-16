namespace MF.LocalConsole

[<RequireQualifiedAccess>]
module RepositoryBuildListCommand =
    open Feather.ConsoleApplication
    open MF.LocalConsole.Console
    open MF.Utils

    type Filter = {
        BuildType: string option
        Version: string option
    }

    let private formatBuildType = function
        | "Library fake build" as buildType -> sprintf "<c:%s>%s</c>" "yellow" buildType
        | "Public Library fake build" as buildType -> sprintf "<c:%s>%s</c>" "dark-yellow" buildType
        | "Console Application fake build" as buildType -> sprintf "<c:%s>%s</c>" "cyan" buildType
        | "Application fake build" as buildType -> sprintf "<c:%s>%s</c>" "green" buildType
        | "SAFE app fake build" as buildType -> sprintf "<c:%s>%s</c>" "magenta" buildType
        | buildType -> buildType

    let private formatVersion: string -> string = function
        | oldVersion when oldVersion.Contains "-" -> sprintf "<c:%s>%s</c>" "red" oldVersion
        | version -> version

    // Input: "// === F# / Application fake build =========================================================== 2020-03-02"
    // Output: Some ("Application fake build", "2020-03-02")
    let private parseVersionLine (line: string) =
        match line.Trim('/').Split('/', 2) |> List.ofSeq with
        | [] -> None
        | _ :: [ typeAndVersion ] ->
            match typeAndVersion.Trim('=').Split('=') |> List.ofSeq |> List.filter (String.length >> (<) 1) with
            | [] -> None
            | [ buildType; version ] ->
                Some (
                    buildType.Trim() |> formatBuildType,
                    version.Trim() |> formatVersion
                )
            | _ -> None
        | _ -> None

    let private run (output: Feather.ConsoleApplication.Output) filter paths =
        paths
        |> FileSystem.getAllFiles
        |> List.filter (fun file -> [ "build.fsx"; "Build.fs" ] |> List.exists (fun sub -> String.contains sub file))
        |> List.filter (String.contains "cache" >> not)
        //|> tee (List.length >> sprintf "Builds[%A]" >> output.Message)
        |> List.collect (fun path ->
            path
            |> FileSystem.readLines
            |> List.filter (String.startsWith "// === F# /")
            |> List.tryHead
            |> Option.bind (fun versionLine ->
                match versionLine |> parseVersionLine with
                | Some (buildType, version) -> Some [ path; buildType; version ]
                | None -> None
            )
            |> Option.toList
        )
        |> List.choose (function
            | [ _path; buildType; version ] as item ->
                match filter with
                | { BuildType = Some filterBuildType; Version = Some filterVersion } when buildType.Contains filterBuildType && version.Contains filterVersion -> Some item
                | { BuildType = Some filterBuildType; Version = None } when buildType.Contains filterBuildType -> Some item
                | { BuildType = None; Version = Some filterVersion } when version.Contains filterVersion -> Some item
                | { BuildType = None; Version = None } -> Some item
                | _ -> None
            | _ -> None
        )
        //|> List.distinct
        |> output.Options "Build list:"

        ()

    let execute = Execute <| fun (input, output) ->
        let paths = input |> Input.getRepositories

        let filter: Filter = {
            BuildType =
                match input with
                | Input.Option.OptionalValue "type" buildType -> Some buildType
                | _ -> None
            Version =
                match input with
                | Input.Option.OptionalValue "build-version" version -> Some version
                | _ -> None
        }

        paths
        |> run output filter

        output.Success "Done"
        ExitCode.Success
