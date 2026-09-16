namespace MF.LocalConsole

[<RequireQualifiedAccess>]
module RepositoryBackupCommand =
    open System
    open System.IO
    open Feather.ConsoleApplication
    open MF.Utils
    open LibGit2Sharp

    type Output =
        | Stdout of Feather.ConsoleApplication.Output
        | File of string

    type CompleteRepository =
        | All
        | OnlyComlete
        | OnlyIncomplete

    type Remote = {
        Name: string
        Url: string
    }

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

    let private run (output: Feather.ConsoleApplication.Output) completeRepository ignoredFiles ignoredRepositories commandOutput paths =
        let entryPath (entry: StatusEntry) = entry.FilePath

        let repositories =
            paths
            |> FileSystem.getAllDirs
            |> List.filter (String.trimEnd '/' >> sprintf "%s/.git" >> Directory.Exists)

        let repositories =
            match ignoredRepositories with
            | [] -> repositories
            | ignoredRepositories ->
                repositories
                |> List.filterNotInBy Path.fileName ignoredRepositories

        let progress = repositories |> List.length |> output.ProgressStart "Getting repository informations ..."

        let repositories =
            repositories
            //|> List.take 5 // todo !!!
            |> List.map (fun path ->
                use repo = new Repository(path)

                let status = repo.RetrieveStatus()

                {
                    Path = path
                    Name = path |> Path.fileName
                    Remotes = repo.Network.Remotes |> Seq.map (fun r -> { Name = r.Name; Url = r.Url }) |> Seq.toList
                    Untracked = [
                        yield! status.Untracked |> Seq.map entryPath
                    ]
                    NotCommited = [
                        yield! status.Added |> Seq.map entryPath
                        yield! status.Modified |> Seq.map entryPath
                        yield! status.Removed |> Seq.map entryPath
                        yield! status.RenamedInIndex |> Seq.map entryPath
                        yield! status.Staged |> Seq.map entryPath
                        yield! status.Unaltered |> Seq.map entryPath
                    ]
                    Ignored =
                        [
                            yield! status.Ignored |> Seq.map entryPath
                        ]
                        |> List.filterNotIn ignoredFiles
                }
                |> tee (fun _ -> progress |> output.ProgressAdvance)
            )
            |> List.filter (fun repo ->
                match completeRepository with
                | All -> true
                | OnlyComlete -> repo.Untracked @ repo.NotCommited @ repo.Ignored |> List.isEmpty
                | OnlyIncomplete -> repo.Untracked @ repo.NotCommited @ repo.Ignored |> List.isEmpty |> not
            )

        progress |> output.ProgressFinish

        (* let rec collectIgnored acc = function
            | [] -> acc |> List.groupBy id |> List.sortByDescending (snd >> List.length)
            | repo :: repos ->
                repos |> collectIgnored (acc @ repo.Ignored)

        repositories
        |> collectIgnored []
        |> List.filter (snd >> List.length >> (>=) 1)    // x > 1
        |> List.map (fun (file, occurences) -> sprintf "%s [%i]" file (occurences |> List.length))
        |> FileSystem.writeSeqToFile "output/ignored-minor.txt" *)

        match commandOutput with
        | Stdout output ->
            repositories
            |> List.map (RepositoryBackup.formatToList)
            |> output.Options (sprintf "Repositories[%i]:" (repositories |> List.length))

        | File path ->
            let mutable reposWithError = []

            let rec copyFiles repository sourceDir targetDir = function
                | [] -> ()
                | files ->
                    files
                    |> List.iter (fun file ->
                        let fileTargetPath = targetDir / file

                        if Directory.Exists (sourceDir / file)
                            then
                                sourceDir / file
                                |> Directory.GetFiles
                                |> Seq.toList
                                |> copyFiles repository (sourceDir / file) fileTargetPath
                            else
                                let fileTargetDir = fileTargetPath |> Path.dirName

                                Directory.ensure fileTargetDir

                                try
                                    File.Copy(sourceDir / file, fileTargetPath, true)
                                with
                                | e ->
                                    let message = sprintf "! %s" e.Message

                                    reposWithError <- [repository; message] :: reposWithError
                    )

            output.NewLine()
            let progress = repositories |> List.length |> output.ProgressStart "Backuping repositories ..."

            repositories
            |> List.iter (fun repository ->
                let repositoryBackup = path / repository.Name
                Directory.ensure repositoryBackup

                repository
                |> Json.serialize
                |> FileSystem.writeToFile (repositoryBackup / "backup.json")

                repository.NotCommited |> List.distinct |> copyFiles repository.Name repository.Path (repositoryBackup / "notCommited")
                repository.Untracked |> copyFiles repository.Name repository.Path (repositoryBackup / "untracked")
                repository.Ignored |> copyFiles repository.Name repository.Path (repositoryBackup / "ignored")

                progress |> output.ProgressAdvance
            )

            progress |> output.ProgressFinish

            reposWithError
            |> List.distinct
            |> List.sort
            |> output.Options "Repository with errors:"

    open MF.LocalConsole.Console

    let execute = Execute <| fun (input, output) ->
        let paths = input |> Input.getRepositories
        let outputFile =
            match input with
            | Input.Option.OptionalValue "output" file -> Some file
            | _ -> None

        let completeRepository =
            match input with
            | Input.Option.Has "only-complete" _ -> CompleteRepository.OnlyComlete
            | Input.Option.Has "only-incomplete" _ -> CompleteRepository.OnlyIncomplete
            | _ -> CompleteRepository.All

        let startDate =
            match input with
            | Input.Option.Value "from" fromDate -> DateTime.Parse $"{fromDate} 00:00"
            | _ -> failwith "Start time is not set."
        let endDate =
            match input with
            | Input.Option.Value "to" toDate -> DateTime.Parse $"{toDate} 23:00"
            | _ -> failwith "End time is not set."

        let ignoredFiles =
            match input with
            | Input.Option.Value "ignore-file" ignored -> ignored |> FileSystem.readLines
            | _ -> []

        let ignoredRepositories =
            match input with
            | Input.Option.Value "ignore-repo" ignored -> ignored |> FileSystem.readLines
            | _ -> []

        paths
        |> run output completeRepository ignoredFiles ignoredRepositories (
            match outputFile with
            | Some file -> Output.File file
            | _ -> Output.Stdout output
        )

        output.Success "Done"
        ExitCode.Success
