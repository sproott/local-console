# Alma.Build migration command

## Purpose

Provide a `local-console` command that ports `~/.local/bin/migrate-to-alma-build` into
this repo, so migrating a target repo's hand-rolled FAKE/Paket build infra onto the
shared `Alma.Build` NuGet package no longer depends on an out-of-repo bash script.

## Command

`build:migrate` (new `build` namespace).

### Arguments

- `path` (optional, default `.`): path to the target repository. Resolved to the git
  top-level directory (`git rev-parse --show-toplevel`) before any edit.

### Options

- `alma-version` (optional, default: newest version published on NuGet, falling back
  to `2.0.0` if the NuGet lookup fails): `Alma.Build` version to pin. (Named
  `alma-version` rather than `version` because `version` is reserved by the console
  application framework for its own `--version` flag.)
- `dry-run` (no-value): print every planned file edit/deletion/process invocation
  without touching the filesystem or running any process.

### Behavior

Mirrors `~/.local/bin/migrate-to-alma-build` step for step, operating on the resolved
repo root:

1. Resolve the repo root via `git rev-parse --show-toplevel`.
2. In `paket.dependencies`, insert `nuget Alma.Build ~> <version>` as the first line of
   `group Build`, then remove every `nuget Fake.*` line from `group Build` onward.
3. In `build/paket.references`, ensure a bare `Alma.Build` line exists directly under
   `group Build`, then remove every `Fake.*` line from `group Build` onward.
4. For each of `Utils.fs`, `Targets.fs`, `Commands.fs`, `Command.fs`, `RtkFilter.fs`,
   `SafeBuildHelpers.fs`: drop its `<Compile Include="…" />` line from
   `build/build.fsproj`, and if the file exists under `build/`, remove it with
   `git rm -q`.
5. In `build/Build.fs`: strip the leading `//`/blank-line header block, replace
   `open ProjectBuild` with `open Alma.Build`, and remove the contiguous `open Fake.*`
   import block (plus the blank line following it).
6. Run `dotnet tool restore`.
7. Run `dotnet tool run paket install`.
8. Run `dotnet build ./build/build.fsproj`.
9. Run `dotnet run --no-build --project ./build/build.fsproj -- Bootstrap`.
10. Run `./build.sh Tests`.

`dry-run` stops after printing the plan for steps 2-5 and lists steps 6-10 without
invoking them.

### Errors

Any failed process invocation (non-zero exit code) stops the command immediately with
a non-zero exit code; the process's own output is already visible on the console
(inherited stdout/stderr, no re-capture or reformatting).

## Out of scope

- Reconciling *custom* local file content (e.g. a repo-specific target file not in the
  deletion list) — the shell script doesn't handle that case either.
- Running the migration against `local-console`'s own `build/` directory as part of
  building this feature.
