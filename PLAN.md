# NetMediate Single-PR Work Plan

This checklist tracks the work implemented sequentially in this same PR.

## Completed

- [x] Align namespace resolution with GenDI strategy (per-compilation assembly name; remove shared static namespace state).
- [x] Bundle `GenDI.SourceGenerator.dll` into the `NetMediate` package (`analyzers/dotnet/cs`).
- [x] Add `buildTransitive/NetMediate.props` to propagate analyzers for transitive consumers and reduce required user actions.
- [x] Update source-generation documentation for friendly `dotnet add package NetMediate` usage (no manual `PrivateAssets` requirement for direct references).
- [x] Validate package output includes:
  - [x] `analyzers/dotnet/cs/NetMediate.SourceGeneration.dll`
  - [x] `analyzers/dotnet/cs/GenDI.SourceGenerator.dll`
  - [x] `buildTransitive/NetMediate.props`

## Current

- [x] Add this plan file in English at project root so progress/accomplishment can be followed.
- [ ] MUST solve `AddNetMediate` compile resolution failure seen in source-generation tests before final merge.

## Notes

- Scope is intentionally kept in a **single PR**, with items implemented in order.
- Latest baseline run in this branch:
  - `dotnet restore src/NetMediate/NetMediate.csproj` ✅
  - `dotnet build src/NetMediate/NetMediate.csproj --no-restore --configuration Release` ✅
  - `dotnet test tests/NetMediate.SourceGeneration.Tests/NetMediate.SourceGeneration.Tests.csproj --configuration Release` ⚠️ blocked by `AddNetMediate` compile resolution issue in `GeneratorIntegrationTests.cs` (tracked above as MUST-solve).
