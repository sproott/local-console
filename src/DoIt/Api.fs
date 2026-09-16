namespace MF.DoIt

open System
open System.IO
open System.Net
open FSharp.Data
open FSharp.Data.HttpRequestHeaders
open MF.Utils
open Feather.ErrorHandling
open MF.DoIt

type Credentials = {
    Autologin: string
    SessionId: string
}

[<RequireQualifiedAccess>]
module Credentials =
    type private CredentialsSchema = JsonProvider<"../../.doit.dist.json">

    let parse file =
        try
            let data = file |> File.ReadAllText |> CredentialsSchema.Parse
            Ok {
                Autologin = data.Autologin
                SessionId = data.ConnectSid
            }
        with e -> Error e

    let private data credentials =
        [
            "autologin", credentials.Autologin
            "connect.sid", credentials.SessionId
        ]

    let internal asCookies credentials =
        credentials
        |> data
        |> List.fold (fun (cc: CookieContainer) (key, value) ->
            let cookie =
                Cookie(
                    name = key,
                    value = value,
                    path = "/",
                    domain = "i.doit.im"
                )

            cc.Add(cookie)
            cc
        ) (CookieContainer())

[<RequireQualifiedAccess>]
module Api =
    let private debugMode = false
    let private teeDebug f a =
        if debugMode then f a
        a

    type LoadedTasks = LoadedTasks of Uuid list

    type private ResourcesResponse = JsonProvider<"schema/resources.json", SampleIsList=true>
    type private TaskResponse = JsonProvider<"schema/task.json", SampleIsList=true>
    type private SubTasksResponse = JsonProvider<"schema/subtasks.json", SampleIsList=true>
    type private CommentsResponse = JsonProvider<"schema/comments.json", SampleIsList=true>

    type private GenericTaskListResponse = JsonProvider<"schema/genericTaskList.json", SampleIsList=true>

    let private api path (parse: string -> 'Response) credentials =
        asyncResult {
            let url = sprintf "https://i.doit.im/api/%s?_=%d" path (DateTimeOffset.Now.ToUnixTimeMilliseconds())

            let! response =
                Http.AsyncRequestString (
                    url,
                    httpMethod = "GET",
                    headers = (
                        [
                            Accept "application/json"
                            AcceptLanguage "cs,en;q=0.9,sk;q=0.8,und;q=0.7"
                            CacheControl "no-cache"
                            Host "i.doit.im"
                        ]
                    ),
                    cookieContainer = (credentials |> Credentials.asCookies)
                )
                |> AsyncResult.ofAsyncCatch id

            try return response |> parse
            with e -> return! AsyncResult.ofError e
        }
        // |> AsyncResult.teeError (function
        //     | Http.NotFound -> ()
        //     | e -> eprintfn "[Api] %s error %A" path e
        // )

    type private TaskItem = {
        Uuid: Uuid
        Name: string
    }

    let private parseGenericList (LoadedTasks loadedTasks) response =
        let data = response |> GenericTaskListResponse.Parse

        data.Entities
        |> teeDebug (Seq.length >> (fun entities -> $"[Api] Parse generic list of entities [{entities}] (excluding {loadedTasks.Length}) ..." |> printfn "%s"))
        |> Seq.choose (fun task ->
            let id = Uuid.ofString task.Uuid

            if loadedTasks |> List.contains id then None
            else
                Some {
                    Uuid = id
                    Name = task.Title
                }
        )
        |> Seq.toList
        |> teeDebug (List.length >> printfn "[Api] Loaded generic list tasks [%A]")

    let private parseDateTime64 = DateTimeOffset.FromUnixTimeMilliseconds

    let private tryParseDateTime64: int64 -> _ = function
        | zero when zero <= (int64 0) -> None
        | value -> value |> parseDateTime64 |> Some

    let private loadTaskList (credentials: Credentials) loadTask (tasks: TaskItem list): AsyncResult<Task list, exn list> =
        let loadTaskDetails load =
            tasks
            |> teeDebug (List.length >> printfn "[Api] Load details for tasks [%A] ...")
            |> List.map (fun { Uuid = id } -> loadTask id credentials)
            |> load id
            |> AsyncResult.map (List.choose id)

        loadTaskDetails AsyncResult.ofParallelAsyncResults
        |> AsyncResult.bindError (fun _ ->
            printfn "[Api] loading failed, retry 1st time (parallely) ..."
            loadTaskDetails AsyncResult.ofParallelAsyncResults
        )
        |> AsyncResult.bindError (fun _ ->
            printfn "[Api] loading failed, retry last time (sequentially) ..."
            loadTaskDetails AsyncResult.ofSequentialAsyncResults
        )

    type private ParsedEntity =
        | Task of Task
        | Project of Project

    [<RequireQualifiedAccess>]
    module private ParsedEntity =
        let task = function
            | Task task -> Some task
            | _ -> None |> teeDebug (fun _ -> printfn "[Api][ParsedEntity.Task] Not a task -> skip")

        let project = function
            | Project project -> Some project
            | _ -> None |> teeDebug (fun _ -> printfn "[Api][ParsedEntity.Project] Not a project -> skip")

    [<RequireQualifiedAccess>]
    module private ParseEntity =
        open Feather.ErrorHandling.Option.Operators

        type private EntitySchema = JsonProvider<"schema/entity.json", SampleIsList=true>
        type private SubtaskEntitySchema = JsonProvider<"schema/subtaskEntity.json", SampleIsList=true>

        let private parseString = function
            | empty when String.IsNullOrWhiteSpace empty -> None
            | string -> Some string

        let entity defaultType loadSubtasks loadComments value = asyncResult {
            let! (entity: EntitySchema.Root) =
                try EntitySchema.Parse value |> AsyncResult.ofSuccess
                with e -> AsyncResult.ofError e

            return!
                match entity.Type, defaultType with
                | Some "project", _ | None, Some "project" ->
                    asyncResult {
                        let project = {
                            Uuid = Uuid.ofString entity.Uuid
                            Name = entity.Title <??> entity.Name >>= parseString <?=> ""
                            Description = entity.Notes >>= parseString
                            Status =
                                match entity.Status with
                                | Some "active" -> Active
                                | Some other -> Other other
                                | _ -> Other ""

                            // Meta
                            Created = entity.Created |> parseDateTime64
                            Updated = entity.Updated |> parseDateTime64
                        }

                        return Project project
                    }
                | Some "task", _ | None, Some "task" ->
                    asyncResult {
                        let taskId = Uuid.ofString entity.Uuid

                        let! subtasks = loadSubtasks taskId
                        let! comments = loadComments taskId

                        let task = {
                            Id = Id entity.Id
                            Uuid = taskId
                            Name = entity.Title <??> entity.Name >>= parseString <?=> ""
                            Description = entity.Notes >>= parseString
                            Focus =
                                match entity.Attribute, entity.StartAt >>= tryParseDateTime64 with
                                | Some "plan", Some startAt -> Date startAt
                                | Some "next", None -> Next
                                | Some "noplan", None -> Someday
                                | _ -> Waiting
                            Deadline = entity.EndAt >>= tryParseDateTime64
                            SubTasks = subtasks
                            Comments = comments
                            Project = entity.Project <!> (fun project -> { Id = Uuid.ofString project })
                            Context = entity.Context <!> (fun context -> { Id = Uuid.ofString context })
                            Tags = entity.Tags |> Seq.map (fun tag -> { Name = tag }) |> Seq.toList
                            Priority =
                                match entity.Priority with
                                | Some 3 -> High
                                | Some 2 -> Medium
                                | Some 1 -> Low
                                | _ -> Default

                            Deleted = entity.Deleted >>= tryParseDateTime64
                            Trashed = entity.Trashed >>= tryParseDateTime64
                            Completed = entity.Completed >>= tryParseDateTime64
                            Archived = entity.Archived >>= tryParseDateTime64
                            Hidden = entity.Hidden >>= tryParseDateTime64

                            // Meta
                            Source = entity.Source >>= parseString
                            Created = entity.Created |> parseDateTime64
                            Updated = entity.Updated |> parseDateTime64
                        }

                        return Task task
                    }
                | other, _ -> try failwithf "Other entity.Type of %A given." other with e -> AsyncResult.ofError e
        }

        let subTask value = result {
            let! entity =
                try SubtaskEntitySchema.Parse value |> Ok
                with e -> Error e

            return {
                Name = entity.Title |> parseString <?=> ""
                Completed = entity.Completed >>= tryParseDateTime64
                Created = entity.Created |> parseDateTime64
                Updated = entity.Updated |> parseDateTime64
            }
        }

        open Feather.ErrorHandling.AsyncResult.Operators

        let task loadSubtasks loadComments = entity (Some "task") loadSubtasks loadComments >!> ParsedEntity.task
        let project = entity (Some "project") (fun _ -> AsyncResult.ofSuccess []) (fun _ -> AsyncResult.ofSuccess []) >!> ParsedEntity.project

    // Endpoints

    open AsyncResult.Operators

    let subTasks (Uuid id) =
        api $"subtasks/{id}" SubTasksResponse.Parse
        >=> (fun data ->
            data.Entities
            |> Seq.map (fun e -> e.JsonValue.ToString() |> ParseEntity.subTask |> AsyncResult.ofResult)
            |> Seq.toList
            |> AsyncResult.sequenceM
        )
        >-> (function
            | Http.NotFound -> AsyncResult.ofSuccess []
            | e -> AsyncResult.ofError e
        )

    let comments (Uuid id) =
        api $"comments/{id}" CommentsResponse.Parse
        >!> (fun data ->
            data.Entities
            |> Seq.map (fun comment ->
                {
                    Content = comment.Content
                    Author = comment.Author
                    AuthorEmail = comment.AuthorEmail
                    Created = comment.Created |> parseDateTime64
                    Updated = comment.Updated |> parseDateTime64
                }
            )
            |> Seq.toList
        )

    let task (Uuid id) credentials =
        api $"task/{id}" TaskResponse.Parse credentials
        >>= (fun data ->
            match data.Task with
            | None ->
                None
                |> teeDebug (fun _ -> printfn "[Api][Task] Empty -> skip %A" id)
                |> AsyncResult.ofSuccess

            | Some task -> asyncResult {
                let loadSubtasks taskId = subTasks taskId credentials
                let loadComments taskId = comments taskId credentials

                let! task =
                    task.JsonValue.ToString()
                    |> ParseEntity.task loadSubtasks loadComments

                return task
        })

    let private apiList path loadedTasks credentials =
        api path (parseGenericList loadedTasks) credentials
        <@> List.singleton
        >>= loadTaskList credentials task

    let today = apiList "tasks/today"
    let next = apiList "tasks/next"
    let scheduled = apiList "tasks/scheduled"
    let someday = apiList "tasks/someday"
    let waiting = apiList "tasks/waiting"

    let projectTasks (Uuid id) = apiList $"tasks/project/{id}"
    let contextTasks (Uuid id) = apiList $"tasks/context/{id}"

    let resources =
        api "resources_init" ResourcesResponse.Parse
        >=> (fun data -> asyncResult {
            let! projects =
                data.Resources.Projects
                |> Seq.map (fun p -> p.JsonValue.ToString() |> ParseEntity.project)
                |> Seq.toList
                |> AsyncResult.ofSequentialAsyncResults id <@> List.head

            let contexts =
                data.Resources.Contexts
                |> Seq.map (fun c ->
                    let c: Context = {
                        Uuid = Uuid.ofString c.Uuid
                        Name = c.Name
                        Created = c.Created |> parseDateTime64
                        Updated = c.Updated |> parseDateTime64
                    }

                    c
                )
                |> Seq.toList

            let tags =
                data.Resources.Tags
                |> Seq.map (fun t -> {
                    Uuid = Uuid.ofString t.Uuid
                    Name = t.Name
                    Created = t.Created |> parseDateTime64
                    Updated = t.Updated |> parseDateTime64
                })
                |> Seq.toList

            return {
                Projects = projects |> List.choose id
                Contexts = contexts
                Tags = tags
            }
        })

    let completed = apiList "tasks/completed"
    let trashed = apiList "tasks/trash"
