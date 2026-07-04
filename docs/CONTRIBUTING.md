# Contributing to Octadock

Thanks for your interest in Octadock. This guide covers the coding standards, how
to build and test, and the branch/PR/commit conventions.

## Prerequisites

- **.NET 8 SDK** (`8.0.x`; pinned in [`global.json`](../global.json)).
- **Windows 10 version 2004+ / Windows 11** to build and run the WPF/Windows
  projects (`Octadock.App`, `Octadock.Platform.Windows`, `Octadock.Cli`).
- **Visual Studio 2022 (17.10+)** with the *.NET desktop development* workload is
  recommended.
- The cross-platform projects (`Octadock.Core`, `Octadock.Data`) and their tests
  build on any OS, so most Core/Data work can be done and tested on Linux/macOS.

## Building and running

```powershell
# Restore + build + test (Debug by default).
./build/build.ps1

# Release, and optionally produce packages.
./build/build.ps1 -Configuration Release
./build/build.ps1 -Configuration Release -Pack

# Or directly:
dotnet build Octadock.sln -c Release
dotnet run --project src/Octadock.App/Octadock.App.csproj
```

On a non-Windows machine, build only the cross-platform projects:

```bash
./build/build.sh          # builds + tests Core and Data only
```

## Running tests

Tests are **required for changes to `Octadock.Core` and `Octadock.Data`.** New
behavior in those libraries must come with unit tests, and bug fixes should add a
regression test.

```powershell
# Run all cross-platform tests.
dotnet test Octadock.sln

# Run one project.
dotnet test tests/Octadock.Core.Tests/Octadock.Core.Tests.csproj

# With coverage (writes results + Cobertura under artifacts/test-results/).
./build/test.ps1
```

The test stack is **xUnit** with **FluentAssertions**, **NSubstitute** for fakes,
and **coverlet** for coverage. Prefer fast, deterministic, isolated tests: use the
in-memory/fixture fakes for stores and platform services rather than real Windows
APIs. Windows-only behavior that cannot be unit-tested is verified manually — see
[TESTING.md](TESTING.md).

## Coding standards

Settings are enforced by [`.editorconfig`](../.editorconfig),
[`Directory.Build.props`](../Directory.Build.props), and the .NET analyzers
(`EnableNETAnalyzers`, `AnalysisLevel = latest-recommended`,
`EnforceCodeStyleInBuild`). Highlights:

- **Language & nullability** — C# 12, `Nullable` enabled, `ImplicitUsings`
  enabled, invariant globalization.
- **File-scoped namespaces** (`namespace Foo;`) — required
  (`csharp_style_namespace_declarations = file_scoped`).
- **`using` directives** are placed **outside** the namespace and `System.*` is
  sorted first.
- **`var`** only when the type is apparent; otherwise use explicit types.
- **Braces are required** for all control blocks, and opening braces go on a new
  line (Allman style).
- **Naming** — `_camelCase` for private fields, `PascalCase` for constants and
  types, `IPascalCase` for interfaces.
- **Expression-bodied members** are encouraged for single-line properties,
  accessors, and methods.
- **Modern C#** — prefer pattern matching, switch expressions, and simple `using`
  statements.
- **XML docs** — the public surface should be documented; a comment on every
  trivial member is not required (`CS1591` is suppressed).
- **Determinism** — the build is deterministic and produces reference assemblies;
  keep it that way.

Warnings do not fail local builds, but **CI treats warnings as errors**
(`ContinuousIntegrationBuild=true` sets `TreatWarningsAsErrors=true`), so fix
analyzer and style warnings before opening a PR. You can auto-format with:

```powershell
dotnet format Octadock.sln
```

### Package management

The solution uses **Central Package Management**. Declare every NuGet version once
in [`Directory.Packages.props`](../Directory.Packages.props) and reference packages
from a `.csproj` with `<PackageReference Include="..." />` (no `Version`
attribute). New dependencies must be permissively licensed (MIT/Apache/BSD or
similar).

### Architecture boundaries

- Keep `Octadock.Core` free of Windows/WPF/WinRT dependencies so it stays
  cross-platform and testable.
- Put Win32/WinRT/WPF code in `Octadock.Platform.Windows` or `Octadock.App`,
  behind the interfaces declared in Core.
- Do not introduce third-party branding, assets, or proprietary file/endpoint
  formats — Octadock is an original, clean-room implementation.

## Branches, commits, and pull requests

### Branches

Branch off `main` with a short, prefixed name:

- `feature/<short-description>` — new functionality.
- `fix/<short-description>` — bug fixes.
- `docs/<short-description>` — documentation only.
- `chore/<short-description>` — build, CI, tooling.

### Commit messages

Use [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<optional scope>): <summary in imperative mood>

<optional body explaining what and why>
```

Common types: `feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`,
`chore`. Examples:

```
feat(cli): forward octadock:// URIs to the running instance
fix(core): reject area rectangles with non-integer coordinates
docs(automation): document exit codes and --json output
```

Keep commits focused; separate refactors from behavior changes where practical.

### Pull requests

1. Open a PR against `main` with a clear description of the change and the
   motivation. Link any related spec section in [`docs/specs`](specs/).
2. Ensure the build is green: `dotnet build -c Release` (warnings-as-errors) and
   `dotnet test` pass. Run `dotnet format --verify-no-changes` for the
   cross-platform projects.
3. Include tests for Core/Data changes and note any manual Windows verification you
   performed for platform/UI changes.
4. Update [`CHANGELOG.md`](../CHANGELOG.md) under `## [Unreleased]` when the change
   is user-visible.
5. Keep PRs reasonably small and reviewable.

By contributing you agree that your contributions are licensed under the project's
[MIT License](../LICENSE).
