namespace MF.LocalConsole

[<RequireQualifiedAccess>]
module RohlikProductsCommand =
    open System
    open System.IO
    open Feather.ConsoleApplication
    open MF.Utils
    open Feather.ErrorHandling
    open MF.Rohlik

    type Arguments = {
        CredentialsPath: string
        OrderLimit: int
    }

    let arguments = [
        Argument.required "credentials-path" "Path to JSON file with Rohlik credentials (format: {\"username\":\"email\", \"password\":\"password or leave empty\"})"
    ]

    let options = [
        Option.optional "order-limit" (Some "l") "Number of recent orders to analyze." (Some "50")
        Option.optional "password" (Some "p") "Password for Rohlik account (overrides password from credentials file)." None
        Option.optional "output" (Some "o") "Output file path for product summary (NDJSON format)." None
    ]

    [<RequireQualifiedAccess>]
    module Credentials =
        open Newtonsoft.Json
        open Newtonsoft.Json.Linq

        type CredentialsJson = {
            Username: string
            Password: string option
        }

        let parse (filePath: string) =
            result {
                let! content =
                    if File.Exists filePath then
                        File.ReadAllText filePath |> Ok
                    else
                        Error $"Credentials file not found: {filePath}"

                let! credentialsJson =
                    try
                        // First try to parse with optional password
                        JsonConvert.DeserializeObject<CredentialsJson>(content) |> Ok
                    with ex ->
                        try
                            // Fallback: try parsing with required password (legacy format)
                            let legacyCredentials = JsonConvert.DeserializeObject<{| Username: string; Password: string |}>(content)
                            Ok { Username = legacyCredentials.Username; Password = Some legacyCredentials.Password }
                        with _ ->
                            Error $"Failed to parse credentials JSON: {ex.Message}"

                return credentialsJson
            }

    let private formatProductSummary (productSummary: ProductSummary list) (output: Output) =
        let headers = ["Product Name"; "Total Qty"; "Order Count"; "Last Order"; "Avg Price"]

        let rows =
            productSummary
            |> List.map (fun p ->
                [
                    p.Name
                    string p.TotalQuantity
                    string p.OrderCount
                    p.LastOrderDate.ToString("yyyy-MM-dd")
                    sprintf "%.2f CZK" p.AveragePrice
                ]
            )

        output.Table headers rows

    let private saveProductSummaryAsNdJson (productSummary: ProductSummary list) (filePath: string) =
        try
            let jsonLines =
                productSummary
                |> List.map (fun p ->
                    let jsonObj = Newtonsoft.Json.Linq.JObject()
                    jsonObj.["name"] <- p.Name
                    jsonObj.["totalQuantity"] <- p.TotalQuantity
                    jsonObj.["orderCount"] <- p.OrderCount
                    jsonObj.["lastOrderDate"] <- p.LastOrderDate.ToString("yyyy-MM-dd")
                    jsonObj.["averagePrice"] <- p.AveragePrice
                    jsonObj.ToString(Newtonsoft.Json.Formatting.None)
                )

            System.IO.File.WriteAllLines(filePath, jsonLines)
            Ok ()
        with ex ->
            Error ex.Message

    let execute = ExecuteAsyncResult <| fun (input, output) -> asyncResult {
        let arguments = {
            CredentialsPath = input |> Input.Argument.asString "credentials-path" |> Option.defaultValue ""
            OrderLimit =
                match input with
                | Input.Option.OptionalValue "order-limit" value ->
                    match System.Int32.TryParse(value) with
                    | true, number -> number
                    | false, _ -> 50
                | _ -> 50
        }

        if output.IsDebug() then Api.enableDebug()
        elif output.IsVerbose() then Api.enableInfo()

        do!
            if String.IsNullOrEmpty arguments.CredentialsPath then
                "Credentials path is required"
                |> CommandError.Message
                |> ConsoleApplicationError.CommandError
                |> AsyncResult.ofError
            else
                AsyncResult.ofSuccess ()

        output.Message $"Loading Rohlik product summary from last {arguments.OrderLimit} orders..."

        let! (credentialsJson: Credentials.CredentialsJson) =
            arguments.CredentialsPath
            |> Credentials.parse
            |> Result.mapError (CommandError.Message >> ConsoleApplicationError.CommandError)
            |> AsyncResult.ofResult

        let directPassword =
            input
            |> Input.Option.asString "password"

        let! password =
            directPassword
            |> Option.orElse credentialsJson.Password
            |> Result.ofOption (CommandError.Message "Password is required" |> ConsoleApplicationError.CommandError)

        let credentials = {
            Username = credentialsJson.Username
            Password = password
        }

        let! (productSummary : ProductSummary list) =
            Api.getOrderHistoryProductSummary credentials arguments.OrderLimit
            |> AsyncResult.mapError (fun ex ->
                CommandError.Exception ex |> ConsoleApplicationError.CommandError)

        output.Message $"Found {productSummary.Length} unique products in your order history:"
        output.NewLine()

        productSummary |> formatProductSummary <| output

        // Save to NDJSON file if output option is provided
        let outputFilePath = input |> Input.Option.asString "output"

        do!
            match outputFilePath with
            | Some filePath ->
                saveProductSummaryAsNdJson productSummary filePath
                |> Result.mapError (sprintf "Failed to save to file: %s" >> CommandError.Message >> ConsoleApplicationError.CommandError)
                |> AsyncResult.ofResult
                |> AsyncResult.tee (fun _ -> output.Success $"Product summary saved to: {filePath}")
            | None ->
                AsyncResult.ofSuccess ()

        output.NewLine()
        output.Success $"Analysis complete! Analyzed {productSummary.Length} unique products from your last {arguments.OrderLimit} orders."

        return ExitCode.Success
    }
