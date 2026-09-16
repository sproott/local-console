Local console
=============

[![Build Status](https://dev.azure.com/MortalFlesh/LocalConsole/_apis/build/status/MortalFlesh.LocalConsole)](https://dev.azure.com/MortalFlesh/LocalConsole/_build/latest?definitionId=1)
[![Build Status](https://api.travis-ci.com/MortalFlesh/local-console.svg?branch=master)](https://travis-ci.com/MortalFlesh/local-console)

## Run statically

First compile
```sh
fake build target release
```

Then run
```sh
dist/local-console help
```

List commands
```sh
dist/local-console list
```

       __                    __       _____                        __
      / /  ___  ____ ___ _  / /      / ___/ ___   ___   ___ ___   / / ___
     / /__/ _ \/ __// _ `/ / /      / /__  / _ \ / _ \ (_-</ _ \ / / / -_)
    /____/\___/\__/ \_,_/ /_/       \___/  \___//_//_//___/\___//_/  \__/


    ==============================================================================

    Usage:
        command [options] [--] [arguments]

    Options:
        -h, --help            Display this help message
        -q, --quiet           Do not output any message
        -V, --version         Display this application version
        -n, --no-interaction  Do not ask any interactive question
        -v|vv|vvv, --verbose  Increase the verbosity of messages

    Available commands:
        about                  Displays information about the current project.
        help                   Displays help for a command
        list                   Lists commands
    azure
        azure:func             Calls a azure function.
    build
        build:migrate          Migrate a repository's hand-rolled FAKE/Paket build infra onto the shared Alma.Build NuGet package.
    dir
        dir:sub:remove         Remove a subdir(s) (and its content) found in the dir.
    normalize
        normalize:file         Call a normalize function for each line of the file.
        normalize:phone        Normalize a single phone number
    repository
        repository:backup      Backup repositories - command will save all repository remote urls to the output file.
        repository:build:list  List all repositories for the build.fsx version and type.
        repository:restore     Restore backuped repositories - command will restore all repositories out of a backup, created by repository:backup command.
    stats
        stats:contacts         Show stats for normalized files.
        stats:phone:code       Search phone codes in the file and show stats for them.

---
### Development

First run `./build.sh` or `./build.sh -t watch`

List commands
```sh
bin/console list
```

Run tests locally
```sh
fake build target Tests
```
