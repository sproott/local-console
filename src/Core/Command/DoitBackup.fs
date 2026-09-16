namespace MF.LocalConsole

[<RequireQualifiedAccess>]
module DoitBackupCommand =
    open System.IO
    open Feather.ConsoleApplication
    open MF.Utils
    open Feather.ErrorHandling
    open MF.DoIt

    type Output =
        | Stdout of Feather.ConsoleApplication.Output
        | File of string

    type Credentials = Credentials

    [<RequireQualifiedAccess>]
    module Remote =
        let format ({ Name = name; Url = url }: Remote) =
            sprintf "<c:dark-yellow>%s</c>:%s" name url

    type RepositoryBackup = {
        Path: string
        Name: string
        Remotes: Remote list
        Untracked: string list
        NotCommited: string list
        Ignored: string list
    }

    [<RequireQualifiedAccess>]
    module RepositoryBackup =
        let private colorize title = function
            | 0 -> sprintf "<c:gray>%s[0]</c>" title
            | count -> sprintf "<c:yellow>%s[%i]</c>" title count

        let formatToList repository =
            [
                repository.Path |> sprintf "<c:cyan>%s</c>"
                repository.Remotes |> List.map Remote.format |> String.concat "; "
                repository.Untracked |> List.length |> colorize "Untracked"
                repository.NotCommited |> List.length |> colorize "NotCommited"
                repository.Ignored |> List.length |> colorize "Ignored"
            ]

    [<RequireQualifiedAccess>]
    module Directory =
        let ensure (path: string) =
            if path |> Directory.Exists |> not then Directory.CreateDirectory(path) |> ignore

    [<RequireQualifiedAccess>]
    module private Path =
        let fileName = String.split "/" >> List.rev >> List.head
        let dirName path =
            let file = path |> fileName
            path.Substring(0, path.Length - file.Length)

        module Operators =
            let (/) a b = Path.Combine(a, b)

    open Path.Operators
    open AsyncResult.Operators

    let private run (output: Feather.ConsoleApplication.Output) useStatic credentialsFile commandOutput = asyncResult {
        let! credentials =
            credentialsFile
            |> Credentials.parse
            |> AsyncResult.ofResult <@> List.singleton

        let! doItBackup =
            if useStatic then
                output.Message "  <c:dark-yellow>! NOTE:</c> Using a static dummy backup."
                DoItBackup.staticBackup ()
                |> AsyncResult.ofSuccess
            else
                credentials
                |> DoItBackup.load output

        match commandOutput with
        | Stdout output -> doItBackup |> Dump.backup output
        | File file ->
            doItBackup
            |> DoItBackup.saveToFile
                (Serialization.serialize >> Json.serializePretty)
                file
            output.Success $"[DoIt] Backup is saved in {file}"
    }

    open MF.LocalConsole.Console

    let arguments = []

    let options = [
        Option.required "credentials" (Some "c") "Name of the file with credentials for doit service." ".doit.json"
        Option.optional "output" (Some "o") "A path to file to output a backup." None
        Option.noValue "force" (Some "f") "Whether to overwrite an existing backup, if it exists."
        Option.noValue "static" None "Whether to use a static backup - it is just for a debugging purposes."
    ]

    let execute= Execute <| fun (input, output) ->
        let credentials = input |> Input.Option.value "credentials"
        let outputFile =
            match input with
            | Input.Option.OptionalValue "output" file -> Some file
            | _ -> None
        let useStatic =
            match input with
            | Input.Option.Has "static" _ -> true
            | _ -> false

        run output useStatic credentials (
            match outputFile, input with
            | Some file, Input.Option.Has "force" _ -> Output.File file
            | Some file, _ when file |> File.Exists -> failwithf "File %A already exists." file
            | Some file, _ -> Output.File file
            | _ -> Output.Stdout output
        )
        |> Async.RunSynchronously
        |> function
            | Ok () ->
                output.Success "Done"
                ExitCode.Success
            | Error errors ->
                output.Error <| sprintf "There is %A errors." errors.Length

                if output.IsVerbose() then
                    errors |> List.iter (fun e -> output.Error $" - {e.Message}")
                elif output.IsDebug() then
                    errors |> List.iter (sprintf "%A" >> output.Error)

                ExitCode.Error
