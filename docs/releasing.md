# Building and releasing TokenGauge

A local release build and a published release are different things:

- A **local release build** is disposable and can be produced at any time.
- A **GitHub Release** is an official snapshot associated with a version tag.

Creating a tag does not freeze `main`. Development carries on as normal after a release; the tag keeps identifying the commit that was released.

## Build locally

From the repository root:

```powershell
dotnet publish .\src\TokenGauge.csproj -p:PublishProfile=win-x64-single-file --output .\artifacts\release
```

This writes `artifacts\release\TokenGauge.exe`: a single self-contained Windows x64 executable (about 50 MB). It includes the .NET runtime, so it needs no installer and no separate .NET install. In VS Code, the **publish single file** task runs the same command.

Local builds don't need a tag and don't create anything on GitHub.

## Continuous integration

[The CI workflow](../.github/workflows/ci.yml) runs for pull requests and pushes to `main`. It builds the solution and publishes the single-file executable. The executable is attached to the run for 14 days (as `TokenGauge.exe` itself, not a zip), so any commit on `main` can be downloaded and tried from the run's **Summary** page.

A green CI run means the commit compiles and publishes. It doesn't create a release.

## Version numbers

Release tags use semantic versions with a leading `v`:

```text
v0.1.0-beta.1  First beta of 0.1.0
v0.1.0-rc.1    First release candidate
v0.1.0         Final 0.1.0 release
v0.1.1         Patch release
v0.2.0         Feature release
```

Tags with a suffix (such as `-beta.1`) are published as pre-releases.

Treat every pushed release tag as permanent. If a release needs a correction, commit the fix and create a new version instead of moving or reusing the old tag.

## Publish a GitHub Release

Make sure the intended commit is committed and pushed to `main`:

```powershell
git status
git push origin main
```

Create and push an annotated tag:

```powershell
git tag -a v0.1.0 -m "TokenGauge v0.1.0"
git push origin v0.1.0
```

[The Release workflow](../.github/workflows/release.yml) starts when a pushed tag begins with `v`. It:

1. Validates the tag format.
2. Builds `TokenGauge.exe` with the version from the tag embedded in it.
3. Generates `TokenGauge.exe.sha256`.
4. Creates the GitHub Release with generated notes and both files attached.

Don't also create the Release through GitHub's **Draft a new release** page. That creates the tag and Release together, which conflicts with the workflow creating the same Release.

Follow the run under the repository's **Actions** tab. When it succeeds, download the executable from the **Releases** page. The source ZIP and tarball GitHub adds are source archives, not the app.

## Verify a downloaded release

From the folder containing both downloaded files:

```powershell
$expected = (Get-Content .\TokenGauge.exe.sha256).Split()[0]
$actual = (Get-FileHash .\TokenGauge.exe -Algorithm SHA256).Hash
$actual.Equals($expected, [StringComparison]::OrdinalIgnoreCase)
```

The result should be `True`.

## Troubleshooting

**The workflow didn't start.** Check the tag was pushed (`git push origin <tag>`), not only created locally; that it begins with `v`; and that the tagged commit contains `.github/workflows/release.yml`.

**Tag validation failed.** Use a tag such as `v1.2.3` or `v1.2.3-beta.1`. A name such as `release-1` doesn't match.

**The Release already exists.** It was probably created by hand before the workflow finished. Resolve the conflicting Release before re-running the workflow, and use the tag flow from then on.

**A released build needs a fix.** Commit the fix and publish a new version, e.g. follow `v0.1.0` with `v0.1.1`.
