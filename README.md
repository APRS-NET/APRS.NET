# APRS.NET

[![CI](https://github.com/APRS-NET/APRS.NET/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/APRS-NET/APRS.NET/actions/workflows/ci.yml)
[![Packages](https://github.com/APRS-NET/APRS.NET/actions/workflows/packages.yml/badge.svg?branch=main)](https://github.com/APRS-NET/APRS.NET/actions/workflows/packages.yml)
[![Line coverage](https://raw.githubusercontent.com/APRS-NET/APRS.NET/badges/badge_linecoverage.svg)](#code-coverage)

An async-first, dependency-light .NET library family for APRS: parse and build packets, connect to APRS-IS, and later
bridge radios and clients through KISS, TCP and WebSocket services.

Status: early scaffolding (M0).

## Layout

| Path                          | Contents                                |
|-------------------------------|-----------------------------------------|
| `src/AprsNet.Packets`         | Packet models, parser, encoder (no I/O) |
| `src/AprsNet.AprsIsTcpClient` | APRS-IS client                          |
| `apps/AprsNet.AprsIsCli`      | The `aprscli` dotnet tool               |
| `tests/`                      | Unit tests and the packet corpus        |

## Build

```sh
dotnet build
dotnet test
```

## Code coverage

The CI `coverage` job fails if the merged line or branch coverage of the libraries and `aprscli` drops below
`MIN_LINE` / `MIN_BRANCH` in `.github/workflows/ci.yml` (currently 85% line, 80% branch), and posts the coverage
table on the run's summary page. On `main` it also updates the badges on the `badges` branch
(`badge_linecoverage.svg`, `badge_branchcoverage.svg`). To see the same numbers locally:

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

Licensed under the [MIT License](LICENSE).
