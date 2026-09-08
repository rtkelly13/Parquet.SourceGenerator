# 16 — The Regression Suite (`/regression`)

Compatibility, conformance and fixture-integrity testing lives outside the ordinary development
loop. `dotnet test` stays the fast inner loop; `/regression` is the outer one, and it is the same
code path locally and in CI.

## Entrypoint

```bash
# Agent / editor slash command (.claude/commands/regression.md)
/regression            # quick
/regression full
/regression deep

# The command it runs, usable directly
dotnet run --project tools/RegressionRunner/RegressionRunner.csproj --configuration Release -- quick
```

The runner is `tools/RegressionRunner`. It owns the definition of what each tier runs, so the
workflow and the local command cannot drift apart: CI invokes the same executable with the same
arguments.

## Tiers

The tiers are strictly nested — `deep` ⊃ `full` ⊃ `quick` — and a test asserts the nesting.

| Tier | Budget | What it runs | Prerequisites |
| --- | --- | --- | --- |
| `quick` (default) | 5 min | Release build, checked-in fixture hash integrity, the deterministic generated round trips and the corpus reads | .NET SDK |
| `full` | 40 min | quick, plus PyArrow v1/v2 corpus regeneration, PyArrow verification of a generated file, DuckDB bidirectional interop, external-interop reads, and the version matrix re-run against freshly generated data | `uv`, DuckDB CLI |
| `deep` | 3 h | full, plus broad seeded property coverage, corrupted-input handling, large multi row-group datasets, IL boxing interrogation, and a Native AOT publish-and-execute | `uv`, DuckDB CLI, native AOT toolchain |

Scale knobs for `deep`: `--seeds N` (default 512) and `--large-rows N` (default 500,000). They are
passed to the test process as `PARQUET_REGRESSION_PROPERTY_SEEDS` and
`PARQUET_REGRESSION_LARGE_ROWS`; the tests fall back to small defaults so a plain `dotnet test`
stays quick.

## Flags

| Flag | Effect |
| --- | --- |
| `--dry-run` | Probe prerequisites, print the plan and the exact command lines, execute nothing. |
| `--keep-going` | Run every step even after one fails. |
| `--allow-missing-tools` | Record an absent prerequisite as a skip instead of a failure. **Never used in CI.** |
| `--enforce-budget` | Fail the run when it exceeds the tier's budget. CI enforces this for `quick`. |
| `--artifacts <dir>` | Where to write logs, manifests and generated data. Default `temp/regression/<run-id>/`. |
| `--repo-root <dir>`, `--run-id <id>`, `--rid <rid>` | Overrides for the discovered root, the run identifier and the AOT runtime identifier. |

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Everything the tier promised ran and passed. |
| 1 | A step failed. |
| 2 | A required external tool was missing (prerequisites are probed *before* any step runs). |
| 3 | The run exceeded an enforced budget. |
| 4 | A checked-in fixture changed on disk during the run. |
| 64 | Bad command line. |

Distinct codes are deliberate: "PyArrow was not installed" and "PyArrow read our file wrong" must
not look the same in a log.

## What every run reports

Each run writes into its artifact directory:

- `run-manifest.json` — machine-readable: mode, exact tool versions and resolved paths, every step
  with its command line, environment, exit code, duration and log path, the fixture-tree digest
  before and after, and the list of advertised-but-unimplemented coverage.
- `summary.md` — the same thing as Markdown, appended to the CI job summary.
- `logs/<step>.log` — captured stdout/stderr per step, prefixed with the command that produced it.
- The generated Parquet files themselves, retained on success as well as failure.

Failures print a reproduction block: the environment exports followed by the exact command.

## Fixture safety

`test/data` is hashed file-by-file before and after every run. Any addition, removal or
modification fails the run with exit code 4 **even if every step passed** — a run that rewrote the
committed corpus has invalidated its own evidence. Generated data is routed into the artifact
directory through `PARQUET_TEST_DATA_OUTPUT_DIR`, `PARQUET_TEST_DATA_CSHARP_OUTPUT_DIR` and
explicit `--output` paths; nothing writes into `test/data`.

## CI wiring

`.github/workflows/regression.yml`:

- **pull_request** → `quick`, with `--enforce-budget`. This is the only tier a PR pays for.
- **workflow_dispatch** → the tier chosen from the `mode` input (`quick`/`full`/`deep`).
- **schedule** → `0 3 * * *` runs `full` nightly; `0 5 * * 6` runs `deep` weekly.

Three jobs: `resolve-mode` picks the tier from the trigger, `regression` runs it, and
`regression-gate` is the aggregate job worth requiring in branch protection. The gate uses
`if: always()` and fails unless every upstream job reports `success`, because a skipped or
cancelled job otherwise leaves the workflow green. Nothing in the workflow is
`continue-on-error`, and CI never passes `--allow-missing-tools`, so a missing engine fails
loudly rather than quietly reducing coverage.

`ci.yml` remains the comprehensive PR pipeline; its test filter is exactly the runner's
`RegressionPlan.DefaultTestFilter`, which excludes the deep categories (`Property`, `Corruption`,
`LargeDataset`). `RegressionWorkflowTests` asserts the two definitions stay identical.

## Coverage that is declared but not yet implemented

The runner prints these on every run and records them in the manifest, so a partially implemented
tier cannot pass itself off as complete:

| Id | Tier | Tracked by |
| --- | --- | --- |
| `apache-tooling-conformance` | full | #167 |
| `duckdb-bidirectional-matrix` | full | #166 |
| `producer-version-and-schema-evolution` | full | #168 |
| `broad-property-and-corruption-corpus` | deep | #169 |
