open System
open System.IO
open Feather.ConsoleApplication
open MF.LocalConsole
open MF.LocalConsole.Console
open MF.Monad

[<EntryPoint>]
let main argv =
    consoleApplication {
        title AssemblyVersionInformation.AssemblyProduct
        info ApplicationInfo.MainTitle
        version AssemblyVersionInformation.AssemblyVersion

        command "monad:play" {
            Description = "Just play with monads."
            Help = None
            Arguments = []
            Options = []
            Initialize = None
            Interact = None
            Execute = Monad.execute
        }

        command "repository:backup" {
            Description = "Backup repositories - command will save all repository remote urls to the output file."
            Help = commandHelp [
                "The <c:dark-green>{{command.name}}</c> saves repository remote urls:"
                "        <c:dark-green>dotnet {{command.full_name}}</c> <c:dark-yellow>path-to-repositories/</c>"
            ]
            Arguments = [
                Argument.repositories
            ]
            Options = [
                Option.outputFile
                Option.optional "ignore-repo" None "Path to file where there are repositories which will be ignored no matter where they are." None
                Option.optional "ignore-file" None "Path to file where there are files/paths which will be ignored from currently ignored files in repositories." None
                Option.noValue "only-complete" None "Select only repositories with all changes commited."
                Option.noValue "only-incomplete" None "Select only repositories where are some uncommited changes (untracked files, modified, ...)."
            ]
            Initialize = None
            Interact = None
            Execute = RepositoryBackupCommand.execute
        }

        command "repository:restore" {
            Description = "Restore backuped repositories - command will restore all repositories out of a backup, created by repository:backup command."
            Help = commandHelp [
                "The <c:dark-green>{{command.name}}</c> restores repositories:"
                "        <c:dark-green>dotnet {{command.full_name}}</c> <c:dark-yellow>path-to-repositories/</c>"
            ]
            Arguments = [
                Argument.required "backup" "Path to dir containing backup."
            ]
            Options = [
                Option.optional "ignore-remote" None "Path to file where there are remotes which will be ignored." None
                Option.noValue "dry-run" None "Whether to just show a result as stdout."
                Option.noValue "use-shell" None "Whether to create a shell script to create repositories."
            ]
            Initialize = None
            Interact = None
            Execute = RepositoryCreateCommand.execute
        }

        command "dir:sub:remove" {
            Description = "Remove a subdir(s) (and its content) found in the dir."
            Help = commandHelp [
                "The <c:dark-green>{{command.name}}</c> remove subdir(s) repositories:"
                "        <c:dark-green>dotnet {{command.full_name}}</c> <c:dark-yellow>path-to-repositories/</c>"
            ]
            Arguments = [
                Argument.required "dir" "Path to dir, where you want to find sub-dirs to remove."
                Argument.requiredArray "dirsToRemove" "Name of the dir(s), which you want to remove (For example: <c:cyan>vendor, node_modules, ...</c>)."
            ]
            Options = [
                Option.noValue "dry-run" None "Whether to just show a result as stdout."
            ]
            Initialize = None
            Interact = None
            Execute = DirRemoveSubdirCommand.execute
        }

        command "repository:build:list" {
            Description = "List all repositories for the build.fsx version and type."
            Help = commandHelp [
                "The <c:dark-green>{{command.name}}</c> saves repository remote urls:"
                "        <c:dark-green>dotnet {{command.full_name}}</c> <c:dark-yellow>path-to-repositories/</c>"
            ]
            Arguments = [
                Argument.repositories
            ]
            Options = [
                Option.optional "type" (Some "t") "Show only builds of this type." None
                Option.optional "build-version" (Some "b") "Show only builds of this version." None
            ]
            Initialize = None
            Interact = None
            Execute = RepositoryBuildListCommand.execute
        }

        command "azure:func" {
            Description = "Calls a azure function."
            Help = commandHelp [
                "The <c:dark-green>{{command.name}}</c> calls azure function 10 times:"
                "        <c:dark-green>dotnet {{command.full_name}}</c> <c:dark-yellow>app-name</c> <c:dark-yellow>function-name</c> <c:dark-magenta>10</c> --code=<your-api-key>"
            ]
            Arguments = [
                Argument.required "app-name" "Name of the azure application."
                Argument.required "function-name" "Name of the function."
                Argument.required "call-count" "Count of calls to be made."
            ]
            Options = [
                Option.optional "code" (Some "c") "Code to your function." None
            ]
            Initialize = None
            Interact = None
            Execute = AzureFuncCommand.execute
        }

        command "normalize:file" {
            Description = "Call a normalize function for each line of the file."
            Help = None
            Arguments = [
                Argument.required "file-name" "Name of the file to normalize."
                Argument.required "input-type" "Type of the input data (<c:blue>email</c>, <c:blue>phone</c>)."
            ]
            Options = [
                Option.optional "code" (Some "c") "Code to your function." None
                Option.optional "output" (Some "o") "Output dir." None
            ]
            Initialize = None
            Interact = None
            Execute = NormalizeCommand.execute
        }

        command "normalize:phone" {
            Description = "Normalize a single phone number"
            Help = None
            Arguments = [
                Argument.required "phone" "Phone number input."
            ]
            Options = []
            Initialize = None
            Interact = None
            Execute = NormalizeCommand.executePhone
        }

        command "stats:phone:code" {
            Description = "Search phone codes in the file and show stats for them."
            Help = None
            Arguments = [
                Argument.required "file-name" "Name of the file to normalize."
            ]
            Options = [
                Option.optional "output" (Some "o") "Output dir." None
            ]
            Initialize = None
            Interact = None
            Execute = PhoneCodeStatsCommand.execute
        }

        command "stats:contacts" {
            Description = "Show stats for normalized files."
            Help = None
            Arguments = [
                Argument.required "file-name" "Name of the file to normalize."
            ]
            Options = []
            Initialize = None
            Interact = None
            Execute = ContactStatsCommand.execute
        }

        command "doit:backup" {
            Description = "Backup a doit.im data."
            Help = None
            Arguments = DoitBackupCommand.arguments
            Options = DoitBackupCommand.options
            Initialize = None
            Interact = None
            Execute = DoitBackupCommand.execute
        }

        command "grafana:parse:metrics" {
            Description = "Parse metrics from a grafana query saved in the json file."
            Help = None
            Arguments = ParseGrafanaMetricsCommand.arguments
            Options = ParseGrafanaMetricsCommand.options
            Initialize = None
            Interact = None
            Execute = ParseGrafanaMetricsCommand.execute
        }

        command "stream:test" {
            Description = "Stream playground command."
            Help = None
            Arguments = [
                Argument.optionalArray "file-name" "Name of the file." None
            ]
            Options = []
            Initialize = None
            Interact = None
            Execute = StreamTestCommand.execute
        }

        command "cognito:stats" {
            Description = "Show stats for cognito users."
            Help = None
            Arguments = CognitoStats.args
            Options = CognitoStats.options
            Initialize = None
            Interact = None
            Execute = CognitoStats.execute
        }

        command "rohlik:products" {
            Description = "Analyze products from Rohlik order history and show product summary."
            Help = commandHelp [
                "The <c:dark-green>{{command.name}}</c> analyzes your Rohlik order history:"
                "        <c:dark-green>dotnet {{command.full_name}}</c> <c:dark-yellow>credentials.json</c> <c:gray>[order-limit]</c>"
                ""
                "Example credentials.json:"
                "        <c:gray>{</c>"
                "            <c:dark-yellow>\"username\"</c>: <c:green>\"your-email@example.com\"</c>,"
                "            <c:dark-yellow>\"password\"</c>: <c:green>\"your-password\"</c>"
                "        <c:gray>}</c>"
            ]
            Arguments = RohlikProductsCommand.arguments
            Options = RohlikProductsCommand.options
            Initialize = None
            Interact = None
            Execute = RohlikProductsCommand.execute
        }

        command "rohlik:analyze" {
            Description = "Analyze Rohlik product data from NDJSON file and display as table."
            Help = commandHelp [
                "The <c:dark-green>{{command.name}}</c> reads and displays product data from an NDJSON file:"
                "        <c:dark-green>dotnet {{command.full_name}}</c> <c:dark-yellow>rohlik-products.ndjson</c>"
                ""
                "If no file is specified, it will look for 'rohlik-products.ndjson' in the current directory."
            ]
            Arguments = RohlikAnalyzeCommand.arguments
            Options = RohlikAnalyzeCommand.options
            Initialize = None
            Interact = None
            Execute = RohlikAnalyzeCommand.execute
        }
    }
    |> run argv
