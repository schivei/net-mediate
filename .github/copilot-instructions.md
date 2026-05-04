# Copilot Instructions (Project Memory)

## General Guidelines
- Always register and update project memories when durable context emerges (for example: mandatory validation commands, package/versioning conventions, and repository-specific architectural rules) to preserve continuity across sessions.

## Dependency Management
- Keep package versioning strategy in `.csproj` as **major floating wildcard** (`10.*`, `4.*`, etc.) when updating dependencies.
- Keep test framework on **xUnit v3** (`xunit.v3` package).
- For tools in `.config/dotnet-tools.json`, use **exact versions** (wildcards are not supported in the tools manifest).

## Validation
- Validate changes with existing project commands before concluding:
  - `dotnet restore src/NetMediate/NetMediate.csproj`
  - `dotnet build src/NetMediate/NetMediate.csproj --no-restore --configuration Release`
  - `dotnet test tests/NetMediate.Tests/NetMediate.Tests.csproj --configuration Release`

## GitHub Actions
- In this repo's GitHub Actions workflows, declare permissions at the job level rather than globally at the workflow level.
