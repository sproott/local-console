# Plan: alma-build-migration-command

Specs touched:
- creates: docs/specs/alma-build-migration-command/spec.md

## Overview

Port `~/.local/bin/migrate-to-alma-build` (a bash script that migrates a repo's hand-rolled
FAKE/Paket build infra onto the shared `Alma.Build` NuGet package) into a new
`build:migrate` command in this `local-console` app, implemented fully in F#.

The repo has no test project (`Core.fsproj`/`Utils.fsproj`/etc. are all `Library`/`Exe`,
no `tests/` dir, no test runner referenced in `paket.dependencies`). Verification is
manual: a `dry-run` mode plus running the command against a disposable fixture repo.

## Architecture Decisions

- New command module `src/Core/Command/MigrateBuildCommand.fs`, following the
  existing one-module-per-command convention (see `DirRemoveSubdirCommand.fs`,
  `RepositoryBackup.fs`).
- Pure step functions (file-content transforms) separated from the effectful runner,
  mirroring `DirRemoveSubdirCommand`'s `run`/`execute` split — text transforms are
  independently reasoned about and easy to dry-run.
- Shell out via `Fake.Core.Process`'s `CreateProcess`/`Proc.run`, already a transitive
  dependency (`Fake.IO.FileSystem` pulls in `Fake.Core.Process`) and the same primitive
  `build/Utils.fs`'s `createProcess`/`run` used — no new package needed. Inherit
  stdio so `dotnet build`/`paket install`/`./build.sh Tests` output streams live,
  matching the bash script's behavior (no output re-capture, per spec "Errors").
- Git root resolution and `git rm` both shell out to `git` the same way, rather than
  pulling in `LibGit2Sharp` (already a dependency) — `git rm` specifically needs the
  CLI's index-aware delete, which `LibGit2Sharp` doesn't expose directly.
- Represent the plan (steps 2-5) as a list of discriminated-union "actions"
  (`EditFile of path * (string -> string)` | `DeleteFile of path`), so `dry-run` prints
  the same list the real run executes, instead of duplicating logic in two code paths.

## Task List

### Phase 1: Command skeleton and argument/option wiring
- [ ] Task 1: Add `build:migrate` command registration in `src/Program.fs` with
      `path` (optional arg, default `.`), `alma-version` (optional option, default `2.0.0`),
      `dry-run` (no-value option), and description/help text following the existing
      command block style.
- [ ] Task 2: Create `src/Core/Command/MigrateBuildCommand.fs` with `arguments`,
      `options`, and a stub `execute` that resolves the repo root via
      `git rev-parse --show-toplevel` (shelling out, `Result`-wrapped) and prints it;
      register the file in `Core.fsproj`'s `<Compile>` list right after
      `RepositoryBuildList.fs` (keeps the `repository:*`/`dir:*`-adjacent commands
      grouped, new file next in declaration order).

### Checkpoint: Phase 1
- [ ] `dotnet build` succeeds.
- [ ] `dotnet run --project local-console.fsproj -- build:migrate --help` shows
      the new command with correct argument/option descriptions.
- [ ] `dotnet run --project local-console.fsproj -- build:migrate /tmp/nonexistent`
      fails cleanly (non-zero exit, readable error) when the path isn't a git repo.

### Phase 2: Pure text-transform steps (paket.dependencies / paket.references / build.fsproj / Build.fs)
- [ ] Task 3: Implement `PaketDependencies.migrate: version:string -> content:string ->
      string` — inserts `nuget Alma.Build ~> <version>` as first line of `group Build`,
      then drops every `nuget Fake.*` line in that group.
- [ ] Task 4: Implement `PaketReferences.migrate: content:string -> string` — ensures a
      bare `Alma.Build` line directly under `group Build`, drops every `Fake.*` line in
      that group.
- [ ] Task 5: Implement `BuildFsproj.migrate: content:string -> string` — strips
      `<Compile Include="…" />` lines for the five superseded filenames
      (`Utils.fs`, `Targets.fs`, `Commands.fs`, `Command.fs`, `RtkFilter.fs`,
      `SafeBuildHelpers.fs`).
- [ ] Task 6: Implement `BuildFs.migrate: content:string -> string` — strips the
      leading `//`/blank header block, replaces `open ProjectBuild` with
      `open Alma.Build`, removes the contiguous `open Fake.*` block (+ trailing blank
      line).
