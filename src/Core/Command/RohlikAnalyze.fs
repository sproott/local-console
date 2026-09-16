namespace MF.LocalConsole

[<RequireQualifiedAccess>]
module RohlikAnalyzeCommand =
    open System
    open System.IO
    open Feather.ConsoleApplication
    open MF.Utils
    open Feather.ErrorHandling
    open MF.Rohlik
    open FSharp.Data

    type Arguments = {
        FilePath: string
    }

    let arguments = [
        Argument.optional "file" "Path to the NDJSON file (default: rohlik-products.ndjson)" None
    ]

    let options = [
        Option.noValue "sheets" (Some "s") "Output data in CSV format suitable for Google Sheets copy/paste."
    ]

    // JSON type provider for NDJSON structure
    type private ProductSummaryProvider = JsonProvider<"""{"name":"Rohlik.cz Krůtí prsní šunka nejvyšší jakosti","totalQuantity":39,"orderCount":39,"lastOrderDate":"2025-08-11","averagePrice":61.5602564102564}""">

    let private parseNdJsonFile (filePath: string) =
        try
            let products =
                System.IO.File.ReadLines(filePath)
                |> Seq.map (fun line ->
                    let json = ProductSummaryProvider.Parse(line)
                    {
                        ProductSummary.Name = json.Name
                        TotalQuantity = json.TotalQuantity
                        OrderCount = json.OrderCount
                        LastOrderDate = DateTimeOffset.Parse(json.LastOrderDate.ToString("yyyy-MM-dd"))
                        AveragePrice = json.AveragePrice
                    }
                )
                |> Seq.toList

            Ok products
        with
        | ex -> Error $"Failed to parse NDJSON file: {ex.Message}"

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
                    sprintf "%.2f" p.AveragePrice
                ]
            )

        output.Table headers rows

    let private formatProductSummaryForSheets (productSummary: ProductSummary list) (output: Output) =
        // CSV header
        output.Message "Product Name\tTotal Qty\tOrder Count\tLast Order\tAvg Price"
        
        // CSV data rows
        productSummary
        |> List.iter (fun p ->
            // Use comma as decimal separator and no currency, prefix with quote to prevent auto-conversion
            let avgPrice = sprintf "'%.2f" p.AveragePrice |> fun s -> s.Replace(".", ",")
            let row = sprintf "%s\t%d\t%d\t%s\t%s" 
                        p.Name 
                        p.TotalQuantity 
                        p.OrderCount 
                        (p.LastOrderDate.ToString("yyyy-MM-dd")) 
                        avgPrice
            output.Message row
        )

    let execute = ExecuteAsyncResult <| fun (input, output) -> asyncResult {
        let! arguments = asyncResult {
            let filePath =
                input
                |> Input.Argument.asString "file"
                |> Option.defaultValue "rohlik-products.ndjson"

            return {
                FilePath = filePath
            }
        }

        output.Message $"Loading Rohlik product summary from file: {arguments.FilePath}..."

        let! (productSummary: ProductSummary list) =
            parseNdJsonFile arguments.FilePath
            |> Result.mapError (CommandError.Message >> ConsoleApplicationError.CommandError)
            |> AsyncResult.ofResult

        output.Message $"Found {productSummary.Length} products in the file:"
        output.NewLine()

        // Check if sheets option is provided
        let useSheetFormat =
            match input with
            | Input.Option.Has "sheets" _ -> true
            | _ -> false
        
        if useSheetFormat then
            output.Message "Data in CSV format (copy/paste to Google Sheets):"
            output.Message "Instructions: Select the output below, copy it, then paste into Google Sheets."
            output.Message "The single quote (') prefix on prices prevents auto-conversion to dates."
            output.NewLine()
            productSummary |> formatProductSummaryForSheets <| output
        else
            productSummary |> formatProductSummary <| output

        output.NewLine()
        output.Success $"Analysis complete! Displayed {productSummary.Length} products from {arguments.FilePath}."

        return ExitCode.Success
    }
