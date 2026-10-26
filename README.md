# APRS.NET

[![CI](https://github.com/APRS-NET/APRS.NET/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/APRS-NET/APRS.NET/actions/workflows/ci.yml)
[![Packages](https://github.com/APRS-NET/APRS.NET/actions/workflows/packages.yml/badge.svg?branch=main)](https://github.com/APRS-NET/APRS.NET/actions/workflows/packages.yml)
[![Line coverage](https://raw.githubusercontent.com/APRS-NET/APRS.NET/badges/badge_linecoverage.svg)](#code-coverage)
[![AI-DECLARATION: copilot](https://img.shields.io/badge/䷼%20AI--DECLARATION-copilot-fee2e2?labelColor=fee2e2)](https://ai-declaration.md)

An async-first, dependency-light .NET library family for APRS: parse and build packets, connect to APRS-IS, and
bridge radios and clients through KISS, APRS-IS and WebSocket services.

Status: alpha, on the 0.4.0 line. Parsing and building every common APRS packet type, station analysis (telemetry
and track anomalies), the APRS-IS client with verified sending, messaging, TLS and automatic reconnects, AX.25 and KISS
for receiving and transmitting through a TNC, a local APRS-IS gateway that serves radio and internet traffic together
to APRS-IS and WebSocket clients, and the `aprscli` tool are all published.

> [!WARNING]
> **Alpha software.** Every APRS.NET package is an alpha pre-release (`0.x.y-alpha.N`) until at least v1.0.0. APIs,
> behaviour and package names can change between any two releases without notice, and there is no stability or support
> guarantee. Do not depend on it in production.

## Install

Packages are published to nuget.org as pre-releases, so pass `--prerelease`:

```sh
dotnet add package AprsNet.Packets --prerelease
dotnet add package AprsNet.Packets.Json --prerelease
dotnet add package AprsNet.AprsIsTcpClient --prerelease
dotnet add package AprsNet.Symbols --prerelease
dotnet add package AprsNet.DeviceId --prerelease
dotnet add package AprsNet.Filters --prerelease
dotnet add package AprsNet.Ax25 --prerelease
dotnet add package AprsNet.Kiss --prerelease
dotnet add package AprsNet.Kiss.Serial --prerelease
dotnet add package AprsNet.Messaging --prerelease
dotnet add package AprsNet.Beacons --prerelease
dotnet add package AprsNet.Gps --prerelease
dotnet add package AprsNet.IGate --prerelease
dotnet add package AprsNet.Corpus --prerelease
dotnet add package AprsNet.Gateway --prerelease
dotnet add package AprsNet.Extensions.DependencyInjection --prerelease
```

The `aprscli` command-line tool is a .NET tool:

```sh
dotnet tool install --global AprsNet.AprsIsCli --prerelease
```

Each [GitHub release](https://github.com/APRS-NET/APRS.NET/releases) also carries the same packages and a CycloneDX
SBOM of them, and is marked as a pre-release. See [docs/versioning.md](docs/versioning.md) for how versions are chosen.

## Guides

- [Getting started with a TNC](docs/guides/tnc.md): read and send packets through Dire Wolf or a serial TNC.
- [Getting started with the gateway](docs/guides/gateway.md): serve APRS-IS and your TNCs to APRS-IS clients.
- [Run the gateway in a container](docs/guides/container.md): the `aprscli` image, and a compose sample with Dire Wolf.
- [Run as Linux services](apps/services/README.md): share a serial TNC and serve a gateway with systemd, set up in TOML
  files, with an installer and scripts to check them and read their logs.

## Layout

| Path                          | Contents                                                                  |
|-------------------------------|---------------------------------------------------------------------------|
| `src/AprsNet.Packets`         | Packet models, parser, encoder (no I/O)                                   |
| `src/AprsNet.Packets.Json`    | Packets as documented JSON objects, for web apps, logs and pipes |
| `src/AprsNet.AprsIsTcpClient` | APRS-IS client                                                            |
| `src/AprsNet.Symbols`         | Symbol meanings and SVG symbol images ([conventional](docs/symbols/conventional.md), [modern](docs/symbols/modern.md)) |
| `src/AprsNet.Filters`         | APRS-IS filters: build them from typed terms, and evaluate them locally   |
| `src/AprsNet.DeviceId`        | Device and program identification from the aprs-deviceid list (code MIT, data CC BY-SA 2.0), and fetching a newer list |
| `src/AprsNet.Ax25`            | AX.25 UI frames: decode and encode what a TNC sends and receives, and the digipeater rules |
| `src/AprsNet.Kiss`            | KISS TNCs over TCP or any stream, with reconnects, a transmit limit, every port of a multi-port TNC, and a KISS TCP server |
| `src/AprsNet.Kiss.Serial`     | KISS TNCs on a serial port, reopened after an unplug, and the ports present |
| `src/AprsNet.Messaging`       | Message conversations: IDs, retries until an ack, reply-acks, each message delivered once |
| `src/AprsNet.Beacons`         | Beacon schedules: when to beacon next and why; SmartBeaconing, dead reckoning |
| `src/AprsNet.Gps`             | A GPS through gpsd, on this machine or another, with reconnects           |
| `src/AprsNet.IGate`           | IGate rules both ways: what goes from RF to APRS-IS, which messages go back to stations heard on RF, and why not |
| `src/AprsNet.Corpus`          | Packet captures for a test corpus: the line format, and an anonymizer     |
| `src/AprsNet.Gateway`         | A local APRS-IS server fed by APRS-IS and KISS TNCs                       |
| `src/AprsNet.Extensions.DependencyInjection` | Register an APRS-IS connection from configuration          |
| `apps/AprsNet.AprsIsCli`      | The `aprscli` command-line tool                                           |
| `apps/services`               | systemd services for `aprscli`: units, example configs, scripts         |
| `tests/`                      | Unit tests and the packet corpus                                          |

## Build

Contributions are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md).

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

The `packages` workflow (`.github/workflows/packages.yml`) runs when a change can affect dependencies (a project
file, `Directory.Packages.props`, `Directory.Build.props`, `global.json` or the tool manifest) and every Monday. It lists vulnerable NuGet packages (direct and transitive) and available updates for direct packages, posts
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
