namespace MF.LocalConsole

[<RequireQualifiedAccess>]
module ContactStatsCommand =
    open System
    open System.Collections.Concurrent
    open System.IO
    open FSharp.Data
    open Feather.ConsoleApplication

    let execute = Execute <| fun (input, output) ->
        let fileName = input |> Input.Argument.asString "file-name" |> Option.defaultValue "-"

        output.SubTitle "Starting ..."
        let lines = File.ReadAllLines(fileName) |> Seq.ofArray
        let lineCount = lines |> Seq.length

        let stats = ConcurrentDictionary<string, string list>()

        let add (original, normalized) =
            stats.AddOrUpdate(normalized, [original], fun _ current -> original :: current |> List.distinct)
            |> ignore

        let progress = lineCount |> output.ProgressStart "Lines"

        lines
        |> Seq.iter (fun line ->
            match line.Split(':', 2) |> Seq.toList with
            | [ original; normalized ] ->
                let original = original.Trim ' '
                let normalized = normalized.Trim ' '

                add (original, normalized)

                progress |> output.ProgressAdvance
            | _ -> ()
        )

        progress |> output.ProgressFinish

        output.NewLine()
        output.Table [ "Original count"; "Normalized count"; "Total"; "Percentage" ] [
            [
                lineCount |> string
                stats.Count |> string
                lineCount - stats.Count |> string
                stats.Count / lineCount * 100 |> float |> Math.Round |> int |> sprintf "%d%%"
            ]
        ]

        output.NewLine()

        let normalizedContacts =
            stats
            |> Seq.map (fun kv -> kv.Key, kv.Value)
            |> Seq.toList
            |> List.filter (fun (_, originals) -> (originals |> List.length) > 1 (* && originals |> List.forall (fun o -> o.Contains "%40" |> not) *))  // jine nez, ty ktere maji %40 a pak @
            |> List.sortByDescending (snd >> List.length)

        normalizedContacts
        |> List.take (min 20 normalizedContacts.Length)
        |> List.map (fun (normalized, originals) -> [ normalized |> sprintf "<c:cyan>%s</c>" ; sprintf "[ %s ]" (originals |> List.map (sprintf "<c:yellow>%s</c>") |> String.concat ", ")])
        |> output.Options (sprintf "Optimized contacts [%d]:" (normalizedContacts |> List.length))

        output.Success "Done"
        ExitCode.Success
