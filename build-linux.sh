#!/usr/bin/env bash
#
# Build and packaging script for Mesen Orion on Linux.
#
# Compiles the core (MesenCore.so) and the .NET UI, then stages the published
# single-file executable into the Debian package tree under
# "distributable/mesen-orion/" and builds a .deb named:
#
#     mesen-orion_<version>_amd64.deb
#
# (version is read from distributable/mesen-orion/DEBIAN/control).
#
# Usage:
#     ./build-linux.sh                 # make clean + build (GCC) + package .deb
#     ./build-linux.sh --clang         # build with Clang instead of GCC
#     ./build-linux.sh --no-clean      # skip 'make clean'
#     ./build-linux.sh --skip-build    # only repackage the existing build output
#     ./build-linux.sh --jobs 8        # override parallel job count
#     ./build-linux.sh --output /tmp   # place the .deb in another directory
#     ./build-linux.sh --deb-name foo.deb
#     ./build-linux.sh --help
#
set -euo pipefail

# ---------------------------------------------------------------------------
# Defaults / parameters
# ---------------------------------------------------------------------------

USE_GCC_BUILD=true     # default to GCC (USE_GCC=true), like the documented Linux build
DO_CLEAN=true
DO_BUILD=true
JOBS="$(nproc)"
OUTPUT_DIR=""
DEB_NAME=""

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

c_cyan='\033[0;36m'; c_green='\033[0;32m'; c_gray='\033[0;90m'; c_red='\033[0;31m'; c_off='\033[0m'

write_header() { printf "\n${c_cyan}========================================${c_off}\n"; printf "${c_cyan}  %s${c_off}\n" "$1"; printf "${c_cyan}========================================${c_off}\n"; }
write_ok()   { printf "  ${c_green}[OK]${c_off} %s\n" "$1"; }
write_info() { printf "  ${c_gray}[..]${c_off} %s\n" "$1"; }
write_fail() { printf "  ${c_red}[!!]${c_off} %s\n" "$1" >&2; }

usage() { sed -n '2,40p' "$0" | sed 's/^# \{0,1\}//'; exit 0; }

# ---------------------------------------------------------------------------
# Parse arguments
# ---------------------------------------------------------------------------

while [ $# -gt 0 ]; do
	case "$1" in
		--clang)       USE_GCC_BUILD=false ;;
		--gcc)         USE_GCC_BUILD=true ;;
		--no-clean)    DO_CLEAN=false ;;
		--skip-build)  DO_BUILD=false ;;
		--jobs|-j)     shift; JOBS="${1:?--jobs requires a value}" ;;
		--output|-o)   shift; OUTPUT_DIR="${1:?--output requires a value}" ;;
		--deb-name)    shift; DEB_NAME="${1:?--deb-name requires a value}" ;;
		--help|-h)     usage ;;
		*) write_fail "Unknown option: $1"; echo "Run '$0 --help' for usage."; exit 1 ;;
	esac
	shift
done

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST_ROOT="$SCRIPT_DIR/distributable/mesen-orion"
CONTROL_FILE="$DIST_ROOT/DEBIAN/control"
PUBLISH_DIR="$SCRIPT_DIR/bin/linux-x64/Release/linux-x64/publish"
SHADERS_SRC="$SCRIPT_DIR/Shaders"

BIN_DST="$DIST_ROOT/usr/bin/mesen-orion"
LIB_DST="$DIST_ROOT/usr/lib/mesen-orion"
SHADERS_DST="$DIST_ROOT/usr/share/mesen-orion/shaders"

[ -z "$OUTPUT_DIR" ] && OUTPUT_DIR="$SCRIPT_DIR"

# ---------------------------------------------------------------------------
# Prerequisites
# ---------------------------------------------------------------------------

write_header "Checking prerequisites"

for tool in dpkg-deb; do
	command -v "$tool" >/dev/null 2>&1 || { write_fail "'$tool' not found in PATH."; exit 1; }
done
write_ok "dpkg-deb: $(command -v dpkg-deb)"

if [ "$DO_BUILD" = true ]; then
	command -v make >/dev/null 2>&1 || { write_fail "'make' not found in PATH."; exit 1; }
	command -v dotnet >/dev/null 2>&1 || { write_fail ".NET SDK ('dotnet') not found in PATH."; exit 1; }
	write_ok "make:   $(command -v make)"
	write_ok "dotnet: $(dotnet --version)"
