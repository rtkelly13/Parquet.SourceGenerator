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
effect on `dotnet run`, which executes under CoreCLR. To actually put the AOT compiler through it
you need a publish with a runtime identifier, plus the native toolchain (`clang`, `zlib`) on your
machine:

```bash
dotnet publish samples/Parquet.SourceGenerator.SampleAot/Parquet.SourceGenerator.SampleAot.csproj \
  --configuration Release -r linux-x64
```

CI does not run this yet, so AOT compatibility is currently unverified.

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

## 📄 License
By contributing to **Parquet.SourceGenerator**, you agree that your contributions will be licensed under the [MIT License](LICENSE).
