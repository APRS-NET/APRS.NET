# Public API tracking

The libraries in `src/` use [Microsoft.CodeAnalysis.PublicApiAnalyzers](https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/PublicApiAnalyzers/PublicApiAnalyzers.Help.md).
Every public type and member is listed in two files next to each project:

| File | Holds |
| --- | --- |
| `PublicAPI.Shipped.txt` | API that has been released as stable. Empty until 1.0.0. |
| `PublicAPI.Unshipped.txt` | API added since the last stable release. During the alpha, all of it. |

The build fails (`RS0016`) when code adds a public API that is not listed, and (`RS0017`) when a listed API disappears.
That makes every change to the public surface show up in review as a diff of these files, so nothing becomes public by
accident.

## When the build fails with RS0016

If the new API is intended, add it to `PublicAPI.Unshipped.txt`. Use the IDE quick fix ("Add to public API"), or:

```sh
dotnet format analyzers src/<Project>/<Project>.csproj --diagnostics RS0016 --severity warn
```

If it is not intended, make the type or member `internal` instead.

## Releases

During the `0.x.y-alpha.N` alpha everything stays in `PublicAPI.Unshipped.txt`: the API is not stable yet. When
preparing 1.0.0, move the entries to `PublicAPI.Shipped.txt` and set `PackageValidationBaselineVersion` (see
[versioning.md](versioning.md)). From then on, removing or changing a shipped API is a visible, deliberate breaking
change.
