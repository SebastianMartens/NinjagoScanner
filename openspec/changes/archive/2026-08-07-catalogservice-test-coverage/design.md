## Context

See proposal.md - Why. No test project exists anywhere in this repo yet (checked all three projects), so this change also establishes the first testing convention for the solution.

`CatalogRepository` (`NinjagoScanner.CatalogService/Catalog/CatalogRepository.cs`) is a `sealed partial class` with a primary constructor taking `ILogger<CatalogRepository>`, `IWebHostEnvironment`, `IConfiguration`. It reads JSON files directly from disk (`File.ReadAllText`, `Directory.EnumerateFiles`, `File.GetLastWriteTimeUtc`) - there is no `IFileSystem` abstraction. All parsing/normalization/sorting helper methods (`LoadSeriesDetails`, `ExtractSeriesCards`, `EnumerateCardEntries`, `NormalizeCardNumber`, `ToSortKey`, `NormalizeLookupKey`, `BuildSeriesList`, etc.) are `private static`. `CardCatalogGrpcService` depends only on `CatalogRepository` (concrete class, not an interface) and `ServerCallContext`.

## Goals / Non-Goals

**Goals:**
- Get meaningful coverage of `CatalogRepository`'s parsing, normalization, sorting, dedup, merge, and caching logic, and of `CardCatalogGrpcService`'s RPC mapping, using only the existing public API surface.
- Establish a reusable fixture pattern (temp directory + JSON files) that later tests (e.g. for the multi-language data-model rework) can extend.
- Keep the change purely additive: zero edits to production code.

**Non-Goals:**
- Refactoring `CatalogRepository` to extract interfaces (`IFileSystem`, `IClock`, etc.) for stricter unit isolation. The private static helpers are pure functions of file content, so black-box tests through `GetSnapshot()`/`FindByName()`/`FindSeriesMetadata()` give equivalent coverage without touching production code.
- Testing `Program.cs` startup/DI wiring or running the service end-to-end over a real gRPC channel.
- Achieving a specific coverage percentage or wiring coverage gates into CI - this change only adds the tests themselves.
- Implementing the multi-language/card-identity data model rework itself (tracked separately, per the TODO in `openspec/specs/catalog-service-card-catalog/spec.md`).

## Decisions

**Test framework: xUnit.** No existing convention in the repo to follow. xUnit is the de facto default for new .NET projects (used by the ASP.NET Core / .NET SDK teams themselves), has first-class `dotnet test` support, and integrates cleanly with `Microsoft.NET.Test.Sdk`. Alternatives considered: NUnit (equally viable, slightly more setup ceremony via `[TestFixture]`/`[SetUp]`) and MSTest (weaker data-driven test ergonomics). No strong reason to deviate from the .NET default.

**Mocking: Moq**, for the two dependencies that need faking (`ILogger<CatalogRepository>` via `NullLogger<T>` where possible, no strict need to mock; `IWebHostEnvironment` needs a stub for `ContentRootPath`). `IConfiguration` is built with `ConfigurationBuilder().AddInMemoryCollection(...)` rather than mocked, since that exercises the real config-binding path (`configuration["Catalog:Directory"]`). Alternatives considered: NSubstitute (equally fine, Moq chosen only for ubiquity/familiarity - no repo precedent either way).

**Fixture strategy: real temp directories with real JSON files, not in-memory fakes.** `CatalogRepository` reads from disk directly, so the fastest path to correct coverage without modifying production code is: each test (or a shared fixture base class) creates a temp directory under `Path.GetTempPath()`, writes `series.json` / `series_*.json` fixture content into it, points `IConfiguration["Catalog:Directory"]` at that directory, constructs `CatalogRepository`, and cleans up the directory afterward (`IDisposable` test fixture / `IAsyncLifetime`). This is black-box testing of the documented behavior (matches the existing `openspec/specs/catalog-service-*` requirements) and stays valid across the planned data-model rework since it doesn't depend on internal method signatures.
Alternative considered and rejected: introducing an `IFileSystem` seam (e.g. `System.IO.Abstractions`) to test in-memory. Rejected as out of scope - it's a production code change the proposal explicitly excludes, and real-file fixtures are fast enough (small JSON files, no I/O bottleneck) that the abstraction isn't needed to keep tests fast.

**Timestamp-based cache invalidation tests use explicit `File.SetLastWriteTimeUtc`**, not `Thread.Sleep`, to force a detectable timestamp change deterministically and keep tests fast.

**gRPC service tests construct `CardCatalogGrpcService` directly** (`new CardCatalogGrpcService(repository)`) with a real `CatalogRepository` over fixture data, and call RPC methods directly with a minimal `ServerCallContext` (e.g. via `Grpc.Core.Testing.TestServerCallContext` or simply passing `null!`/a dummy context, since none of the current RPC implementations read from `ServerCallContext`). No test server or real network channel is started.

**Project layout:** `NinjagoScanner.CatalogService.Tests/` sits alongside the three existing projects at the repo root, added to `NinjagoScanner.slnx`. Fixture JSON files live under `NinjagoScanner.CatalogService.Tests/Fixtures/` and are copied to output via `CopyToOutputDirectory` or written directly to temp paths at test time (writing at test time is preferred - avoids output-path coupling and keeps each test's fixture data colocated with its assertions where practical).

## Risks / Trade-offs

- [Real-file-based fixtures are slightly slower than pure in-memory unit tests] → Acceptable: JSON fixtures are small (a handful of series/cards), and this trades a marginal speed cost for zero production-code changes.
- [Tests couple to `CatalogRepository`'s current public API shape, which the planned multi-language rework will change] → Expected and intended: these tests are the safety net for that rework. When the rework lands, failing tests will point precisely at behavior that changed, and tests will be updated alongside it as part of that future change.
- [No coverage tooling/threshold enforced in this change] → Out of scope per Non-Goals; can be added later (e.g. `coverlet.collector` + a CI gate) as a separate change if desired.

## Migration Plan

Purely additive - add the new test project, reference it from the solution, run `dotnet test`. No deployment or rollback concerns; nothing in the runtime services changes.
