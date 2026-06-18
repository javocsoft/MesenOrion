# Mesen Orion — Flatpak

This folder contains a Flatpak manifest that packages the **prebuilt** Mesen Orion Linux binary
(the same self-contained executable shipped in the `.deb` / AppImage).

## Prerequisites

```sh
# Flatpak + builder
sudo apt install flatpak flatpak-builder            # (or your distro's equivalent)
flatpak remote-add --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install -y flathub org.freedesktop.Platform//23.08 org.freedesktop.Sdk//23.08
```

## Build (easy path)

A helper script does everything — builds the app, resolves the SDL2 hash automatically, and
builds + installs the Flatpak (just like `build-linux.sh` does for the `.deb`):

```sh
./build-flatpak.sh                # build app + flatpak, install for the user
./build-flatpak.sh --skip-build   # reuse the existing build (no recompile)
./build-flatpak.sh --bundle       # also export a single-file MesenOrion-<version>.flatpak
./build-flatpak.sh --help         # all options
```

## Build (manual)

1. **Build the app first** so the staged install tree exists:

   ```sh
   ./build-linux.sh          # produces distributable/mesen-orion/usr/...
   ```

2. **Fill in the SDL2 hash.** The manifest builds SDL2 from source (the freedesktop runtime does
   not ship it, and the core links it dynamically). Replace the placeholder `sha256` in
   `io.github.javocsoft.MesenOrion.yml` with the real one — the easiest way is to run the build
   once and copy the hash flatpak-builder prints on the mismatch, or:

   ```sh
   curl -L https://github.com/libsdl-org/SDL/releases/download/release-2.30.9/SDL2-2.30.9.tar.gz | sha256sum
   ```

3. **Build + install + run:**

   ```sh
   flatpak-builder --user --install --force-clean build-flatpak flatpak/io.github.javocsoft.MesenOrion.yml
   flatpak run io.github.javocsoft.MesenOrion
   ```

## Notes & status

- This manifest **bundles the prebuilt binary** (simple, good for self-distribution). For an
  official **Flathub** submission they generally require building from source inside the sandbox
  (with the `org.freedesktop.Sdk.Extension.dotnet*` extension), which is a larger change.
- **Not yet tested end-to-end** here — it's a complete, correct-as-far-as-possible scaffold.
  The most likely things to adjust on a real flatpak-builder machine:
  - the SDL2 version/sha256,
  - whether the binary's `rpath` finds `/app/lib/mesen-orion/*.so` (it mirrors the `.deb` layout),
  - the `runtime-version` (bump to a newer `org.freedesktop.Platform` if desired).
- `finish-args` grant: GPU + controllers (`--device=all`), audio (pulseaudio), network
  (RetroAchievements / net play), Wayland/X11, and `--filesystem=home` for ROM/save access.
  Tighten `--filesystem=home` to a specific ROMs folder if you prefer.
