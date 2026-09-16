# Todo: alma-build-migration-command

See [plan.md](plan.md) for full task descriptions, rationale, risks, and checkpoints.

## Phase 1: Command skeleton and argument/option wiring
- [x] Task 1: Register `build:migrate` in `src/Program.fs` (arg `path`, options
      `alma-version`/`dry-run`)
- [x] Task 2: Create `MigrateBuildCommand.fs` stub resolving repo root via
      `git rev-parse --show-toplevel`; add to `Core.fsproj`

### Checkpoint: Phase 1
- [x] `dotnet build` succeeds
- [x] `--help` shows correct arguments/options
- [x] Non-git path fails cleanly with non-zero exit code

## Phase 2: Pure text-transform steps
- [x] Task 3: `PaketDependencies.migrate`
- [x] Task 4: `PaketReferences.migrate`
- [x] Task 5: `BuildFsproj.migrate`
- [x] Task 6: `BuildFs.migrate`
- [x] Task 7: Wire transforms into plan-building function (+ file deletions)

### Checkpoint: Phase 2
- [x] Transforms verified by hand against this repo's own `build/` files
- [x] (No automated tests exist in this repo — manual inspection only)

## Phase 3: Dry-run rendering and file-system application
- [x] Task 8: Dry-run action rendering
- [x] Task 9: Real application (`EditFile` write, `DeleteFile` via `git rm -q`)

### Checkpoint: Phase 3
- [x] `--dry-run` makes zero filesystem changes on a disposable `fmetrics` clone
- [x] Non-dry-run produces expected file state on a disposable `fmetrics` clone

## Phase 4: Verification pipeline
- [x] Task 10: Sequential runner for restore → paket install → build → Bootstrap →
      `./build.sh Tests`, abort chain on first non-zero exit
- [x] Task 11: `--dry-run` prints instead of running

### Checkpoint: Phase 4 (end-to-end)
- [x] Full pipeline succeeds against a disposable clone of
      `/home/david_hrabe/Data/Programming/Work/Alma-OSS/fmetrics` (required an upstream
      fix in `Alma.Build` itself — see below)
- [x] Broken fixture surfaces a clear error, non-zero exit, no partial silent migration
      (verified: a pre-existing lint failure in fmetrics' own tests aborted the chain
      cleanly with a non-zero exit)

**Unplanned fix (blocking dependency):** `Alma.Build`'s own `paket.references` had no
`FSharp.Core` line, so its packed nuspec never declared an `FSharp.Core` dependency.
Consumers installing `Alma.Build` could resolve an `FSharp.Core` version inconsistent
with the one `Alma.Build.dll` was compiled against, crashing `Bootstrap` with a
`FileNotFoundException`. Fixed at the source
(`/home/david_hrabe/Data/Programming/Work/Alma-OSS/fbuild/src/Alma.Build/paket.references`)
by adding an unpinned `FSharp.Core` reference; verified via a locally-packed preview
(`publish-local`) against a disposable `fmetrics` clone. This also surfaced the same bug
pattern in `local-console` itself (`src/Utils`, `src/ErrorHandling` lacked explicit
`FSharp.Core` references), fixed alongside it.

## Phase 5: Docs
- [ ] Task 12: README.md command listing + CHANGELOG.md entry

### Checkpoint: Complete
- [ ] All acceptance criteria met
- [ ] `dotnet build` clean
- [ ] Ready for review
