namespace MF.LocalConsole

[<RequireQualifiedAccess>]
module StreamTestCommand =
    open System
    open System.IO
    open System.IO.Pipelines
    open System.Text
    open System.Threading.Tasks

    open Feather.ConsoleApplication
    open MF.Utils
    open Feather.ErrorHandling

    [<RequireQualifiedAccess>]
    module StreamFile =
        let testWithOwnBuffer fileName = task {
            use fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read)
            let buffer = Array.zeroCreate<byte> 64

            printfn "Starting ..."
            let mutable readFile = true
            let mutable threshold = 10

            while readFile do
                threshold <- threshold - 1
                if threshold = 0 then
                    printfn "Threshold reached!"
                    readFile <- false

                let! read = fileStream.ReadAsync(buffer, 0, buffer.Length)
                if read = 0 then
                    printfn "End of file!"
                    readFile <- false
                else
                    let content = Encoding.UTF8.GetString(buffer, 0, read)
                    printfn "Read[%d]:\n%s\n" read content

            printfn "Finished!"
        }

        let testWithBufferedStream fileName = task {
            use fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read)
            use fileBufferStream = new BufferedStream(fileStream, 32)

            if not fileBufferStream.CanRead then printfn "Stream is not readable!"
            else

            let buffer = Array.zeroCreate<byte> 64

            printfn "Starting ..."
            let mutable readFile = true
            let mutable threshold = 10

            while readFile do
                threshold <- threshold - 1
                if threshold = 0 then
                    printfn "Threshold reached!"
                    readFile <- false

                printfn "Waiting ..."
                do! Task.Delay 1000

                let! read = fileBufferStream.ReadAsync(buffer, 0, buffer.Length)
                if read = 0 then
                    printfn "End of file!"
                    readFile <- false
                else
                    let content = Encoding.UTF8.GetString(buffer, 0, read)
                    printfn "Read[%d]:\n%s\n" read content

            printfn "Finished!"
        }

        let testWithBufferedStreamReader fileName = task {
            use fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read)
            use fileBufferStream = new BufferedStream(fileStream, 32)

            if not fileBufferStream.CanRead then printfn "Stream is not readable!"
            else

            use reader = new StreamReader(fileBufferStream)

            printfn "Starting ..."
            let mutable readFile = true
            let mutable threshold = 10

            while readFile do
                threshold <- threshold - 1
                if threshold = 0 then
                    printfn "Threshold reached!"
                    readFile <- false

                printfn "Waiting ..."
                do! Task.Delay 1000

                match! reader.ReadLineAsync() with
                | null ->
                    printfn "End of file!"
                    readFile <- false
                | line ->
                    printfn "Read:\n%s\n" line

            printfn "Finished!"
        }

        let testWithBufferedStreamSeq fileName: string seq = seq {
            use fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read)
            use fileBufferStream = new BufferedStream(fileStream, 32)

            if not fileBufferStream.CanRead then
                printfn "Stream is not readable!"

            else
                let buffer = Array.zeroCreate<byte> 64

                printfn "Starting seq ..."
                let mutable readFile = true
                let mutable threshold = 10

                while readFile do
                    threshold <- threshold - 1
                    if threshold = 0 then
                        printfn "Threshold reached!"
                        readFile <- false

                    let read = fileBufferStream.Read(buffer, 0, buffer.Length)
                    if read = 0 then
                        printfn "End of file!"
                        readFile <- false
                    else
                        yield Encoding.UTF8.GetString(buffer, 0, read)

                printfn "Finished seq!"
        }

        let testWithBufferedStreamSeqReader fileName: string seq = seq {
            use fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read)
            use fileBufferStream = new BufferedStream(fileStream, 32)

            if not fileBufferStream.CanRead then
                printfn "Stream is not readable!"
            else
                use reader = new StreamReader(fileBufferStream)

                printfn "Starting seq ..."
                let mutable readFile = true
                let mutable threshold = 10

                while readFile do
                    threshold <- threshold - 1
                    if threshold = 0 then
                        printfn "Threshold reached!"
                        readFile <- false

                    match reader.ReadLine() with
                    | null ->
                        printfn "End of file!"
                        readFile <- false
                    | line ->
                        yield line

                printfn "Finished seq!"
        }

        let readStream (stream: Stream) = task {
            if not stream.CanRead then printfn "Stream is not readable!"
            else

            use reader = new StreamReader(stream)

            printfn "Starting ..."
            let mutable readFile = true
            let mutable threshold = 10

            while readFile do
                //printfn "Waiting ..."
                do! Task.Delay 1000

                match! reader.ReadLineAsync() with
                | null ->
                    printfn "End of file!"
                    readFile <- false
                | line ->
                    printfn "Line: %s" line

                threshold <- threshold - 1
                if threshold = 0 then
                    printfn "Threshold reached!"
                    readFile <- false

            printfn "[ReadStream] Finished!"
        }

        // It works, yet it reads all file contents in CopyToAsync - which took a lot of time for big files.
        let testCombine files = task {
            let readFile file =
                printfn "[ReadFile] Reading file: %s" file
                new BufferedStream(new FileStream(file, FileMode.Open, FileAccess.Read), 1024) :> Stream

            let combine (streams: Stream list) = task {
                let pipe = new Pipe()
                let writer = pipe.Writer

                printfn "[Combine] Starting ..."
                for stream in streams do
                    do! stream.CopyToAsync(writer)

                printfn "[Combine] All streams written."

                do! writer.CompleteAsync()
                printfn "[Combine] Finished!"

                return new BufferedStream(pipe.Reader.AsStream(), 1024)
            }

            let! content =
                files
                |> List.map readFile
                |> combine

            do! readStream (content :> Stream)

            printfn "Finished!"
        }

        let testCombineLazy files = task {
            let bufferSize = 4096

            let readFile file =
                printfn "[ReadFile] Reading file: %s" file
                new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true) :> Stream

            let combine (streams: Stream list) = task {
                let pipe = new Pipe()
                let writer = pipe.Writer

                printfn "[Combine] Starting lazy streaming ..."
                (* for stream in streams do
                    do! stream.CopyToAsync(writer) *)

                let! _ =
                    streams
                    |> List.map (fun stream -> task {
                        let buffer = Array.zeroCreate<byte> bufferSize
                        let mutable bytesRead = 0

                        while (bytesRead <- stream.Read(buffer, 0, bufferSize); bytesRead > 0) do
                            let! _ = writer.WriteAsync(ReadOnlyMemory(buffer, 0, bytesRead))
                            let! _ = writer.FlushAsync()
                            ()

                        stream.Dispose()
                    })
                    |> Task.WhenAll

                printfn "[Combine] All streams written."

                do! writer.CompleteAsync()
                printfn "[Combine] Finished!"

                //return new BufferedStream(pipe.Reader.AsStream(), 1024)
                return pipe.Reader.AsStream()
            }

            let! content =
                files
                |> List.map readFile
                |> combine

            do! readStream content

            printfn "Finished!"
        }

    let execute = ExecuteAsyncResult <| fun (input, output) -> asyncResult {
        output.Title "Stream playground"

        let tasks =
            match input |> Input.Argument.asList "file-name" with
            | [] -> []
            | [ fileName ] ->
                [
                    fileName |> StreamFile.testWithBufferedStreamReader
                    task { fileName |> StreamFile.testWithBufferedStreamSeqReader |> Seq.iter (output.Message) |> output.NewLine }
                ]
            | files ->
                [
                    //files |> StreamFile.testCombine
                    files |> StreamFile.testCombineLazy
                ]

        do!
            tasks
            |> List.map (AsyncResult.ofTaskCatch id)
            |> AsyncResult.ofSequentialAsyncResults id
            |> AsyncResult.ignore
            |> AsyncResult.mapError (List.map CommandError.Exception >> CommandError.Errors >> ConsoleApplicationError.CommandError)

        output.Success "Done"
        return ExitCode.Success
    }
