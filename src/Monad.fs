namespace MF.Monad

open Feather.ErrorHandling

module Helper =
    let runAsyncResult (ar: AsyncResult<'a, 'e>) =
        async {
            let! result = ar
            match result with
            | Ok success -> printfn "Success: %A" success
            | Error error -> printfn "Error: %A" error
        }

module WriterExample =
    [<RequireQualifiedAccess>]
    module WriterOld =
        open Feather.ConsoleApplication

        type Writer<'A> = Writer of ('A * string)

        type Kleisli<'A, 'B, 'C> = ('A -> Writer<'B>) -> ('B -> Writer<'C>) -> ('A -> Writer<'C>)
        let (>=>): Kleisli<'A, 'B, 'C> =
            fun m1 m2 a ->
                let (Writer (b, s1)) = m1 a
                let (Writer (c, s2)) = m2 b

                Writer (c, s1 + s2)

        type Return<'A> = 'A -> Writer<'A>
        let retn: Return<'A> = fun a -> Writer (a, "")

        type UpCase = string -> Writer<string>
        let upCase: UpCase = fun s -> Writer(s.ToUpper(), "upCase ")

        type ToWords = string -> Writer<string list>
        let toWords: ToWords = fun s -> Writer(s.Split " " |> Seq.toList, "toWords ")

        type Process = string -> Writer<string list>
        let runProcess: Process = upCase >=> toWords

        let example = Execute <| fun (input, output) ->
            let (Writer (words, log)) =
                "Hello World"
                |> runProcess

            output.Messages " - " words
            output.Message <| sprintf "<c:dark-yellow>// %s</c>" log

            ExitCode.Success

    module Writer =
        /// 'a = data
        /// 'e = error
        /// 'w = writer monoid
        type AsyncResultWriter<'a,'e,'w> = Async<Result<'a,'e> * 'w>

        let tell (msg: 'w) : AsyncResultWriter<unit,'e,'w> =
            async { return (Ok (), msg) }

        let ofAsyncResult (m: AsyncResult<'a,'e>) (empty: 'w) : AsyncResultWriter<'a,'e,'w> =
            async {
                let! res = m
                return (res, empty)
            }

        type AsyncResultWriterBuilder<'w> (empty: 'w, append: 'w -> 'w -> 'w) =

            member _.Return(x: 'a) : AsyncResultWriter<'a,'e,'w> =
                async { return (Ok x, empty) }

            member _.ReturnFrom(m: AsyncResultWriter<'a,'e,'w>) = m

            member _.Bind
                (m: AsyncResultWriter<'a,'e,'w>,
                f: 'a -> AsyncResultWriter<'b,'e,'w>) : AsyncResultWriter<'b,'e,'w> =
                async {
                    let! (result, log1) = m
                    match result with
                    | Ok x ->
                        let! (result2, log2) = f x
                        return (result2, append log1 log2)
                    | Error e ->
                        return (Error e, log1)
                }

            member _.Zero() = async { return (Ok (), empty) }

            member this.Combine(m1, m2) = this.Bind(m1, fun () -> m2)

            member _.Delay(f: unit -> AsyncResultWriter<'a,'e,'w>) =
                async.Delay(f)

        let example () =
            let writer = AsyncResultWriterBuilder<string list>([], (@))

            let fakeDbGetUser id : AsyncResult<string,string> =
                async {
                    if id = 42 then return Ok "Lister"
                    else return Error "User not found"
                }

            let workflow userId =
                writer {
                    do! tell ["Starting workflow"]
                    let! name = fakeDbGetUser userId |> (fun m -> ofAsyncResult m [])
                    do! tell [$"Got user: {name}"]
                    return $"Hello, {name}!"
                }

            async {
                let! result, log = workflow 42
                printfn "Result: %A" result
                printfn "Log: %A" log
            }

    module WriterWithMode =
        type WriterMode =
            | HappyOnly
            | ErrorOnly
            | All

        /// 'w = writer monoid
        type WriterLog<'w> = WriterLog of 'w

        type AsyncResultWriter<'a,'e,'w> = Async<Result<'a,'e> * WriterLog<'w>>

        let tell (msg: 'w) : AsyncResultWriter<unit,'e,'w> =
            async { return (Ok (), WriterLog msg) }

        let ofAsyncResult (empty: 'w) (m: AsyncResult<'a,'e>): AsyncResultWriter<'a,'e,'w> =
            async {
                let! res = m
                return (res, WriterLog empty)
            }

        /// 'a = data
        /// 'e = error
        /// 'w = writer monoid
        type AsyncResultWriterBuilder<'w>(mode: WriterMode, empty: 'w, append: 'w -> 'w -> 'w) =

            member _.Return(x) : Async<Result<'a,'e> * WriterLog<'w>> =
                async { return (Ok x, WriterLog empty) }

            member _.Bind(m, f) =
                async {
                    let! (res, WriterLog log1) = m
                    match res with
                    | Ok x ->
                        let! (res2, WriterLog log2) = f x
                        let newLog =
                            match mode with
                            | HappyOnly | All -> append log1 log2
                            | ErrorOnly -> log1   // ignorujeme happy logy

                        return (res2, WriterLog newLog)
                    | Error e ->
                        let newLog =
                            match mode with
                            | ErrorOnly | All -> log1
                            | HappyOnly -> empty

                        return (Error e, WriterLog newLog)
                }

            member _.ReturnFrom(m) = m

        let example () =
            let writer = AsyncResultWriterBuilder<string>(All, "", (+))
            // let listWriter = AsyncResultWriterBuilder<string list>(All, [], (@))

            let computation =
                writer {
                    do! tell "Start"
                    let! x = async { return (Ok 42, WriterLog "") }
                    do! tell "Step done"
                    return x
                }

            async {
                let! result, WriterLog log = computation
                printfn "Result: %A" result
                printfn "Log: %s" log
            }

module ReaderExample =
    //
    // Reader
    //
    type Reader<'env,'a> = Reader of ('env -> 'a)

    module Reader =
        let run (Reader f) env = f env
        let ask = Reader id

    type ReaderBuilder() =
        member _.Return(x) = Reader(fun _ -> x)
        member _.Bind(m: Reader<'env,'a>, f: 'a -> Reader<'env,'b>) =
            Reader(fun env ->
                let (Reader g) = m
                let a = g env
                let (Reader h) = f a
                h env)
        member _.ReturnFrom(m) = m

    let reader = ReaderBuilder()

    //
    // Example usage
    //

    type User = { Id: int; Name: string }

    type IHasDb =
        abstract member GetUser : int -> AsyncResult<User, string>

    type IHasGreeter =
        abstract member GreetUser : User -> AsyncResult<string, string>

    let workflowDirect userId = reader {
        let! (env: #IHasDb & #IHasGreeter) = Reader.ask

        return asyncResult {
            let! user = env.GetUser userId

            return! env.GreetUser user
        }
    }

    let getUser () =
        reader {
            let! (env: #IHasDb) = Reader.ask
            return env.GetUser
        }

    let greetUser () =
        reader {
            let! (env: #IHasGreeter) = Reader.ask
            return env.GreetUser
        }

    let workflow userId = reader {
        let! getUserAsync = getUser ()
        let! greetUser = greetUser ()

        return asyncResult {
            let! user = getUserAsync userId

            return! greetUser user
        }
    }

    // Running workflow

    type Env() =
        interface IHasDb with
            member _.GetUser id =
                async {
                    if id = 42 then
                        return Ok { Id = id; Name = "Lister" }
                    else
                        return Error "User not found"
                }
        interface IHasGreeter with
            member _.GreetUser user =
                async { return Ok $"Hello, {user.Name}!" }

    let runWorkflow workflow userId =
        let env = Env()
        let computation = Reader.run (workflow userId) env

        Helper.runAsyncResult computation

    let example () = asyncResult {
        do! runWorkflow workflowDirect 42
        do! runWorkflow workflow 1
    }

    //
    // Combining records and interfaces
    //

    type GetUser = int -> AsyncResult<User, string>
    type GreetUser = User -> AsyncResult<string, string>

    type GetAndGreetUser = { Get: GetUser; Greet: GreetUser }

    type IGetAndGreetUser =
        abstract member GetAndGreet: GetAndGreetUser

    type Env2(getAndGreet: GetAndGreetUser) =
        interface IGetAndGreetUser with
            member _.GetAndGreet = getAndGreet

    let workflow2 userId = reader {
        let! (env: #IGetAndGreetUser) = Reader.ask
        let { Get = getUser; Greet = greetUser } = env.GetAndGreet

        return asyncResult {
            let! user = getUser userId

            return! greetUser user
        }
    }

    let example2 () =
        let env = Env2 ({
            Get = fun id -> async { return Ok { Id = id; Name = "Lister" } }
            Greet = fun user -> async { return Ok $"Hello, {user.Name}!" }
        })

        let computation = Reader.run (workflow2 42) env

        Helper.runAsyncResult computation

module ReaderWriterExample =
    open ReaderExample
    open WriterExample.WriterWithMode

    type IGetWriter =
        abstract member GetWriter: unit -> AsyncResultWriterBuilder<string>

    let (+//) xR msg = xR |> ofAsyncResult msg

    let greetUserWithWriter userId = reader {
        let! (env: #IGetAndGreetUser & #IGetWriter) = Reader.ask
        let { Get = getUser; Greet = greetUser } = env.GetAndGreet
        let writer = env.GetWriter()

        return writer {
            do! tell "Start"
            let! user = getUser userId |> ofAsyncResult "Getting user ..."
            do! tell "User obtained"

            // with operator
            let! response = greetUser user +// "Greeting user ..."
            do! tell "User greeted"

            return response
        }
    }

    type Env(getAndGreet: GetAndGreetUser, writer) =
        interface IGetAndGreetUser with
            member _.GetAndGreet = getAndGreet

        interface IGetWriter with
            member _.GetWriter() = writer

    let example () =
        let env = Env (
            {
                Get = fun id -> async { return Ok { Id = id; Name = "Lister" } }
                Greet = fun user -> async { return Ok $"Hello, {user.Name}!" }
            },
            AsyncResultWriterBuilder<string>(All, "", sprintf "%s\n|> %s")
        )

        let computation = Reader.run (greetUserWithWriter 42)

        async {
            let! result, WriterLog log = env |> computation

            printfn "Result: %A" result
            printfn "Log: %s" log
        }

module Monad =
    open Feather.ConsoleApplication

    let execute = ExecuteAsync <| fun (input, output) -> async {
        output.Title "Monad Examples"

        output.Section "Reader/Writer Example"
        do! ReaderWriterExample.example()

        return ExitCode.Success
    }