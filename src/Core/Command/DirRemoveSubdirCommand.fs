namespace MF.LocalConsole

[<RequireQualifiedAccess>]
module DirRemoveSubdirCommand =
    open System.IO
    open Feather.ConsoleApplication
    open MF.Utils
    open Fake.IO
    open Fake.IO.FileSystemOperators
    open Fake.IO.Globbing.Operators

    type Mode =
        | DryRun
        | RemoveDirectories

    // this fixes a subdir in subdir
    // for example:
    //    /path/DIR/foo/node_modules/package/node_modules
    //    /path/DIR/foo/node_modules
    let private fixSubdirInSubdirPath dirsToRemove path =
        let rec fixPath newPath = function
            | [] -> newPath |> List.rev
            | firstDirToRemove :: _ignoredSubdirs when dirsToRemove |> Set.contains firstDirToRemove -> firstDirToRemove :: newPath |> List.rev
            | current :: rest -> rest |> fixPath (current :: newPath)

        path
        |> String.split "/"
        |> fixPath []
        |> String.concat "/"

    let private run (output: Feather.ConsoleApplication.Output) mode (dirsToRemove: string list) dir =
        let dirsToRemoveSet = dirsToRemove |> Set.ofList

        let pathsToDirsToRemove =
            let invalidChars = [ "/"; @"\" ] |> Set.ofList

            dirsToRemove
            |> List.map (function
                | invalid when invalidChars |> Set.contains invalid -> failwithf "Invalid dir %A. Dir name cannot be path." invalid
                | subDir -> sprintf "%s/**/%s" dir subDir
            )

        if output.IsVerbose () then
            output.Section <| sprintf "Find %A in %s to remove ..." pathsToDirsToRemove dir

        let deleteDir dir =
            match mode with
            | DryRun -> dir |> sprintf "<c:gray>rm -rf</c> %s" |> output.Message
            | RemoveDirectories ->
                if output.IsVeryVerbose() then dir |> sprintf "<c:yellow>rm -rf</c> %s" |> output.Message
                dir |> Shell.cleanDir
                if output.IsVeryVerbose() then " -> Done" |> output.Success

        let outputStep name = tee (fun _ -> output.Section name)
        let outputCount list = list |> tee (Seq.length >> sprintf " -> <c:green>Done</c> [<c:magenta>%d</c>]" >> output.Message >> output.NewLine)

        pathsToDirsToRemove
        |> outputStep "Find dirs"
        |> Seq.collect (!!)
        |> outputCount

        |> outputStep "Fix subdir paths"
        |> Seq.sort
        |> Seq.toList
        |> List.map (fixSubdirInSubdirPath dirsToRemoveSet)
        |> List.distinct
        |> outputCount

        |> outputStep "Delete dirs"
        |> List.iter deleteDir

    let execute = Execute <| fun (input, output) ->
        let dir = input |> Input.Argument.value "dir"
        let dirsToRemove = input |> Input.Argument.asList "dirsToRemove"

        let mode =
            match input with
            | Input.Option.Has "dry-run" _ -> DryRun
            | _ -> RemoveDirectories

        dir
        |> run output mode dirsToRemove

        output.Success "Done"
        ExitCode.Success
