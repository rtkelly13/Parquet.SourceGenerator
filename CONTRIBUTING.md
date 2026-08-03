# Contributing to Parquet.SourceGenerator

Thank you for your interest in contributing to **Parquet.SourceGenerator**! This guide outlines development setup, building, testing, benchmarking, and submitting pull requests.

---

## 🛠️ Development Setup

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher
- Python 3.11+ with [`uv`](https://github.com/astral-sh/uv) (for PyArrow cross-engine test dataset generation)

### Cloned Repository Structure
```bash
git clone https://github.com/ryankelly/Parquet.SourceGenerator.git
cd Parquet.SourceGenerator
```

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

### 4. Verify Native AOT Compilation
```bash
dotnet run --project samples/Parquet.SourceGenerator.SampleAot/Parquet.SourceGenerator.SampleAot.csproj --configuration Release
```

---

## 📊 Running Benchmarks

Performance is a top priority. Always run benchmarks before and after submitting changes to avoid performance regressions:

```bash
dotnet run -c Release --project benchmarks/Parquet.SourceGenerator.Benchmarks/Parquet.SourceGenerator.Benchmarks.csproj -- --filter "*"
```

---

## 📥 Submitting Pull Requests

1. **Create a Feature Branch**: `git checkout -b feat/your-feature-name`
2. **Code Style**: Ensure code adheres to `.editorconfig` rules (`dotnet format --verify-no-changes`).
3. **Tests & Coverage**: All 20+ unit tests must pass cleanly (`dotnet test`).
4. **Benchmarking**: Ensure no performance or memory allocation regressions.
5. **Commit Message**: Use Conventional Commit messages (`feat:`, `fix:`, `perf:`, `docs:`, `ci:`).

---

## 📄 License
By contributing to **Parquet.SourceGenerator**, you agree that your contributions will be licensed under the [MIT License](LICENSE).
