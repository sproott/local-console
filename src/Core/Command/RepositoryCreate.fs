namespace MF.LocalConsole

[<RequireQualifiedAccess>]
module RepositoryCreateCommand =
    open System.IO
    open FSharp.Data
    open Feather.ConsoleApplication
    open MF.Utils
    open LibGit2Sharp

    type private BackupSchema = JsonProvider<"schema/backup.json">

    type Mode =
        | CreateShell
        | DryRun
        | CreateRepositories

    let mutable private reposWithError: (string list) list = []
    let mutable private shellLines: string list = []

    let private addRepoError message =
        reposWithError <- message :: reposWithError

    let private addShellCommand command =
        shellLines <- command :: shellLines

    let private createRepository (output: Feather.ConsoleApplication.Output) mode repositoryName =
        match mode with
        | CreateShell -> sprintf "echo \"Create %s ...\"" repositoryName |> addShellCommand
        | DryRun
        | CreateRepositories -> output.Section <| sprintf "Create %s" repositoryName

    let private skipRepository (output: Feather.ConsoleApplication.Output) mode reason =
        match mode with
        | CreateShell -> sprintf "echo \" - skipped for %s ...\"" reason |> addShellCommand
        | DryRun
        | CreateRepositories -> output.Message <| sprintf "<c:yellow> - skipped for %s</c>" reason

    let private ensureDir (output: Feather.ConsoleApplication.Output) mode dir =
        match mode with
        | CreateShell -> sprintf "mkdir -p %A" dir |> addShellCommand
        | DryRun -> output.Message <| sprintf " - <c:cyan>Directory.ensure</c> %A -> %s" dir (if Directory.Exists dir then "<c:gray>already there</c>" else "<c:yellow>create</c>")
        | CreateRepositories -> Directory.ensure dir

    let private copyFile (output: Feather.ConsoleApplication.Output) mode repositoryName source target =
        match mode with
        | CreateShell ->
            if File.Exists source then sprintf "cp -R %A %A" source target |> addShellCommand
            else [repositoryName; sprintf "! file %A does not exists" source] |> addRepoError
        | DryRun -> output.Message <| sprintf " - <c:cyan>File.Copy</c> %A -> %A" source target
        | CreateRepositories ->
            try File.Copy(source, target, true)
            with | e -> [repositoryName; sprintf "! %s" e.Message] |> addRepoError

    open Path.Operators

    let private cloneRepository (output: Feather.ConsoleApplication.Output) mode repositoryName url targetDir =
        match mode with
        | CreateShell -> sprintf "git clone %s %A" url (targetDir / repositoryName) |> addShellCommand
        | DryRun -> output.Message <| sprintf " - <c:cyan>Repository.Clone</c> %A -> %A" url targetDir
        | CreateRepositories -> Repository.Clone(url, targetDir) |> ignore  // todo - https://stackoverflow.com/questions/40700154/clone-a-git-repository-with-ssh-and-libgit2sharp

    let private run (output: Feather.ConsoleApplication.Output) (ignoredRemotes: string list) mode backupDir =
        let repositories =
            [ backupDir ]
            |> FileSystem.getAllFiles
            |> List.filter (fun f -> f.EndsWith("backup.json"))

        let createRepository = createRepository output mode
        let skipRepositoryMessage = skipRepository output mode
        let ensureDir = ensureDir output mode
        let copyFile = copyFile output mode
        let cloneRepository = cloneRepository output mode

        let rec copyFiles repositoryName backupRepositoryDir targetDir = function
            | [] -> ()
            | files ->
                files
                |> List.iter (fun file ->
                    let fileTargetPath = targetDir / file

                    if output.IsDebug() then output.Message <| sprintf " - Copy file %s -> %s" (backupRepositoryDir / file) fileTargetPath

                    if Directory.Exists (backupRepositoryDir / file)
                        then
                            backupRepositoryDir / file
                            |> Directory.GetFiles
                            |> Seq.toList
                            |> copyFiles repositoryName (backupRepositoryDir / file) fileTargetPath
                        else
                            let fileTargetDir = fileTargetPath |> Path.dirName

                            ensureDir fileTargetDir
                            copyFile repositoryName (backupRepositoryDir / file) fileTargetPath
                )

        repositories
        |> List.iter (fun backupFile ->
            let repositoryBackupPath = Path.GetDirectoryName(backupFile)
            let repository = backupFile |> File.ReadAllText |> BackupSchema.Parse

            createRepository repository.Name

            let topLevelDir =
                repository.Path
                |> Path.dirName
                |> tee ensureDir

            let remotes = repository.Remotes |> Seq.toList

            if remotes |> List.exists (fun r -> ignoredRemotes |> List.exists (fun ignored -> r.Url.Contains(ignored)))
            then skipRepositoryMessage "ignored remote"
            else
                let url =
                    remotes
                    |> List.tryFind (fun r -> r.Name = "origin")
                    |> Option.bind (fun _ -> repository.Remotes |> Seq.tryHead)
                    |> Option.map (fun r -> r.Url)

                if output.IsVerbose() then output.Message <| sprintf " - remote url: %A" url

                if Directory.Exists repository.Path then skipRepositoryMessage "already there"
                else
                    match url with
                    | Some url -> cloneRepository repository.Name url topLevelDir
                    | _ ->
                        output.Message "<c:yellow> - no remote defined</c>"
                        ensureDir repository.Path

                let copyFiles subdir = copyFiles repository.Name (repositoryBackupPath / subdir) repository.Path

                repository.Ignored |> Seq.toList |> copyFiles "ignored"
                repository.Untracked |> Seq.toList |> copyFiles "untracked"
                repository.NotCommited |> Seq.toList |> List.distinct |> copyFiles "notCommited"

                output.Success " - done"
        )

        "#!/bin/bash" :: "" :: "set -e" :: ""
        :: ("echo \"Done\"" :: shellLines |> List.rev)
        |> FileSystem.writeSeqToFile "output-shell.sh"

        reposWithError
        |> List.distinct
        |> List.sort
        |> output.Options "Repository with errors:"

    let execute = Execute <| fun (input, output) ->
        let backupDir = input |> Input.Argument.value "backup"

        let mode =
            match input with
            | Input.Option.Has "dry-run" _ -> DryRun
            | Input.Option.Has "use-shell" _ -> CreateShell
            | _ -> CreateRepositories

        let ignoredRemotes =
            match input with
            | Input.Option.Value "ignore-remote" ignored -> ignored |> FileSystem.readLines
            | _ -> []

        backupDir
        |> run output ignoredRemotes mode

        output.Success "Done"
        ExitCode.Success