- [ ] Task 7: Wire all four transforms into `MigrateBuildCommand`'s plan-building
      function, producing the `EditFile`/`DeleteFile` action list from spec steps 2-5
      (file deletions for the five superseded `build/*.fs` files, guarded by
      `File.Exists`).

### Checkpoint: Phase 2
- [ ] Each transform has a case in a scratch F# script or `dotnet fsi` session
      exercised against a copy of this repo's own `build/paket.dependencies` /
      `build/paket.references` / `build/build.fsproj` / `build/Build.fs` — confirms
      the transforms produce the same diff the bash script would (compare by hand
      against `~/.local/bin/migrate-to-alma-build`'s `sed`/`awk` behavior).
- [ ] No test project exists in this repo (see Overview) — this checkpoint is manual
      inspection, not an automated suite.

### Phase 3: Dry-run rendering and file-system application
- [ ] Task 8: Implement dry-run rendering: for each planned action, print
      `edit <path>` / `delete <path>` (plus a unified before/after line count or diff
      preview), and list the five verification commands (steps 6-10) as "would run".
- [ ] Task 9: Implement real application: write each `EditFile` transform result back
      to disk, delete each `DeleteFile` target via `git rm -q` (shelled out, working
      directory = repo root).

### Checkpoint: Phase 3
- [ ] `--dry-run` against the `fmetrics` fixture (see Phase 4) prints the expected
      action list and makes zero filesystem changes (`git status` empty after).
- [ ] Without `--dry-run`, a disposable clone of `fmetrics` ends up with
      `paket.dependencies`/`build/paket.references`/`build/build.fsproj`/
      `build/Build.fs` matching what the bash script would produce, and the five
      superseded files are `git rm`-staged.

### Phase 4: Verification pipeline (steps 6-10)
- [ ] Task 10: Implement the sequential process runner: `dotnet tool restore` →
      `dotnet tool run paket install` → `dotnet build ./build/build.fsproj` →
      `dotnet run --no-build --project ./build/build.fsproj -- Bootstrap` →
      `./build.sh Tests`, each inheriting stdio, each aborting the chain on non-zero
      exit with that exit code surfaced as the command's `ExitCode.Error`.
- [ ] Task 11: Skip individual-step execution (print only) when `--dry-run` is passed.

### Checkpoint: Phase 4 (end-to-end)
- [ ] Take a disposable clone of
      `/home/david_hrabe/Data/Programming/Work/Alma-OSS/fmetrics` (still on the local
      `ProjectBuild`-style `build/` engine) and run `build:migrate <clone-path>`
      for real.
- [ ] Confirm: `paket install`, `dotnet build ./build/build.fsproj`, `Bootstrap`, and
      `./build.sh Tests` all succeed against the migrated clone.
- [ ] Confirm a deliberately broken fixture (e.g. missing `build/Build.fs`) surfaces a
      clear F# error and non-zero exit rather than a partial silent migration.

### Phase 5: Docs
- [ ] Task 12: Add the new command to `README.md`'s "Available commands" listing
      (new `build` namespace section) and a CHANGELOG.md entry under `## Unreleased`.

### Checkpoint: Complete
- [ ] All acceptance criteria above met.
- [ ] `dotnet build` clean, no compiler warnings introduced.
- [ ] Ready for review.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| No test project in this repo means no automated regression coverage for the text transforms | Medium | Keep transforms as small pure functions; verify manually against this repo's own `build/` files each phase; consider a follow-up to add a test project (out of scope here per user's "implemented fully in F#" ask, not "add tests") |
| Text-transform edge cases (e.g. `Fake.*` line ordering, blank-line quirks) diverge from the bash script's `sed`/`awk` behavior | Medium | Task 7's checkpoint explicitly diffs output against the bash script's behavior on this repo's own files |
| Shelling out via `Fake.Core.Process` inside a console-app command (rather than a FAKE build script) is unusual for this codebase | Low | `build/Utils.fs`'s `createProcess`/`run` already do exactly this pattern; reuse it verbatim rather than inventing a new one |

## Open Questions

None outstanding — end-to-end fixture is a disposable clone of `fmetrics`.
