# APRS.NET

[![CI](https://github.com/APRS-NET/APRS.NET/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/APRS-NET/APRS.NET/actions/workflows/ci.yml)
[![Packages](https://github.com/APRS-NET/APRS.NET/actions/workflows/packages.yml/badge.svg?branch=main)](https://github.com/APRS-NET/APRS.NET/actions/workflows/packages.yml)
[![Line coverage](https://raw.githubusercontent.com/APRS-NET/APRS.NET/badges/badge_linecoverage.svg)](#code-coverage)
[![AI-DECLARATION: pair](https://img.shields.io/badge/䷼%20AI--DECLARATION-pair-ffedd5?labelColor=ffedd5)](https://ai-declaration.md)

An async-first, dependency-light .NET library family for APRS: parse and build packets, connect to APRS-IS, and later
bridge radios and clients through KISS, TCP and WebSocket services.

Status: early scaffolding (M0).

> [!WARNING]
> **Alpha software.** Every APRS.NET package is an alpha pre-release (`0.x.y-alpha.N`) until at least v1.0.0. APIs,
> behaviour and package names can change between any two releases without notice, and there is no stability or support
> guarantee. Do not depend on it in production.

## Install

Packages are published to nuget.org as pre-releases, so pass `--prerelease`:

```sh
dotnet add package AprsNet.Packets --prerelease
dotnet add package AprsNet.AprsIsTcpClient --prerelease
dotnet add package AprsNet.Symbols --prerelease
```

The `aprscli` command-line tool is a .NET tool:

```sh
dotnet tool install --global AprsNet.AprsIsCli --prerelease
```

Each [GitHub release](https://github.com/APRS-NET/APRS.NET/releases) also carries the same packages, and is marked as a
pre-release. See [docs/versioning.md](docs/versioning.md) for how versions are chosen.

## Layout

| Path                          | Contents                                                                  |
|-------------------------------|---------------------------------------------------------------------------|
| `src/AprsNet.Packets`         | Packet models, parser, encoder (no I/O)                                   |
| `src/AprsNet.AprsIsTcpClient` | APRS-IS client                                                            |
| `src/AprsNet.Symbols`         | Symbol meanings and SVG symbol images ([conventional](docs/symbols/conventional.md), [modern](docs/symbols/modern.md)) |
| `apps/AprsNet.AprsIsCli`      | The `aprscli` command-line tool                                           |
| `tests/`                      | Unit tests and the packet corpus                                          |

## Build

```sh
dotnet tool restore
dotnet build
dotnet test
```

Code is formatted with [CSharpier](https://csharpier.com), and CI fails on unformatted files. Enable the pre-commit hook
once per clone so staged files are formatted when you commit:

```sh
git config core.hooksPath .githooks
```

## Code coverage

The CI `coverage` job fails if the merged line or branch coverage of the libraries and `aprscli` drops below
`MIN_LINE` / `MIN_BRANCH` in `.github/workflows/ci.yml` (currently 85% line, 80% branch), and posts the coverage
table on the run's summary page. On `main` it also updates the badges on the `badges` branch (`badge_linecoverage.svg`,
`badge_branchcoverage.svg`). To see the same numbers locally:

```sh
dotnet tool restore
dotnet test --collect:"XPlat Code Coverage" --results-directory coverage
dotnet reportgenerator -reports:"coverage/**/coverage.cobertura.xml" -targetdir:coverage/report \
  -reporttypes:"Html;TextSummary" -assemblyfilters:"+AprsNet.*;+aprscli;-*.Tests;-*.SpecTests;-*.CorpusTests"
```

Then open `coverage/report/index.html`. Raise the thresholds when coverage improves; do not lower them to get a
change through.

## Package checks

The `packages` workflow (`.github/workflows/packages.yml`) runs on every push to `main`, on pull requests and every
Monday. It lists vulnerable NuGet packages (direct and transitive) and available updates for direct packages, posts
the report to the run summary, keeps a single report comment up to date on pull requests from this repository, and
fails if any package has a known vulnerability. To run the same check locally:

```sh
dotnet restore -p:NuGetAudit=false
dotnet list AprsNet.slnx package --vulnerable --include-transitive --format json > vulnerable.json
dotnet list AprsNet.slnx package --outdated --format json > outdated.json
dotnet run tools/ci/PackageReport.cs -- --vulnerable vulnerable.json --outdated outdated.json --out package-report.md
```

## AI assistance

APRS.NET is developed with an AI coding assistant (Claude Code) working alongside the maintainer, who understands,
reviews and commits every change. See [AI-DECLARATION.md](AI-DECLARATION.md).

Licensed under the [MIT License](LICENSE).
