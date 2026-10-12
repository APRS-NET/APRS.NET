# Versioning and releases

Versions follow [Semantic Versioning](https://semver.org). All packages share one version and are tagged `vX.Y.Z`.

## How it works

1. Commits to `main` use [Conventional Commits](https://www.conventionalcommits.org). With squash merges the **PR title** is the commit message, so the `pr-title` workflow checks it.
2. `release-please` (the `release` workflow) keeps a release PR open that bumps the version and updates `CHANGELOG.md`.
3. Merging that PR creates the `vX.Y.Z` tag and a GitHub release. The `pack` job then builds the tag, runs the tests, and attaches the `.nupkg` and `.snupkg` files to the release.
4. [MinVer](https://github.com/adamralph/minver) derives each assembly and package version from the nearest tag, so no version number is stored in the repo. Builds between releases look like `0.1.1-alpha.0.3`.

## Commit types

| Prefix | Changelog | Bump (before 1.0) | Bump (1.0 and later) |
| --- | --- | --- | --- |
| `feat:` | Features | minor | minor |
| `fix:`, `perf:` | Bug fixes, Performance | patch | patch |
| `feat!:`, `fix!:` or a `BREAKING CHANGE:` footer | Breaking changes | minor | major |
| `docs:`, `test:`, `chore:`, `ci:`, `build:`, `refactor:` | hidden | none | none |

Scopes are optional, for example `feat(is): support runtime filter changes`.

## Notes

- Before 1.0, breaking changes bump the minor version and `feat` also bumps the minor version, so the library never jumps to 1.0 by accident. To release 1.0.0 deliberately, add a `Release-As: 1.0.0` footer to a commit.
- The repository setting "Allow GitHub Actions to create and approve pull requests" must be on for release-please.
- Publishing to nuget.org is not wired up yet (planned for M5). Until then packages are attached to the GitHub release.
- To release manually, push a `vX.Y.Z` tag; MinVer will version a local `dotnet pack` from it.
