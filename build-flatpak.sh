#!/usr/bin/env bash
#
# Build script for the Mesen Orion Flatpak.
#
# Wraps flatpak-builder around flatpak/io.github.javocsoft.MesenOrion.yml. It builds the app
# (via build-linux.sh, to produce the staged install tree), auto-resolves the SDL2 source hash,
# then builds the Flatpak and, by default, exports a single-file .flatpak bundle (use --install
# if you also want it installed for the current user).
#
# Usage:
#     ./build-flatpak.sh                  # build app + export MesenOrion-<version>.flatpak
#     ./build-flatpak.sh --install        # also install it for the current user
#     ./build-flatpak.sh --no-bundle      # don't export the bundle (pair with --install)
#     ./build-flatpak.sh --skip-build     # reuse the existing distributable/ tree (no recompile)
#     ./build-flatpak.sh --runtime-version 24.08
#     ./build-flatpak.sh --jobs 8
#     ./build-flatpak.sh --output /tmp    # where to place the .flatpak bundle
#     ./build-flatpak.sh --help
#
set -euo pipefail

# ---------------------------------------------------------------------------
# Defaults / parameters
# ---------------------------------------------------------------------------

DO_BUILD=true
DO_INSTALL=false
DO_BUNDLE=true
JOBS="$(nproc)"
OUTPUT_DIR=""
RUNTIME_VERSION=""

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

c_cyan='\033[0;36m'; c_green='\033[0;32m'; c_gray='\033[0;90m'; c_red='\033[0;31m'; c_off='\033[0m'

write_header() { printf "\n${c_cyan}========================================${c_off}\n"; printf "${c_cyan}  %s${c_off}\n" "$1"; printf "${c_cyan}========================================${c_off}\n"; }
write_ok()   { printf "  ${c_green}[OK]${c_off} %s\n" "$1"; }
write_info() { printf "  ${c_gray}[..]${c_off} %s\n" "$1"; }
write_fail() { printf "  ${c_red}[!!]${c_off} %s\n" "$1" >&2; }

usage() { sed -n '2,17p' "$0" | sed 's/^# \{0,1\}//'; exit 0; }

# Detects the system package manager and echoes the install command prefix (empty if none found).
detect_pkg_install() {
	if   command -v apt-get >/dev/null 2>&1; then echo "sudo apt-get install -y"
	elif command -v dnf     >/dev/null 2>&1; then echo "sudo dnf install -y"
	elif command -v pacman  >/dev/null 2>&1; then echo "sudo pacman -S --needed --noconfirm"
	elif command -v zypper  >/dev/null 2>&1; then echo "sudo zypper install -y"
	else echo ""; fi
}

# Ensures a tool is available; if not, offers to install its package and continue.
ensure_tool() {
	local tool="$1" pkg="$2"
	command -v "$tool" >/dev/null 2>&1 && { write_ok "$tool: $(command -v "$tool")"; return 0; }

	write_info "'$tool' is not installed."
	local installer; installer="$(detect_pkg_install)"
	if [ -z "$installer" ]; then
		write_fail "No supported package manager found. Install '$pkg' manually and retry."; exit 1
	fi

	local reply=""
	printf "  Install '%s' now (%s %s)? [y/N] " "$pkg" "$installer" "$pkg"
	read -r reply || reply=""
	case "$reply" in
		[yY]|[yY][eE][sS])
			# shellcheck disable=SC2086
			$installer "$pkg" || { write_fail "Installation of '$pkg' failed."; exit 1; }
			command -v "$tool" >/dev/null 2>&1 || { write_fail "'$tool' still not found after install."; exit 1; }
			write_ok "$tool installed"
			;;
		*)
			write_fail "Cannot continue without '$tool'."; exit 1
			;;
	esac
}

# ---------------------------------------------------------------------------
# Parse arguments
# ---------------------------------------------------------------------------

while [ $# -gt 0 ]; do
	case "$1" in
		--skip-build)       DO_BUILD=false ;;
		--install)          DO_INSTALL=true ;;
		--no-install)       DO_INSTALL=false ;;
		--bundle)           DO_BUNDLE=true ;;
		--no-bundle)        DO_BUNDLE=false ;;
		--runtime-version)  shift; RUNTIME_VERSION="${1:?--runtime-version requires a value}" ;;
		--jobs|-j)          shift; JOBS="${1:?--jobs requires a value}" ;;
		--output|-o)        shift; OUTPUT_DIR="${1:?--output requires a value}" ;;
		--help|-h)          usage ;;
		*) write_fail "Unknown option: $1"; echo "Run '$0 --help' for usage."; exit 1 ;;
	esac
	shift
done

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_ID="io.github.javocsoft.MesenOrion"
MANIFEST="$SCRIPT_DIR/flatpak/$APP_ID.yml"
DIST_ROOT="$SCRIPT_DIR/distributable/mesen-orion"
CONTROL_FILE="$DIST_ROOT/DEBIAN/control"
BUILD_DIR="$SCRIPT_DIR/build-flatpak"
REPO_DIR="$SCRIPT_DIR/build-flatpak-repo"

[ -z "$OUTPUT_DIR" ] && OUTPUT_DIR="$SCRIPT_DIR"

