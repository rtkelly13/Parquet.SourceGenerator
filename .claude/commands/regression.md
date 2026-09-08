---
description: Run the compatibility, conformance and fixture-integrity suite (quick | full | deep)
argument-hint: "[quick|full|deep]"
allowed-tools: Bash(dotnet run --project tools/RegressionRunner/*), Bash(dotnet run --project tools/RegressionRunner:*), Read
---

Run the repository regression suite in the tier named by `$ARGUMENTS` (default `quick`):

```bash
dotnet run --project tools/RegressionRunner/RegressionRunner.csproj --configuration Release -- $ARGUMENTS
```

Tiers:

- `quick` (default) — deterministic generated round trips plus checked-in fixture reads. No
  external tools, budget 5 minutes. This is the tier for the ordinary development loop.
- `full` — quick plus pinned PyArrow generation and verification, DuckDB bidirectional interop,
  the regenerated v1/v2 version matrix and fixture-integrity hashing. Needs `uv` and the DuckDB
  CLI (`DUCKDB_BIN` overrides the lookup).
- `deep` — full plus broad seeded property coverage, corrupted-input handling, large multi
  row-group datasets, IL boxing interrogation and a Native AOT publish-and-execute.

Useful flags: `--dry-run` (print the plan and the prerequisite report, run nothing),
`--keep-going`, `--allow-missing-tools`, `--enforce-budget`, `--seeds N`, `--large-rows N`,
`--artifacts <dir>`.

After the run, report the outcome line, the artifact directory, and — for any failure — the
reproduction command the runner printed. The full record is `run-manifest.json` and `summary.md`
in the artifact directory (`temp/regression/<run-id>/` by default). Do not edit anything under
`test/data`: the runner hashes that tree before and after the run and fails if it changed.
