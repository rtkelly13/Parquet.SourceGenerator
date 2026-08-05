# Contributing to Parquet.SourceGenerator

Thank you for your interest in contributing to **Parquet.SourceGenerator**! This guide outlines development setup, building, testing, benchmarking, and submitting pull requests.

---

## 🛠️ Development Setup

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher
- Python 3.11+ with [`uv`](https://github.com/astral-sh/uv) (for PyArrow cross-engine test dataset generation)

### Clone
```bash
git clone https://github.com/rtkelly13/Parquet.SourceGenerator.git
cd Parquet.SourceGenerator
```

> The solution file is named `Parquet.SourceGenertor.sln` — the typo is historical and kept so
> existing clones keep working.

---

## 🧪 Building & Testing

### 1. Build Solution
```bash
dotnet build Parquet.SourceGenertor.sln --configuration Release
```

### 2. Run Test Suite
```bash
dotnet test Parquet.SourceGenertor.sln --configuration Release
```

### 3. Run Test Suite with Code Coverage
```bash
dotnet test --collect:"XPlat Code Coverage" Parquet.SourceGenertor.sln
```

### 4. Run the AOT sample
```bash
dotnet run --project samples/Parquet.SourceGenerator.SampleAot/Parquet.SourceGenerator.SampleAot.csproj --configuration Release
```

This exercises the generated code, but it does **not** verify Native AOT: `PublishAot` has no
effect on `dotnet run`, which executes under CoreCLR.

### 5. Verify Native AOT properly

Putting the AOT compiler through it needs a publish with a runtime identifier, plus the native
toolchain (`clang`, `zlib`) on your machine:

```bash
dotnet publish test/Parquet.SourceGenerator.AotTest/Parquet.SourceGenerator.AotTest.csproj \
  --configuration Release -r linux-x64 -o ./aot-out
./aot-out/Parquet.SourceGenerator.AotTest
```

`AotTest` round-trips a Parquet stream through the generated serializer and throws on mismatch, so
the native binary exiting `0` is the actual result. CI runs exactly this on every pull request, for
`linux-x64`; other runtime identifiers are untested.

Expect `IL2104` and `IL3053` warnings against `Parquet.dll` during the publish. They come from
Parquet.Net, not from generated code, and are not currently treated as errors — the generated code
is reflection-free but the library beneath it is not. If you add a step that trips new IL warnings
attributed to *this* repo's assemblies, that is a real regression worth chasing.

---

## 📊 Running Benchmarks

Benchmarks are not run in CI (BenchmarkDotNet on shared runners is too noisy to gate merges on)
and no baseline results have been committed, so there is nothing to compare against yet. Run them
locally when working on performance:

```bash
dotnet run -c Release --project benchmarks/Parquet.SourceGenerator.Benchmarks/Parquet.SourceGenerator.Benchmarks.csproj -- --filter "*"
```

If you publish numbers anywhere, include the machine and runtime they came from.

---

## 📥 Submitting Pull Requests

1. **Create a Feature Branch**: `git checkout -b feat/your-feature-name`
2. **Code Style**: Ensure code adheres to `.editorconfig` rules (`dotnet format --verify-no-changes`).
3. **Tests**: The suite must pass cleanly (`dotnet test`).
4. **PR Title**: Must follow Conventional Commits — CI validates it (`feat:`, `fix:`, `perf:`,
   `docs:`, `style:`, `refactor:`, `test:`, `chore:`, `ci:`, `build:`).
5. **Commit Message**: Use Conventional Commit messages too.

---

## 🚀 Releasing

The pushed tag is the version. Nothing else needs editing — in particular, do **not** bump
`<Version>` in `Directory.Build.props`; that value is a development default and the release
workflow overrides it with `-p:Version`.

```bash
git tag v0.1.0
git push origin v0.1.0
```

`release.yml` then derives `0.1.0` from the tag, rebuilds and tests from that commit, packs, checks
the package layout, and pushes to NuGet.org. A tag that does not yield a valid `MAJOR.MINOR.PATCH`
(optionally with a pre-release suffix) fails the run before anything is built.

Two things worth knowing:

- **NuGet versions are permanent.** A version cannot be re-uploaded or replaced, only deprecated or
  unlisted. That is why the workflow verifies the package layout before pushing rather than relying
  on the pull request run — and why publishing is worth being deliberate about.
- Only tag a commit whose CI is green. The workflow re-runs the build and tests, but a tag is the
  wrong place to discover a failure.

---

## 📄 License
By contributing to **Parquet.SourceGenerator**, you agree that your contributions will be licensed under the [MIT License](LICENSE).
