#!/bin/bash
set -e

export PUBLISHFLAGS="-r linux-x64 --no-self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true"
make -j$(nproc) -O LTO=true STATICLINK=true SYSTEM_LIBEVDEV=false

curl -SL https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage -o appimagetool

rm -rf AppDir
mkdir -p AppDir/usr/bin
cp bin/linux-x64/Release/linux-x64/publish/Mesen AppDir/usr/bin
chmod +x AppDir/usr/bin/Mesen
ln -sr AppDir/usr/bin/Mesen AppDir/AppRun

# Bundle the shader collection so it ships inside the AppImage. Mesen looks for
# "<exeDir>/../share/mesen-orion/shaders" (same layout as the .deb), which resolves
# to this folder when the binary runs from AppDir/usr/bin.
mkdir -p AppDir/usr/share/mesen-orion/shaders
cp -r Shaders/. AppDir/usr/share/mesen-orion/shaders/

# Mesen Orion icon + desktop entry
cp distributable/mesen-orion/usr/share/icons/hicolor/128x128/apps/mesen-orion.png AppDir/mesen-orion.png
cp Linux/appimage/Mesen.desktop AppDir/mesen-orion.desktop
mkdir -p AppDir/usr/share/applications && cp ./AppDir/mesen-orion.desktop ./AppDir/usr/share/applications
mkdir -p AppDir/usr/share/icons && cp ./AppDir/mesen-orion.png ./AppDir/usr/share/icons
mkdir -p AppDir/usr/share/icons/hicolor/128x128/apps && cp ./AppDir/mesen-orion.png ./AppDir/usr/share/icons/hicolor/128x128/apps

# AppStream metadata (embedded so the AppImage can be listed on AppImageHub)
mkdir -p AppDir/usr/share/metainfo
cp distributable/mesen-orion/usr/share/metainfo/io.github.javocsoft.MesenOrion.metainfo.xml AppDir/usr/share/metainfo/

chmod a+x appimagetool
./appimagetool AppDir/ MesenOrion-x86_64.AppImage
