# Versioning and releases

Versions follow [Semantic Versioning](https://semver.org). All packages share one version and are tagged `vX.Y.Z`.

**Until 1.0.0 every release is an alpha pre-release**, versioned `0.x.y-alpha.N` (for example `0.1.0-alpha.1`). The
suffix makes nuget.org list the packages as pre-release, and release-please marks each GitHub release as a
pre-release.

## How it works

1. Commits to `main` use [Conventional Commits](https://www.conventionalcommits.org). Pull requests are rebase-merged, so
   every commit lands on `main` as written and release-please reads each one: the `commits` workflow checks every
   commit in a pull request, and `pr-title` checks the title.
2. `release-please` (the `release` workflow) keeps a release PR open that bumps the version and updates `CHANGELOG.md`.
3. Merging that PR creates the `vX.Y.Z` tag and a GitHub release. The `pack` job then builds the tag, runs the tests, and attaches the `.nupkg` and `.snupkg` files to the release.
4. [MinVer](https://github.com/adamralph/minver) derives each assembly and package version from the nearest tag, so no version number is stored in the repo. Builds between releases look like `0.1.1-alpha.0.3`.

## Commit types

| Prefix | Changelog | Bump during the alpha (`0.x.y-alpha.N`) | Bump (1.0 and later) |
| --- | --- | --- | --- |
| `feat:` | Features | alpha number (`0.1.0-alpha.1` → `0.1.0-alpha.2`) | minor |
| `fix:`, `perf:` | Bug fixes, Performance | alpha number | patch |
| `feat!:`, `fix!:` or a `BREAKING CHANGE:` footer | Breaking changes | alpha number | major |
| `docs:`, `test:`, `chore:`, `ci:`, `build:`, `refactor:` | hidden | none | none |

During the alpha, release-please only increments the alpha number. To start a new milestone, add a footer such as
`Release-As: 0.2.0-alpha.1` to a commit.

Scopes are optional, for example `feat(is): support runtime filter changes`.

Only changes to shipped code count. release-please skips a commit when every file it touches is under `tests/`,
`tools/`, `docs/`, `apps/` (the CLI is not packaged), `benchmarks/` or `.github/` (`exclude-paths` in
`release-please-config.json`).
Use `fix:` and `feat:` for the libraries in `src/`; a fix to a tool or test is `test:` or `build:`.

## Notes

- The alpha versioning comes from `release-please-config.json`: `"versioning": "prerelease"`, `"prerelease": true`
  and `"prerelease-type": "alpha"`. The first release was set to `0.1.0-alpha.1` with a `Release-As` footer: with no
  earlier release tag, release-please would otherwise propose 1.0.0 (its `initial-version` setting did not take effect
  here).
- The library never reaches 1.0 by accident. For the first stable release, remove the three pre-release settings and
  add a `Release-As: 1.0.0` footer to a commit. Also move the public API to `PublicAPI.Shipped.txt` (see
  [public-api.md](public-api.md)) and set `PackageValidationBaselineVersion` to catch breaking changes from then on.
- The repository setting "Allow GitHub Actions to create and approve pull requests" must be on for release-please.
- The `pack` job refuses to attach or publish any package whose version has no pre-release suffix.
- To release manually, push a `vX.Y.Z` tag; MinVer will version a local `dotnet pack` from it.

## Publishing to nuget.org

After the `pack` job attaches the packages to the GitHub release, the `publish` job pushes them to nuget.org with
[trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): GitHub's OIDC token is exchanged
for a short-lived API key, so no key is stored in the repository. The job waits for approval on the `nuget`
environment, because a package pushed to nuget.org cannot be deleted, only unlisted.

One-time setup:

1. **nuget.org trusted publishing policy.** Signed in as the account (or organization) that will own the packages, go
   to your username → *Trusted Publishing* and add a GitHub Actions policy: owner `APRS-NET`, repository `APRS.NET`,
   workflow file `release.yml`, environment `nuget`.
2. **Repository variable** `NUGET_USER` = that nuget.org username (Settings → Secrets and variables → Actions →
   Variables). It is not a secret. Until it is set the `publish` job is skipped and releases only go to GitHub.
3. **Environment** `nuget` (Settings → Environments) with yourself as a required reviewer, and *Deployment branches
   and tags* limited to the `main` branch (the release workflow runs on pushes to `main`, not on the tag).

`--skip-duplicate` makes a re-run safe: versions that are already on nuget.org are skipped.
