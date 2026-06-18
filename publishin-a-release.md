# Publishing a release

1. **Build the artifacts:**
   ```sh
   ./build-linux.sh                 # -> mesen-orion_<version>_amd64.deb
   ./Linux/appimage/appimage.sh     # -> MesenOrion-<version>-x86_64.AppImage
   ```

2. **Create the GitHub Release** with the artifacts as assets (using the `gh` CLI):
   ```sh
   gh release create v3.0.2 \
     MesenOrion-3.0.2-x86_64.AppImage \
     mesen-orion_3.0.2_amd64.deb \
     --title "Mesen Orion 3.0.2" --notes "Release notes here"
   ```
   > Don't rename the AppImage asset — the `MesenOrion-<version>-x86_64.AppImage` naming is what
   > AppImageHub's auto-detection relies on.

## Listing on AppImageHub (optional, one-time)

[AppImageHub](https://appimage.github.io/) is a public, crowd-sourced catalog. Submitting is a
**one-time** action — once accepted it auto-detects every future release. Steps:

1. Fork **https://github.com/AppImage/appimage.github.io**.
2. Add a new file `data/MesenOrion` (file name = repository name, case-sensitive) whose **only
   content** is one line with the repository URL (not a link to a specific AppImage):
   ```
   https://github.com/javocsoft/MesenOrion
   ```
3. Open a pull request against `AppImage/appimage.github.io` `master`.
4. The `Test / test (pull_request)` GitHub Action downloads the latest AppImage from the repo's
   Releases and validates it. It passes because the AppImage uses the standard naming and embeds
   the `.desktop` entry, icon and AppStream metainfo (`io.github.javocsoft.MesenOrion.metainfo.xml`).
5. A green check means the requirements are met; an AppImageHub maintainer then merges the PR
   manually (this can take a while). Once merged, Mesen Orion appears in the catalog and new
   releases are picked up automatically — no further PRs needed.

> If the check goes red, open the failed check's *Details* and read the log: the most common
> cause is the Release not being published yet, or the AppImage asset not matching the
> `MesenOrion-<version>-x86_64.AppImage` name.
