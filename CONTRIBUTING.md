# Contributing to APRS.NET

Thanks for helping. APRS.NET is an alpha (`0.x.y-alpha.N`), so the API still changes; issues and pull requests that
make the libraries more faithful to real APRS traffic are especially welcome.

## Before you start

- **Bugs:** open an issue with the raw packet line (`SOURCE>DEST,PATH:information`) if a packet is involved, what
  APRS.NET did and what you expected. A line from `aprscli listen` or `aprscli parse` is ideal.
- **Features:** open an issue first for anything larger than a fix, so the design can be agreed before you write it.
  [docs/design/packet-api.md](docs/design/packet-api.md) explains how the packet API is shaped and why.
- **Security problems:** do not open a public issue; use GitHub's private vulnerability reporting on this repository.

## Building and testing

You need the .NET SDK named in `global.json` (10.0).

```sh
dotnet tool restore
dotnet build
dotnet test
git config core.hooksPath .githooks   # once per clone: formats staged files with CSharpier on commit
```

Warnings are errors, analyzers run in the build, and CI fails on unformatted code (`dotnet csharpier check .`).

Tests come in three kinds, and a change usually needs the first and gets checked by the other two:

| Project | What it holds |
| --- | --- |
| `tests/AprsNet.Packets.Tests` and the other `*.Tests` | Unit tests, one behaviour per test, named as a sentence (`RefusesAnAnalogValueAbove999`) |
| `tests/AprsNet.Packets.SpecTests` | Vectors taken from the APRS specifications in `docs/spec` |
| `tests/AprsNet.Packets.CorpusTests` | Every packet in `tests/corpus`: parsing, byte-exact rebuilds, rebuilds from values, damaged input |

The corpus tests are the safety net for real traffic: a parser change that breaks any of the 55,000 captured
packets shows up there.

## Packet corpus

`tests/corpus` holds real APRS-IS traffic, **anonymized**. Raw captures never go into the repository: capture into
`captures/` (git-ignored) with `aprscli listen --capture`, then run `tools/corpus/Anonymize.cs` as described in
[tests/corpus/README.md](tests/corpus/README.md). Its mapping file undoes the anonymization, so it must never be
committed either. Review the free text of a new capture for personal names before adding it, and list the file in
the corpus README's table (a test checks that).

## Public API

Every public type and member in `src/` is listed in `PublicAPI.Unshipped.txt`, and the build fails when that list and
the code disagree. A new public member is a deliberate decision: add it to the file, or make the member `internal`.
See [docs/public-api.md](docs/public-api.md).

## Commits and pull requests

Commit messages and pull request titles follow [Conventional Commits](https://www.conventionalcommits.org), and a
workflow checks the title. The type decides the release, so pick it by what ships:

| Change | Type |
| --- | --- |
| A library in `src/` gains a feature | `feat:` (`feat(packets):`, `feat(aprs-is):`) |
| A library in `src/` gets a fix | `fix:` |
| A breaking library change | `feat!:` or `fix!:` |
| The CLI, tests, tools, docs, CI, benchmarks | `chore:`, `test:`, `docs:`, `ci:`, `build:` |

Only `src/` changes produce a release; [docs/versioning.md](docs/versioning.md) has the details. Explain in the body
why the change is needed and anything a reviewer should check, and keep unrelated changes in separate pull requests.

## Code style

Match the code around your change: its naming, its comment density, and its habits (immutable models, `Try*` methods
that never throw on bad input, spans for parsing). Comments say why, not what. Prefer plain, specific wording in
messages and docs; an exception message says what was wrong and how to fix it.

## AI assistance

The maintainer develops APRS.NET with an AI coding assistant, as [AI-DECLARATION.md](AI-DECLARATION.md) describes.
Contributions made with AI help are welcome on the same terms as any other: you are responsible for understanding,
testing and standing behind what you submit.

## License

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).