fi

[ -f "$CONTROL_FILE" ] || { write_fail "control file not found: $CONTROL_FILE"; exit 1; }
[ -d "$DIST_ROOT/DEBIAN" ] || { write_fail "package tree not found: $DIST_ROOT"; exit 1; }
write_ok "Package tree: $DIST_ROOT"

# Read the version from the control file (single source of truth)
VERSION="$(awk -F':[[:space:]]*' '/^Version:/{print $2; exit}' "$CONTROL_FILE" | tr -d '[:space:]')"
[ -n "$VERSION" ] || { write_fail "Could not read 'Version:' from $CONTROL_FILE"; exit 1; }
write_ok "Version: $VERSION"

[ -n "$DEB_NAME" ] || DEB_NAME="mesen-orion_${VERSION}_amd64.deb"
DEB_PATH="$OUTPUT_DIR/$DEB_NAME"

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------

if [ "$DO_BUILD" = true ]; then
	if [ "$USE_GCC_BUILD" = true ]; then BUILD_ENV="USE_GCC=true"; else BUILD_ENV=""; fi

	if [ "$DO_CLEAN" = true ]; then
		write_header "make clean"
		( cd "$SCRIPT_DIR" && make clean )
		write_ok "Cleaned"
	fi

	write_header "Building (${BUILD_ENV:-clang}, -j$JOBS)"
	( cd "$SCRIPT_DIR" && env $BUILD_ENV make -j"$JOBS" )
	write_ok "Build completed"
else
	write_info "Skipping build (--skip-build)"
fi

# ---------------------------------------------------------------------------
# Stage files into the package tree
# ---------------------------------------------------------------------------

write_header "Staging package files"

[ -d "$PUBLISH_DIR" ] || { write_fail "Publish directory not found: $PUBLISH_DIR"; write_fail "Run a full build first (omit --skip-build)."; exit 1; }
[ -f "$PUBLISH_DIR/Mesen" ] || { write_fail "Executable not found: $PUBLISH_DIR/Mesen"; exit 1; }

# Executable -> usr/bin/mesen-orion
install -D -m 0755 "$PUBLISH_DIR/Mesen" "$BIN_DST"
write_ok "usr/bin/mesen-orion ($(du -h "$BIN_DST" | cut -f1))"

# Native libraries -> usr/lib/mesen-orion
mkdir -p "$LIB_DST"
for lib in libHarfBuzzSharp.so libSkiaSharp.so; do
	if [ -f "$PUBLISH_DIR/$lib" ]; then
		install -m 0644 "$PUBLISH_DIR/$lib" "$LIB_DST/$lib"
		write_ok "usr/lib/mesen-orion/$lib"
	else
		write_fail "Native library not found in publish output: $lib"; exit 1
	fi
done

# Shaders collection -> usr/share/mesen-orion/shaders
if [ -d "$SHADERS_SRC" ]; then
	rm -rf "$SHADERS_DST"
	mkdir -p "$SHADERS_DST"
	cp -r "$SHADERS_SRC/." "$SHADERS_DST/"
	write_ok "usr/share/mesen-orion/shaders ($(find "$SHADERS_DST" -type f | wc -l) files)"
else
	write_info "Shaders source folder not found, leaving existing shaders untouched."
fi

# Ensure maintainer scripts are executable
[ -f "$DIST_ROOT/DEBIAN/postinst" ] && chmod 0755 "$DIST_ROOT/DEBIAN/postinst"
[ -f "$DIST_ROOT/DEBIAN/prerm" ]    && chmod 0755 "$DIST_ROOT/DEBIAN/prerm"

# ---------------------------------------------------------------------------
# Build the .deb
# ---------------------------------------------------------------------------

write_header "Building package: $DEB_NAME"

mkdir -p "$OUTPUT_DIR"
rm -f "$DEB_PATH"
dpkg-deb --root-owner-group --build "$DIST_ROOT" "$DEB_PATH"

write_ok "Created: $DEB_PATH ($(du -h "$DEB_PATH" | cut -f1))"

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

write_header "All done"
write_ok "Package: $DEB_PATH"
printf "\n  Install with:  ${c_gray}sudo dpkg -i %s${c_off}\n\n" "$DEB_PATH"
