# 04 - Roadmap & Open Source Blueprint

## 🗺️ Project Roadmap

### Phase 1: Foundation & Infrastructure (Current Phase)
- [x] Fix SDK rollForward policy in `global.json`.
- [x] Establish project documentation & architectural blueprints in `docs/`.
- [x] Configure gitignore rules for local todo management.
- [ ] Upgrade `Parquet.SourceGenerator.CLI` target framework to `net8.0` / `net9.0`.
- [ ] Update `Parquet.Net` library dependency to latest stable release (`4.x` / `5.x`).
- [ ] Implement attribute marker definitions (`ParquetSerializableAttribute`, `ParquetColumnAttribute`, `ParquetIgnoreAttribute`).

### Phase 2: Primitive Types & Basic Code Generation
- [ ] Implement `ParquetIncrementalGenerator` Roslyn 4.0 pipeline.
- [ ] Generate static `ParquetSchema` from decorated target types.
- [ ] Generate zero-reflection `WriteParquetAsync` column array exporter for primitives (`int`, `long`, `double`, `float`, `bool`, `string`, `DateTime`, `Guid`).
- [ ] Generate `ReadParquetAsync` column array importer for primitive types.
- [ ] Add unit test suite with `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing` and `Verify.SourceGenerators`.

### Phase 3: Nullability, Complex Types & Diagnostics
- [ ] Support nullable primitives (`int?`, `DateTime?`, `double?`).
- [ ] Implement Roslyn compiler diagnostics (`PARQ001` - `PARQ099`) for invalid attributes or unsupported types.
- [ ] Support custom decimal precision/scale (`[ParquetDecimal]`) and timestamp units (`[ParquetTimestamp]`).
- [ ] Support nested collections (`List<T>`, arrays) and nested POCO structs (`StructField`).

### Phase 4: Native AOT, Performance Optimization & Benchmarking
- [ ] Create `Parquet.SourceGenerator.Benchmarks` project using `BenchmarkDotNet`.
- [ ] Benchmark serialization & deserialization throughput against `ParquetConvert` reflection implementation.
- [ ] Validate `PublishAot=true` compatibility with zero-trimming warnings.
- [ ] Optimize memory allocations using `ArrayPool<T>` and ref struct column buffers where applicable.

### Phase 5: Open Source Launch & CI/CD
- [ ] Setup GitHub Actions workflow (`.github/workflows/ci.yml`) for automated building, testing, and formatting.
- [ ] Configure NuGet package metadata, licensing (MIT), icons, and README embedded docs.
- [ ] Publish initial v1.0.0 release to NuGet.org.

---

## 🧪 Testing Strategy

Quality and correctness are verified across three testing tiers:

### 1. Source Generator Unit Tests (`Verify.SourceGenerators`)
Inspects generated C# code strings against verified snapshot outputs to prevent regressions when refactoring Roslyn code.

```csharp
[Fact]
public Task GeneratesCorrectSerializerForBasicPoco()
{
    string source = """
        using Parquet.SourceGenerator;
        
        [ParquetSerializable]
        public record Person(int Id, string Name);
        """;

    return TestHelpers.VerifyGenerator(source);
}
```

### 2. Integration Tests (`Parquet.Net` Compatibility)
Generates actual Parquet byte streams, writes them to disk/memory, reads them back using both `ParquetReader` and generated extensions, and asserts object field equality.

### 3. Native AOT Verification
Builds an AOT test console app with `<PublishAot>true</PublishAot>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` to guarantee trim safety.

---

## 🤝 Contributing Guidelines

1. **Code Style**: Adhere to modern C# 12/13 conventions, file-scoped namespaces, nullable reference types (`#nullable enable`), and pattern matching.
2. **Roslyn Performance**: Never hold raw `ISymbol` or `SyntaxNode` references in incremental generator pipeline state. Always pass value-equatable record models.
3. **Commit Workflow**: Run `dotnet test` locally before submitting pull requests. Ensure all snapshot tests pass.
