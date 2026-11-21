#!/usr/bin/env bash
# Publishes and packages Easy Notch for Linux: artifacts/EasyNotch-linux-<arch>.tar.gz
#   linux/publish.sh [linux-x64|linux-arm64]
set -euo pipefail

runtime=${1:-linux-x64}
case "$runtime" in linux-x64|linux-arm64) ;; *) echo "Runtime must be linux-x64 or linux-arm64." >&2; exit 2;; esac
here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/.." && pwd)
dotnet=${DOTNET:-dotnet}

name="EasyNotch-$runtime"
out="$root/artifacts/$name"
rm -rf "$out" "$root/artifacts/$name.tar.gz"
"$dotnet" publish "$here/EasyNotch.Linux.csproj" -c Release -r "$runtime" --self-contained true -o "$out/.publish" \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
    -p:DebugType=None -p:DebugSymbols=false --disable-build-servers -m:1
mv "$out/.publish/easy-notch" "$out/easy-notch"
rm -rf "$out/.publish"

cp "$root/README.md" "$here/THIRD-PARTY-NOTICES.md" "$out/"
cp "$here/packaging/install.sh" "$here/packaging/uninstall.sh" "$here/packaging/easy-notch.desktop" "$out/"
cp "$here/Assets/easynotch.png" "$out/easy-notch.png"
chmod 755 "$out/easy-notch" "$out/install.sh" "$out/uninstall.sh"

# Licences for everything bundled inside the executable.
licences="$out/licences"
mkdir -p "$licences/material-icons"
cp "$here/Assets/Fonts/LICENSE-material-icons.txt" "$licences/material-icons/LICENSE.txt"
packages=$(sed -n 's/.*"packagesPath": "\(.*\)",/\1/p' "$here/obj/project.assets.json" | head -1)
for pack in "microsoft.netcore.app.runtime.$runtime" "microsoft.netcore.app.host.$runtime"; do
    dir=$(ls -d "$packages/$pack"/*/ 2>/dev/null | sort -V | tail -1 || true)
    [ -n "$dir" ] || continue
    mkdir -p "$licences/runtime/$pack"
    find "$dir" -maxdepth 1 -type f \( -iname 'LICENSE*' -o -iname 'THIRD-PARTY-NOTICES*' \) -exec cp {} "$licences/runtime/$pack/" \;
done
for pack in avalonia avalonia.fonts.inter skiasharp harfbuzzsharp tmds.dbus.protocol; do
    dir=$(ls -d "$packages/$pack"/*/ 2>/dev/null | sort -V | tail -1 || true)
    [ -n "$dir" ] || continue
    for file in "$dir"LICENSE* "$dir"license* "$dir"THIRD-PARTY-NOTICES*; do [ -f "$file" ] && { mkdir -p "$licences/$pack"; cp "$file" "$licences/$pack/"; }; done
done

tar -C "$root/artifacts" -czf "$root/artifacts/$name.tar.gz" "$name"
sha256sum "$root/artifacts/$name.tar.gz"
