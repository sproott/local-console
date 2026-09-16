namespace MF.LocalConsole

[<RequireQualifiedAccess>]
module PhoneCodeStatsCommand =
    open System
    open System.Collections.Concurrent
    open System.IO
    open FSharp.Data
    open Feather.ConsoleApplication

    type PhoneCodes = JsonProvider<"schema/countryCodes.json", SampleIsList=true>

    let private (|Code|_|) codeLength numberLength: (string -> _) = function
        | phone when phone.StartsWith "+" && phone.Length = "+".Length + codeLength + numberLength -> phone.Substring(0, "+".Length + codeLength) |> Some
        | _ -> None

    let private (|PhoneCode|_|) (allowedCodes: string list): (string -> _) = function
        | phone when phone.StartsWith "+" && allowedCodes |> List.exists (phone.StartsWith) ->
            allowedCodes
            |> List.filter phone.StartsWith
            |> List.tryPick (fun code ->
                if phone.Length - code.Length = 9
                    then Some code
                    else None
            )
        | _ -> None

    let execute = Execute <| fun (input, output) ->
        let fileName = input |> Input.Argument.asString "file-name" |> Option.defaultValue "-"
        let outputDir = input |> Input.Option.asString "output"

        output.SubTitle "Starting ..."
        let lines = File.ReadAllLines(fileName) |> Seq.ofArray
        let lineCount = lines |> Seq.length

        let ok = ConcurrentDictionary<string, int>()
        let suspicious = ConcurrentDictionary<string, int>()

        let allowCodes: string list =
            PhoneCodes.GetSamples()
            |> Seq.toList
            |> List.map (fun country ->
                (* let dialCode = country.DialCode

                match dialCode.Number, dialCode.String with
                | _, Some string -> string |> Some
                | Some number, _ -> string number |> Some
                | _ -> None *)

                country.DialCode |> sprintf "+%d"
            )

        let progress = lineCount |> output.ProgressStart "Lines"

        // todo - nacist vsechny kody a pak zkontrolovat, jestli tomu odpovida cislo a ma porad spravnou delku

        lines
        |> Seq.iter (fun line ->
            match line.Split ':' |> Seq.toList with
            | [ _; normalized ] ->
                let normalized = normalized.Trim ' '

                match normalized with
                | PhoneCode allowCodes code -> ok.AddOrUpdate(code, 1, fun _ current -> current + 1) |> ignore
                //| Code 2 9 code -> ok.AddOrUpdate(code, 1, fun _ current -> current + 1) |> ignore
                //| Code 3 9 code -> ok.AddOrUpdate(code, 1, fun _ current -> current + 1) |> ignore
                | differentWithPlus when differentWithPlus.StartsWith "+" -> suspicious.AddOrUpdate(normalized, 1, fun _ current -> current + 1) |> ignore
                | _ -> ()

                progress |> output.ProgressAdvance
            | _ -> ()
        )

        progress |> output.ProgressFinish

        match outputDir with
        | Some outputDir ->
            let writeData name (data: ConcurrentDictionary<_, _>) =
                let write dir name (data: string list) =
                    File.WriteAllLines(sprintf "%s/%s" dir name, data)

                data
                |> Seq.toList
                |> List.map (fun kv -> kv.Key, kv.Value)
                |> List.sortByDescending snd
                |> List.map (fun (k, v) -> sprintf "%s: %d" k v)
                |> write outputDir name

            ok |> writeData "codes-ok.txt"
            suspicious |> writeData "codes-suspicious.txt"
        | _ -> ()

        output.Success "Done"
        ExitCode.Success
