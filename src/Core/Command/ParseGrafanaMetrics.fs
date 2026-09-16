namespace MF.LocalConsole

[<RequireQualifiedAccess>]
module ParseGrafanaMetricsCommand =
    open System
    open System.Collections.Concurrent
    open System.Text.RegularExpressions
    open System.Net.Mail
    open System.IO
    open FSharp.Data
    open Feather.ConsoleApplication
    open Feather.ErrorHandling
    open Feather.ErrorHandling.AsyncResult.Operators
    open MF.Utils

    type MertricSchema = JsonProvider<"schema/grafanaMetrics.json", SampleIsList=true>

    let arguments = [
        Argument.required "metrics" "Path to json file with metrics query result."
    ]
    let options = []

    type Frame = {
        Name: string
        Cluster: string
        Container: string
        Job: string
        Namespace: string
        Pod: string
        Prometheus: string
    }

    let private readFile file = asyncResult {
        return! file |> File.ReadAllTextAsync
    }

    let private groupBy f frames =
        frames
        |> List.groupBy f
        |> List.map (fun (ns, items) -> sprintf "%s [%d]" ns items.Length)

    let execute = ExecuteAsyncResult <| fun (input, output) -> asyncResult {
        output.Title "Parse metrics"
        let sectionDone () = output.Message "<c:green> -> Done</c>"

        output.Section "Load file"
        let! file =
            input
            |> Input.Argument.asString "metrics"
            |> Result.ofOption (CommandError.Message "Missing input file" |> ConsoleApplicationError.CommandError)
        sectionDone ()

        output.Section "Load file contents"
        let! contents =
            file
            |> readFile <@> (CommandError.Exception >> ConsoleApplicationError.CommandError)
        sectionDone ()

        output.Section "Parse file content"
        let! (parsedContents: MertricSchema.Root) =
            try
                contents
                |> MertricSchema.Parse
                |> AsyncResult.ofSuccess
            with e ->
                e
                |> CommandError.Exception
                |> ConsoleApplicationError.CommandError
                |> AsyncResult.ofError
        sectionDone ()

        output.Section "Parse frames"
        let! frames =
            parsedContents.Results.E.Frames
            |> Seq.map (fun item ->
                try
                    let fields = item.Schema.Fields
                    let labels =
                        fields
                        |> Seq.tryPick (function
                            | values when values.Name = "Value" -> values.Labels
                            | _ -> None
                        )

                    labels
                    |> Option.map (fun labels ->
                        {
                            Name = labels.Name
                            Cluster = labels.Cluster
                            Container = labels.Container
                            Job = labels.Job
                            Namespace = labels.Namespace
                            Pod = labels.Pod
                            Prometheus = labels.Prometheus
                        }
                    )
                    |> Ok

                with e ->
                    e.Message
                    |> CommandError.Message
                    |> Error
            )
            |> Seq.toList
            |> Validation.ofResults
            |> Result.mapError (CommandError.Errors >> ConsoleApplicationError.CommandError)
        sectionDone ()

        output.Section "Choose metrics"
        let metrics = frames |> List.choose id
        sectionDone ()

        output.Title "Metrics Stats"
        let namespaces =
            metrics
            |> groupBy (fun f -> f.Namespace)
            |> String.concat ", "

        output.Table [ "Total metrics"; $"Namespaces [{namespaces.Length}]" ] [[
            string metrics.Length
            namespaces
        ]]

        let pods =
            metrics
            |> groupBy (fun f -> f.Pod)

        pods
        |> List.map List.singleton
        |> output.Options $"Pods [{pods.Length}]"

        return ExitCode.Success
    }
