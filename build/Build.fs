open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "Local Console"
            Summary = "Console application for local helpers."
            Git = Git.init ()
        }
        Specs = Spec.defaultConsoleApplication [
            OSX
            // Windows
        ]
    }

    args |> Args.run