# ---------------------------------------------------------------------------
# Prerequisites
# ---------------------------------------------------------------------------

write_header "Checking prerequisites"

# Offer to install the flatpak tooling if it's missing
ensure_tool flatpak flatpak
ensure_tool flatpak-builder flatpak-builder

# These are part of coreutils and effectively always present
for tool in curl sha256sum; do
	command -v "$tool" >/dev/null 2>&1 || { write_fail "'$tool' not found in PATH."; exit 1; }
done

[ -f "$MANIFEST" ] || { write_fail "manifest not found: $MANIFEST"; exit 1; }
write_ok "Manifest: $MANIFEST"

# Resolve the runtime version (CLI override, else read from the manifest)
if [ -z "$RUNTIME_VERSION" ]; then
	RUNTIME_VERSION="$(awk -F"'" '/^runtime-version:/{print $2; exit}' "$MANIFEST")"
fi
[ -n "$RUNTIME_VERSION" ] || { write_fail "Could not determine runtime-version."; exit 1; }
write_ok "Runtime: org.freedesktop.Platform//$RUNTIME_VERSION"

# Make sure the runtime + SDK are installed (best effort; needs the flathub remote)
if ! flatpak info "org.freedesktop.Sdk//$RUNTIME_VERSION" >/dev/null 2>&1; then
	write_info "Installing org.freedesktop.Platform/Sdk//$RUNTIME_VERSION ..."
	flatpak remote-add --if-not-exists --user flathub https://flathub.org/repo/flathub.flatpakrepo || true
	flatpak install --user -y flathub "org.freedesktop.Platform//$RUNTIME_VERSION" "org.freedesktop.Sdk//$RUNTIME_VERSION" \
		|| { write_fail "Could not install the runtime/SDK. Install them manually and retry."; exit 1; }
fi
write_ok "Runtime/SDK present"

# ---------------------------------------------------------------------------
# Build the app (produces the staged tree the manifest installs from)
# ---------------------------------------------------------------------------

if [ "$DO_BUILD" = true ]; then
	write_header "Building Mesen Orion (build-linux.sh)"
	( cd "$SCRIPT_DIR" && ./build-linux.sh --jobs "$JOBS" )
	write_ok "App built + staged"
else
	write_info "Skipping app build (--skip-build)"
fi

[ -f "$DIST_ROOT/usr/bin/mesen-orion" ] || { write_fail "Staged binary not found: $DIST_ROOT/usr/bin/mesen-orion (run without --skip-build)."; exit 1; }

# ---------------------------------------------------------------------------
# Resolve the SDL2 source hash and write a working copy of the manifest
# ---------------------------------------------------------------------------

write_header "Preparing manifest"

SDL_URL="$(awk '/url:.*SDL2-/{print $2; exit}' "$MANIFEST")"
[ -n "$SDL_URL" ] || { write_fail "Could not find the SDL2 url in the manifest."; exit 1; }
write_info "Hashing SDL2 source: $SDL_URL"
SDL_SHA="$(curl -fsSL "$SDL_URL" | sha256sum | cut -d' ' -f1)"
[ -n "$SDL_SHA" ] || { write_fail "Failed to download/hash SDL2."; exit 1; }
write_ok "SDL2 sha256: $SDL_SHA"

WORK_MANIFEST="$SCRIPT_DIR/flatpak/.$APP_ID.work.yml"
sed "s/^\([[:space:]]*sha256:[[:space:]]*\)0\{64\}/\1$SDL_SHA/" "$MANIFEST" > "$WORK_MANIFEST"
trap 'rm -f "$WORK_MANIFEST"' EXIT
write_ok "Working manifest ready"

# ---------------------------------------------------------------------------
# Build the Flatpak
# ---------------------------------------------------------------------------

write_header "Building Flatpak"

FB_ARGS=(--force-clean --disable-rofiles-fuse)
[ "$DO_BUNDLE" = true ] && FB_ARGS+=(--repo="$REPO_DIR")
[ "$DO_INSTALL" = true ] && FB_ARGS+=(--user --install)

flatpak-builder "${FB_ARGS[@]}" "$BUILD_DIR" "$WORK_MANIFEST"
write_ok "Flatpak built"
[ "$DO_INSTALL" = true ] && write_ok "Installed for the current user (flatpak run $APP_ID)"

# ---------------------------------------------------------------------------
# Optional single-file bundle
# ---------------------------------------------------------------------------

if [ "$DO_BUNDLE" = true ]; then
	write_header "Exporting bundle"
	VERSION="$(awk -F':[[:space:]]*' '/^Version:/{print $2; exit}' "$CONTROL_FILE" 2>/dev/null | tr -d '[:space:]')"
	[ -n "$VERSION" ] || VERSION="dev"
	BUNDLE_PATH="$OUTPUT_DIR/MesenOrion-${VERSION}.flatpak"
	flatpak build-bundle "$REPO_DIR" "$BUNDLE_PATH" "$APP_ID"
	write_ok "Bundle: $BUNDLE_PATH"
fi

write_header "Done"
write_ok "Flatpak: $APP_ID"
[ "$DO_INSTALL" = true ] && printf "  Run it with: ${c_green}flatpak run %s${c_off}\n" "$APP_ID"
