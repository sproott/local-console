namespace MF.DoIt

type DoItBackup = {
    Tasks: Task list
    Projects: Project list
    Contexts: Context list
    Tags: Tag list
}

[<RequireQualifiedAccess>]
module DoItBackup =
    open System
    open Feather.ConsoleStyle
    open MF.Utils
    open Feather.ErrorHandling
    open Feather.ErrorHandling.AsyncResult.Operators

    [<AutoOpen>]
    module private Loader =
        (* type private Progress = // todo - remove when ConsoleStyle 3.0.0 is out
            | Active of Feather.ConsoleApplication.ProgressBar
            | Inactive

            with
                member this.Finish() =
                    match this with
                    | Active (Some progressBar) -> progressBar.Dispose()
                    | _ -> ()

                interface IDisposable with
                    member this.Dispose() = this.Finish() *)

        type ShowLoaderInfo<'Parent> = {
            Info: 'Parent -> unit
            TasksInfo: Task list -> unit
            LoadTasksError: exn list -> unit
        }
        type TasksLoader = Api.LoadedTasks -> Credentials -> AsyncResult<Task list, exn list>

        /// Load tasks from loaders sequentually with progress
        let loadTasks (output: Feather.ConsoleApplication.Output) credentials =
            let rec load progress (acc: Task list): (string * TasksLoader) list -> _ = function
                | [] -> acc |> AsyncResult.ofSuccess
                | (title, loader) :: rest -> asyncResult {
                    if output.IsVerbose() then output.Message $"<c:cyan>[DoIt][Backup]</c> Loading tasks from <c:yellow>{title}</c> ..."

                    let ignoredTasks = acc |> List.map Task.id |> Api.LoadedTasks
                    let! (tasks: Task list) = loader ignoredTasks credentials

                    progress |> output.ProgressAdvance

                    (* match progress with
                    | ProgressBar.Active progressBar -> progressBar |> output.ProgressAdvance
                    | Inactive when output.IsVeryVerbose() -> output.Message $"  └─> <c:magenta>{tasks.Length}</c> tasks loaded"
                    | Inactive -> () *)

                    return! rest |> load progress (acc @ tasks |> List.distinctBy Task.id)
                }

            function
            | [] -> AsyncResult.ofSuccess []
            | loaders ->
                if output.IsVerbose() then
                    loaders |> load ProgressBar.inactive []
                else
                    use progress = output.ProgressStart "Load tasks" loaders.Length
                    loaders |> load progress []

        /// Create an api loader for a parent container (project, context, ...) of tasks
        let load<'Parent> (parents: 'Parent list) show parentId apiFetch: TasksLoader =
            fun loadedTasks credentials ->
                parents
                |> List.map (fun parent -> asyncResult {
                    show.Info parent

                    return!
                        apiFetch (parentId parent) loadedTasks credentials
                        |> AsyncResult.tee show.TasksInfo
                        |> AsyncResult.teeError show.LoadTasksError
                })
                |> AsyncResult.ofSequentialAsyncResults List.singleton
                <!> List.concat
                <@> List.concat

    let load (output: Feather.ConsoleApplication.Output) credentials = asyncResult {
        output.Title "[DoIt][Backup] Load data for backup"
        let prefix = "<c:cyan>[DoIt][Backup]</c>"

        if output.IsVerbose() then output.Message $"{prefix} Loading <c:yellow>resources</c> ..."

        let! (resources: Resources) = Api.resources credentials <@> List.singleton

        if output.IsVeryVerbose() then
            output.Table ["Resource"; "Count"] [
                [ "Projects"; resources.Projects |> List.length |> sprintf "<c:magenta>%d</c>" ]
                [ "Contexts"; resources.Contexts |> List.length |> sprintf "<c:magenta>%d</c>" ]
                [ "Tags"; resources.Tags |> List.length |> sprintf "<c:magenta>%d</c>" ]
            ]
        elif output.IsVerbose() then output.Message $"{prefix} Resources loaded"

        let showInfo parentName =
            {
                Info = parentName >> fun name ->
                    if output.IsVeryVerbose() then
                        output.Message $"  ├─┬─ loading tasks for <c:cyan>{name}</c> ..."
                TasksInfo = fun tasks ->
                    if output.IsVeryVerbose() then
                        output.Message $"  │ └──> <c:magenta>{tasks.Length}</c> tasks loaded"
                LoadTasksError = fun errors ->
                    if output.IsVeryVerbose() then
                        output.Message $"  │ └──> tasks <c:red>NOT loaded due to errors</c> <c:magenta>{errors.Length}</c>"
            }

        let! (tasks: Task list) =
            loadTasks output credentials [
                "projects", load resources.Projects (showInfo Project.name) Project.id Api.projectTasks
                "contexts", load resources.Contexts (showInfo Context.name) Context.id Api.contextTasks

                "today", Api.today
                "next", Api.next
                "scheduled", Api.scheduled
                "someday", Api.someday
                "waiting", Api.waiting

                "completed", Api.completed
                "trashed", Api.trashed
            ]

        if output.IsVerbose() then output.Message $"{prefix} Tasks loaded [<c:magenta>{tasks.Length}</c>]"

        return {
            Tasks = tasks
            Projects = resources.Projects
            Contexts = resources.Contexts
            Tags = resources.Tags
        }
    }

    let staticBackup () =
        let uuid = Uuid.create
        let now = DateTimeOffset.Now

        let project1 = uuid()
        let context1 = uuid()

        {
            Tasks = [
                {
                    Id = Id "1"
                    Uuid = uuid()
                    Name = "Task"
                    Description = None
                    Focus = Next
                    Deadline = None
                    SubTasks = []
                    Comments = []
                    Project = None
                    Context = None
                    Tags = []
                    Priority = Default
                    Deleted = None
                    Trashed = None
                    Completed = None
                    Archived = None
                    Hidden = None
                    Source = Some "static"
                    Created = now
                    Updated = now
                }
                {
                    Id = Id "2"
                    Uuid = uuid()
                    Name = "Task 2"
                    Description = None
                    Focus = Date now
                    Deadline = None
                    SubTasks = [
                        {
                            Name = "Sub 1"
                            Completed = None
                            Created = now
                            Updated = now
                        }
                    ]
                    Comments = [
                        {
                            Content = "Comment"
                            Author = "author"
                            AuthorEmail = "author@email.com"
                            Created = now
                            Updated = now
                        }
                    ]
                    Project = Some { Id = project1 }
                    Context = Some { Id = context1 }
                    Tags = [
                        { Name = "Tag" }
                    ]
                    Priority = High
                    Deleted = None
                    Trashed = None
                    Completed = None
                    Archived = None
                    Hidden = None
                    Source = Some "static"
                    Created = now
                    Updated = now
                }
            ]
            Projects = [
                {
                    Uuid = project1
                    Name = "Project"
                    Description = None
                    Status = Status.Active
                    Created = now
                    Updated = now
                }
            ]
            Contexts = [
                {
                    Uuid = context1
                    Name = "Context"
                    Created = now
                    Updated = now
                }
            ]
            Tags = [
                {
                    Uuid = uuid()
                    Name = "Tag"
                    Created = now
                    Updated = now
                }
            ]
        }

    open System.IO

    let saveToFile serialize file (backup: DoItBackup) =
        let (serialized: string) = backup |> serialize

        File.WriteAllText (file, serialized)
