# NetMediate.SourceGeneration

Reference this package in the application's startup/main project with:

```xml
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x.x">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

`NetMediate.SourceGeneration` is the package that runs the NetMediate source generator directly.

## Indirect but required dependencies

When you install this package, its `buildTransitive` file adds these required `PackageReference` entries automatically:

- `NetMediate` — runtime implementation used by the generated registrations
- `GenDI.SourceGenerator` — generator that emits the DI builder APIs used by NetMediate

You normally do **not** add those packages manually in the same startup project. They are indirect dependencies, but they are still required for the generated experience to work.

## Contracts-only projects

If you have a shared contracts project, reference `NetMediate.Core` there:

```bash
dotnet add package NetMediate.Core
```

Then reference `NetMediate.SourceGeneration` only in the executable/startup project that calls `AddNetMediate()`.
