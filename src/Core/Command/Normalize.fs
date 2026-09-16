namespace MF.LocalConsole

[<RequireQualifiedAccess>]
module NormalizeCommand =
    open System
    open System.Collections.Concurrent
    open System.Text.RegularExpressions
    open System.Net.Mail
    open System.IO
    open FSharp.Data
    open Feather.ConsoleApplication
    open MF.Utils

    type NormalizationResponse = JsonProvider<"schema/response.json", SampleIsList=true>

    let private allowedPhoneCodes = [
        "+420"  // czech republic must be first, so it would be used for code-less numbers

        "+93"; "+358"; "+355"; "+213"; "+1684"; "+376"; "+244"; "+1264"; "+672"; "+1268"; "+54"
        "+374"; "+297"; "+61"; "+43"; "+994"; "+1242"; "+973"; "+880"; "+1246"; "+375"; "+32"
        "+501"; "+229"; "+1441"; "+975"; "+591"; "+387"; "+267"; "+47"; "+55"; "+246"; "+673"
        "+359"; "+226"; "+257"; "+855"; "+237"; "+1"; "+238"; "+345"; "+236"; "+235"; "+56"
        "+86"; "+61"; "+61"; "+57"; "+269"; "+242"; "+243"; "+682"; "+506"; "+225"; "+385"
        "+53"; "+357"; "+45"; "+253"; "+1767"; "+1849"; "+593"; "+20"; "+503"; "+240"
        "+291"; "+372"; "+251"; "+500"; "+298"; "+679"; "+358"; "+33"; "+594"; "+689"; "+262"
        "+241"; "+220"; "+995"; "+49"; "+233"; "+350"; "+30"; "+299"; "+1473"; "+590"
        "+1671"; "+502"; "+44"; "+224"; "+245"; "+592"; "+509"; "+0"; "+379"; "+504"
        "+852"; "+36"; "+354"; "+91"; "+62"; "+98"; "+964"; "+353"; "+44"; "+972"
        "+39"; "+1876"; "+81"; "+44"; "+962"; "+7"; "+254"; "+686"; "+850"; "+82"
        "+383"; "+965"; "+996"; "+856"; "+371"; "+961"; "+266"; "+231"; "+218"; "+423"
        "+370"; "+352"; "+853"; "+389"; "+261"; "+265"; "+60"; "+960"; "+223"; "+356"
        "+692"; "+596"; "+222"; "+230"; "+262"; "+52"; "+691"; "+373"; "+377"; "+976"
        "+382"; "+1664"; "+212"; "+258"; "+95"; "+264"; "+674"; "+977"; "+31"; "+599"
        "+687"; "+64"; "+505"; "+227"; "+234"; "+683"; "+672"; "+1670"; "+47"; "+968"; "+92"; "+680"
        "+970"; "+507"; "+675"; "+595"; "+51"; "+63"; "+64"; "+48"; "+351"; "+1939"
        "+974"; "+40"; "+7"; "+250"; "+262"; "+590"; "+290"; "+1869"; "+1758"; "+590"
        "+508"; "+1784"; "+685"; "+378"; "+239"; "+966"; "+221"; "+381"; "+248"; "+232"
        "+65"; "+421"; "+386"; "+677"; "+252"; "+27"; "+211"; "+500"; "+34"; "+94"
        "+249"; "+597"; "+47"; "+268"; "+46"; "+41"; "+963"; "+886"; "+992"; "+255"
        "+66"; "+670"; "+228"; "+690"; "+676"; "+1868"; "+216"; "+90"; "+993"; "+1649"
        "+688"; "+256"; "+380"; "+971"; "+44"; "+1"; "+598"; "+998"; "+678"; "+58"
        "+84"; "+1284"; "+1340"; "+681"; "+967"; "+260"; "+263"
    ]

    type private ResultCollection = ConcurrentDictionary<string, string>
    type private ResultsCollections = {
        Ok: ResultCollection
        Invalid: ResultCollection
        Suspicious: ResultCollection
        Errors: ResultCollection
    }

    let private clearOutputFile outputDir fileName =
        outputDir
        |> Option.iter (fun dir ->
            let path = sprintf "%s/%s.txt" dir fileName

            if File.Exists path then
                path |> File.Delete
        )

    let private clearOutputDir outputDir =
        outputDir
        |> Option.iter (fun dir ->
            [
                "ok"; "suspicious"; "invalid"; "errors"
            ]
            |> List.iter (fun fileName ->
                let path = sprintf "%s/%s.txt" dir fileName

                if File.Exists path then
                    path |> File.Delete
            )
        )

    let private createBody inputType (value: string) =
        match inputType with
        | "phone" -> "null", (value |> sprintf "%A")
        | "email" -> (value |> sprintf "%A"), "null"
        | invalid -> failwithf "Invalid type %A" invalid

        |> fun (email, phone) ->
            sprintf
                """{
                    "data": {
                        "type": "normalization",
                        "attributes": {
                            "contact": {
                                "email": %s,
                                "phone": %s
                            }
                        }
                    }
                }"""
                email
                phone
            |> TextRequest

    let private normalizeLine (output: Feather.ConsoleApplication.Output) baseUrl inputType progress results (line: string) = async {
        let line = line.Trim '"'

        let! rawResponse =
            Http.AsyncRequestString
                (
                    baseUrl,
                    httpMethod = "POST",
                    headers = [
                        HttpRequestHeaders.Accept "application/vnd.api+json"
                        HttpRequestHeaders.ContentType "application/vnd.api+json"
                    ],
                    body = (line |> createBody inputType)
                )
            |> Async.Catch

        progress |> output.ProgressAdvance

        return
            match rawResponse with
            | Choice1Of2 response ->
                try
                    let parsed = response |> NormalizationResponse.Parse

                    let email = parsed.Data.Attributes.Contact.Email
                    let phone = parsed.Data.Attributes.Contact.Phone

                    match inputType, email, phone with
                    | "phone", _, Some phone ->
                        match allowedPhoneCodes |> List.tryFind phone.StartsWith with
                        | Some code when phone.Length - code.Length >= 8 && phone.Length - code.Length <= 11 ->
                            try
                                let phoneNumberUtil = PhoneNumbers.PhoneNumberUtil.GetInstance()
                                let parsedPhone = phoneNumberUtil.Parse(phone, null)

                                (*
                                    todo - to use this, disable progress and change Async.Parallel to Async.Sequential
                                    output.Table [ "original"; "normalized"; "p.code"; "p.nationalNumber" ] [
                                    [
                                        line
                                        phone
                                        parsedPhone.CountryCode |> string
                                        parsedPhone.NationalNumber |> string
                                    ]
                                ] *)

                                if parsedPhone.IsInitialized
                                    then
                                        results.Ok.AddOrUpdate(line, phone, (fun _ _ -> phone)) |> ignore
                                        [ string line; "<c:green>Ok</c>" ]
                                    else
                                        let response = "Is not initialized."
                                        results.Invalid.AddOrUpdate(line, response, (fun _ _ -> response)) |> ignore
                                        [ string line; "<c:yellow>Invalid</c>" ]
                            with
                            | e ->
                                let response = e.Message
                                results.Invalid.AddOrUpdate(line, response, (fun _ _ -> response)) |> ignore
                                [ string line; "<c:yellow>Invalid</c>" ]
                        | _ ->
                            results.Suspicious.AddOrUpdate(line, phone, (fun _ _ -> phone)) |> ignore
                            [ string line; "<c:yellow>Suspicious</c>" ]

                    | "email", Some email, _ ->
                        if email.Length > "a@b.cz".Length && email.Contains "@" && email.Contains "."
                            then
                                try
                                    let emailAddress = email |> MailAddress

                                    results.Ok.AddOrUpdate(line, emailAddress.Address, (fun _ _ -> emailAddress.Address)) |> ignore
                                    [ string line; "<c:green>Ok</c>" ]
                                with
                                | e ->
                                    let response = e.Message
                                    results.Invalid.AddOrUpdate(line, response, (fun _ _ -> response)) |> ignore
                                    [ string line; "<c:yellow>Invalid</c>" ]
                            else
                                results.Suspicious.AddOrUpdate(line, email, (fun _ _ -> email)) |> ignore
                                [ string line; "<c:yellow>Suspicious</c>" ]

                    | _ ->
                        let response = sprintf "Error (missing contact): %A" line
                        results.Errors.AddOrUpdate(line, response, (fun _ _ -> response)) |> ignore

                        [ string line; sprintf "<c:red>%A</c>" response ]
                with
                | e ->
                    let response = sprintf "Error (parse response): %A" e.Message
                    results.Errors.AddOrUpdate(line, response, (fun _ _ -> response)) |> ignore

                    [ string line; sprintf "<c:red>%A</c>" response ]

            | Choice2Of2 e ->
                let response = sprintf "Error: %A" e.Message
                results.Errors.AddOrUpdate(line, response, (fun _ _ -> response)) |> ignore

                [ string line; "<c:red>Error</c>" ]
    }

    let private debugResults (output: Feather.ConsoleApplication.Output) results =
        output.Title "Real responses"

        output.SubTitle "Ok"
        results.Ok
        |> Seq.toList
        |> List.map (fun kv -> [kv.Key |> string; kv.Value])
        |> output.Table [ "Request"; "Response" ]

        output.SubTitle "Suspicious"
        results.Suspicious
        |> Seq.toList
        |> List.map (fun kv -> [kv.Key |> string; kv.Value])
        |> output.Table [ "Request"; "Response" ]

        output.SubTitle "Errors"
        results.Errors
        |> Seq.toList
        |> List.map (fun kv -> [kv.Key |> string; kv.Value])
        |> output.Table [ "Request"; "Error Response" ]

    let private normalizeLinesByChunks output baseUrl inputType progress (errors: ResultCollection) outputDir chunkSize lines =
        let results = {
            Ok = ResultCollection()
            Invalid = ResultCollection()
            Suspicious = ResultCollection()
            Errors = ResultCollection()
        }

        lines
        |> Seq.chunkBySize chunkSize
        |> Seq.iter (fun linesChunk ->
            linesChunk
            |> Seq.map (normalizeLine output baseUrl inputType progress results)
            |> Async.Parallel
            |> Async.RunSynchronously
            //|> tee (fun _ -> progress |> output.ProgressFinish; output.NewLine())
            |> Seq.toList
            |> tee (fun lines ->
                if output.IsVeryVerbose() then
                    output.NewLine()

                    lines
                    |> output.Table [ "Request"; "Response" ]
            )
            |> ignore

            if output.IsDebug() then results |> debugResults output

            match outputDir with
            | Some outputDir ->
                let writeData name (data: ConcurrentDictionary<_, _>) =
                    let write dir name (data: string list) =
                        File.AppendAllLines(sprintf "%s/%s" dir name, data)

                    data
                    |> Seq.toList
                    |> List.map (fun kv -> sprintf "%s: %s" kv.Key kv.Value)
                    |> write outputDir name

                results.Ok |> writeData "ok.txt"
                results.Invalid |> writeData "invalid.txt"
                results.Suspicious |> writeData "suspicious.txt"
                results.Errors |> writeData "errors.txt"
            | _ -> ()

            results.Errors
            |> Seq.iter (fun kvPair ->
                errors.AddOrUpdate(kvPair.Key, kvPair.Value, (fun _ _ -> kvPair.Value)) |> ignore
            )

            results.Ok.Clear()
            results.Invalid.Clear()
            results.Suspicious.Clear()
            results.Errors.Clear()
        )

    let private normalizeLines (output: Feather.ConsoleApplication.Output) baseUrl inputType outputDir lines =
        let errors = ResultCollection()
        let lineCount = lines |> Seq.length
        let progress = lineCount |> output.ProgressStart "Function calls"

        lines
        |> normalizeLinesByChunks output baseUrl inputType progress errors outputDir 500

        progress |> output.ProgressFinish
        output.NewLine()

        errors

    let execute = Execute <| fun (input, output) ->
        let fileName = input |> Input.Argument.asString "file-name" |> Option.defaultValue "-"
        let inputType = input |> Input.Argument.asString "input-type" |> Option.defaultValue ""

        let appName = "fun-prod-web"
        let functionName = "HttpTrigger"
        let code = input |> Input.Option.asString "code" |> Option.defaultValue "-"
        let outputDir = input |> Input.Option.asString "output"
        let baseUrl = sprintf "https://%s.azurewebsites.net/api/%s?code=%s" appName functionName code

        output.Table ["app"; "func"; "code"] [
            [ fileName; functionName; code ]
        ]

        output.SubTitle "Normalize - Starting ..."
        let lines = File.ReadAllLines(fileName) |> Seq.ofArray

        outputDir |> clearOutputDir

        let errors =
            lines
            |> normalizeLines output baseUrl inputType outputDir

        if not errors.IsEmpty then
            output.SubTitle <| sprintf "Normalize errors again [%A] - Starting ..." errors.Count

            "errors" |> clearOutputFile outputDir

            errors.Keys
            |> normalizeLines output baseUrl inputType outputDir
            |> ignore

        output.Success "Done"
        ExitCode.Success

    let executePhone = Execute <| fun (input, output) ->
        let phone = input |> Input.Argument.asString "phone" |> Option.defaultValue ""

        let tryParsePhone code phone =
            let phoneNumberUtil = PhoneNumbers.PhoneNumberUtil.GetInstance()
            phoneNumberUtil.Parse(phone, code)

        try
            let parsedPhone = tryParsePhone null phone

            output.Table [ "original"; "p.code"; "p.nationalNumber" ] [
                [
                    phone
                    parsedPhone.CountryCode |> string
                    parsedPhone.NationalNumber |> string
                ]
            ]
        with
        | e ->
            try
                let parsedPhone = tryParsePhone "CZ" phone

                output.Table [ "original"; "p.code"; "p.nationalNumber" ] [
                    [
                        phone
                        parsedPhone.CountryCode |> string
                        parsedPhone.NationalNumber |> string
                    ]
                ]

            with
            | _ ->
                e.Message
                |> sprintf "Phone %A is not valid: %A" phone
                |> output.Error

        ExitCode.Success
