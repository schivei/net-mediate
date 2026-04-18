# Copilot Instructions (Project Memory)

- Keep package versioning strategy in `.csproj` as **major floating wildcard** (`10.*`, `4.*`, etc.) when updating dependencies.
- Keep test framework on **xUnit v3** (`xunit.v3` package).
- For tools in `.config/dotnet-tools.json`, use **exact versions** (wildcards are not supported in the tools manifest).
- Validate changes with existing project commands before concluding:
  - `dotnet restore src/NetMediate/NetMediate.csproj`
  - `dotnet build src/NetMediate/NetMediate.csproj --no-restore --configuration Release`
  - `dotnet test tests/NetMediate.Tests/NetMediate.Tests.csproj --configuration Release`
