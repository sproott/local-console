namespace MF.LocalConsole

[<RequireQualifiedAccess>]
module CognitoStats =
    open System
    open System.Collections.Concurrent
    open System.Text.RegularExpressions
    open System.Net.Mail
    open System.IO
    open FSharp.Data
    open Feather.ConsoleApplication
    open MF.Utils
    open Feather.ErrorHandling

    let args = [
        Argument.required "clients" "Clients input (json file)."
        Argument.required "events" "Events input (json lines file)."
    ]

    let options = []

    let private errorOfError e = e |> sprintf "Error: %A" |> CommandError.Message

    let private cmdErr = ConsoleApplicationError.CommandError

    type private ClientsSchema = JsonProvider<"schema/cognitoClients.json">
    type private EventSchema = JsonProvider<"schema/cognitoEvent.json">

    type [<Struct>] private ClientId = ClientId of string

    [<RequireQualifiedAccess>]
    module private ClientId =
        let value (ClientId id) = id

    type [<Struct>] private Client = {
        ClientId: ClientId
        ClientName: string
        UserPoolId: string
        UserPoolName: string
    }

    type [<Struct>] private Event = {
        ClientId: ClientId
        Timestamp: DateTimeOffset
    }

    type private StateKey = {
        ClientId: ClientId
        Date: DateOnly
    }

    type private StatsItem = {
        Client: Client
        Date: DateOnly
        Count: int
    }

    let private processEvents (output: Feather.ConsoleApplication.Output) events =
        use progress = output.ProgressStart "Processing events" (Seq.length events)

        let state = ConcurrentDictionary<StateKey, int>()
        events
        |> Seq.choose (fun line ->
            try
                let parsed = EventSchema.Parse line

                match parsed.EventName, parsed.AdditionalEventData.RequestParameters.GrantType with
                | "Token_POST", [| "client_credentials" |] ->
                    Some {
                        ClientId = ClientId parsed.AdditionalEventData.ClientId
                        Timestamp = parsed.EventTime
                    }

                | _ -> None
            with e ->
                if output.IsVeryVerbose() then output.Warning e.Message
                None
        )
        |> Seq.iter (fun event ->
            let date = event.Timestamp.Date

            state.AddOrUpdate(
                { ClientId = event.ClientId; Date = DateOnly.FromDateTime date },
                1,
                fun _ count -> count + 1
            )
            |> ignore

            progress.Advance()
        )

        state

    let private formatDate (date: DateOnly) =
        date.ToString("yyyy-MM-dd")

    let private formatCount = function
        | hi when hi >= 100 -> sprintf "<c:red>%d</c>" hi
        | moderate when moderate >= 20 && moderate < 100 -> sprintf "<c:orange>%d</c>" moderate
        | low when low < 20 -> sprintf "<c:green>%d</c>" low
        | _ -> "<c:dark-gray>0</c>"

    let execute = ExecuteAsyncResult <| fun (input, output) -> asyncResult {
        let clientsPath = input |> Input.Argument.asString "clients" |> Option.defaultValue ""
        let events = input |> Input.Argument.asString "events" |> Option.defaultValue ""

        let! clientsContent = File.ReadAllTextAsync clientsPath |> AsyncResult.ofTaskCatch (CommandError.Exception >> ConsoleApplicationError.CommandError)
        let! userPools = try clientsContent |> ClientsSchema.Parse |> Ok with e -> Error (CommandError.Exception e |> ConsoleApplicationError.CommandError)

        let clients =
            userPools
            |> Seq.toList
            |> List.collect (fun pool ->
                pool.Clients
                |> Seq.map (fun client ->
                    {
                        ClientId = ClientId client.ClientId
                        ClientName = client.ClientName
                        UserPoolId = pool.PoolId
                        UserPoolName = pool.PoolName
                    }
                )
                |> Seq.toList
            )

        let events: string seq =
            if File.Exists events then File.ReadLines events
            else Seq.empty

        if output.IsVeryVerbose() then
            output.Table ["clients"; "events" ] [
                [
                    clients |> Seq.length |> string
                    events |> Seq.length |> string
                ]
            ]

        output.SubTitle "Processing events ..."
        let state = processEvents output events
        output.Success "Processed events"

        output.SubTitle "Generating stats ..."
        let stats =
            state
            |> Seq.choose (fun kvp ->
                let clientId = kvp.Key.ClientId
                let date = kvp.Key.Date
                let client = clients |> List.tryFind (_.ClientId >> (=) clientId)

                match client with
                | Some c ->
                    Some {
                        Client = c
                        Date = date
                        Count = kvp.Value
                    }
                | None -> None
            )
            |> Seq.toList
        output.Success "Generated stats"

        let groupedByPool =
            stats
            |> List.groupBy _.Client.UserPoolName

        groupedByPool
        |> List.iter (fun (poolName, items) ->
            output.SubTitle ("<c:dark-yellow>%s</c>", poolName)
            let days = items |> List.map (fun item -> item.Date) |> List.distinct |> List.sort
            let formattedDays = days |> List.map formatDate

            let rows =
                items
                |> List.groupBy (fun item -> item.Client)
                |> List.map (fun (client, clientItems) ->
                    let counts =
                        days
                        |> List.map (fun day ->
                            clientItems
                            |> List.tryFind (fun i -> i.Date = day)
                            |> Option.map (fun i -> i.Count)
                            |> Option.defaultValue 0
                            |> formatCount
                        )
                    [
                        sprintf "<c:cyan>%s</c>" client.ClientName
                        client.ClientId |> ClientId.value |> sprintf "<c:yellow>%s</c>"
                        clientItems |> List.sumBy (fun i -> i.Count) |> formatCount
                    ] @ counts
                )

            output.Table [ "client name"; "client id"; "total"; yield! formattedDays ] rows
        )

        output.SubTitle "Summary"

        let totalEvents = stats |> List.sumBy (fun item -> item.Count)
        groupedByPool
        |> List.map (fun (poolName, items) ->
            [
                sprintf "<c:dark-yellow>%s</c>" poolName
                items |> List.sumBy (fun item -> item.Count) |> formatCount
            ]
        )
        |> output.Table [ "user pool"; $"total events ({totalEvents})" ]

        output.Success "Done"
        return ExitCode.Success
    }
