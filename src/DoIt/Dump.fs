namespace MF.DoIt

[<RequireQualifiedAccess>]
module Dump =
    open Feather.ConsoleApplication

    let private findIn<'Item, 'ItemKey when 'ItemKey : equality>
        (items: 'Item list)
        (f: 'Item -> 'ItemKey)
        (key: 'ItemKey) =
        items |> List.find (f >> (=) key)

    let private findByReference<'Item, 'Key, 'Reference when 'Key : equality>
        (items: 'Item list)
        (fi: 'Item -> 'Key)
        (fr: 'Reference -> 'Key)
        (reference: 'Reference) =
        reference
        |> fr
        |> findIn items fi

    let logo (output: Output) =
        let logo =
            [
                ""
                @"   _____          _______  __      ______               __"
                @"  |     \ .-----.|_     _||  |_   |   __ \.---.-..----.|  |--..--.--..-----."
                @"  |  --  ||  _  |  |   |  |   _|  |   __ <|  _  ||  __||    < |  |  ||  _  |"
                @"  | |  | || | | |  |   |  |  |    |______/|___._||____||__|__||_____||   __|"
                @"  |  --  ||  _  | _|   |_ |  |_                                      |__|"
                @"  |_____/ |_____||_______||____|"
            ]

        sprintf "<c:cyan>%s</c>%s<c:gray>Version:</c> <c:magenta>1.0.0</c>\n\n<c:cyan>%s</c>"
            (logo |> String.concat "\n")
            (String.replicate 28 " ")
            (String.replicate 78 "=")
        |> output.Message
        |> output.NewLine

    let private separator (output: Output) () =
        output.NewLine()
        String.replicate 100 "-" |> sprintf "<c:gray>%s</c>" |> output.Message
        output.NewLine()

    let task (output: Output) projects contexts (task: Task) =
        output.Message $"<c:gray>Task</c>[<c:gray>{task.Uuid}</c>]: <c:yellow>{task.Name}</c> (Subtasks: <c:magenta>{task.SubTasks.Length}</c>, Comments: <c:magenta>{task.Comments.Length}</c>)"
        if output.IsVeryVerbose() then
            output.Message $"    -> {task.Focus}"
            task.Description |> Option.iter (sprintf "    -> <c:dark-yellow>%A</c>" >> output.Message)
        if output.IsVerbose() then
            task.Project |> Option.iter (findByReference projects Project.id ProjectReference.id >> Project.format >> sprintf "    -> project: %s" >> output.Message)
            task.Context |> Option.iter (findByReference contexts Context.id ContextReference.id >> Context.format >> sprintf "    -> context: %s" >> output.Message)
            match task.Tags with
            | [] -> ()
            | tags -> output.Message <| sprintf "    -> tags: %s" (tags |> List.map (fun t -> t.Name) |> String.concat ", ")

    let project (output: Output) (project: Project) =
        output.Message $"<c:gray>Project</c>[<c:gray>{project.Uuid}</c>]: <c:yellow>{project.Name}</c>"
        if output.IsVeryVerbose() then
            project.Description |> Option.iter (sprintf "    -> <c:dark-yellow>\"%s\"</c>" >> output.Message)

    let context (output: Output) (context: Context) =
        output.Message $"<c:gray>Context</c>[<c:gray>{context.Uuid}</c>]: <c:yellow>{context.Name}</c>"

    let tag (output: Output) (tag: Tag) =
        output.Message $"<c:gray>Tag</c>[<c:gray>{tag.Uuid}</c>]: <c:yellow>{tag.Name}</c>"

    let backup (output: Output) (backup: DoItBackup) =
        let separator = separator output

        logo output

        output.Table ["Resource"; "Count"] [
            [ "Projects"; backup.Projects |> List.length |> sprintf "<c:magenta>%d</c>" ]
            [ "Contexts"; backup.Contexts |> List.length |> sprintf "<c:magenta>%d</c>" ]
            [ "Tags"; backup.Tags |> List.length |> sprintf "<c:magenta>%d</c>" ]
            [ "Tasks"; backup.Tasks |> List.length |> sprintf "<c:magenta>%d</c>" ]
        ]

        separator()
        output.SubTitle $"Projects [{backup.Projects.Length}]"
        backup.Projects |> List.iter (project output)

        separator()
        output.SubTitle $"Contexts [{backup.Contexts.Length}]"
        backup.Contexts |> List.iter (context output)

        separator()
        output.SubTitle $"Tags [{backup.Tags.Length}]"
        backup.Tags |> List.iter (tag output)

        separator()
        output.SubTitle $"Tasks [{backup.Tasks.Length}]"
        backup.Tasks |> List.iter (task output backup.Projects backup.Contexts)
