#!/bin/sh
# Installs Easy Notch for the current user. No root access is needed.
#   ./install.sh                 install or upgrade, then start Easy Notch
#   ./install.sh --no-start      install or upgrade without starting it
#   ./install.sh --no-autostart  don't start Easy Notch when you log in
# Start at login can also be switched in Settings.
set -eu

here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
data=${XDG_DATA_HOME:-$HOME/.local/share}
config=${XDG_CONFIG_HOME:-$HOME/.config}
app="$HOME/.local/opt/easy-notch"
bin="$HOME/.local/bin"
start=1
autostart=1
for option in "$@"; do
    case "$option" in
        --no-start) start=0 ;;
        --no-autostart) autostart=0 ;;
        *) echo "Unknown option: $option" >&2; exit 2 ;;
    esac
done

if [ ! -f "$here/easy-notch" ]; then
    echo "Run install.sh from the extracted Easy Notch folder." >&2
    exit 1
fi

# Close a running copy so it saves notes and settings; it's started again below.
if pgrep -u "$(id -u)" -x easy-notch >/dev/null 2>&1; then
    pkill -TERM -u "$(id -u)" -x easy-notch || true
    for _ in 1 2 3 4 5 6 7 8 9 10; do pgrep -u "$(id -u)" -x easy-notch >/dev/null 2>&1 || break; sleep 0.3; done
fi

mkdir -p "$app" "$bin" "$data/applications" "$data/icons/hicolor/256x256/apps"
# Copy to a temporary name, then rename: replacing the executable is atomic.
cp "$here/easy-notch" "$app/easy-notch.new" && chmod 755 "$app/easy-notch.new" && mv -f "$app/easy-notch.new" "$app/easy-notch"
for file in README.md THIRD-PARTY-NOTICES.md uninstall.sh easy-notch.png; do
    [ -f "$here/$file" ] && cp "$here/$file" "$app/"
done
chmod 755 "$app/uninstall.sh"
rm -rf "$app/licences" && cp -R "$here/licences" "$app/licences"
cp "$here/easy-notch.png" "$data/icons/hicolor/256x256/apps/easy-notch.png"
ln -sf "$app/easy-notch" "$bin/easy-notch"
sed "s|@EXEC@|$app/easy-notch|" "$here/easy-notch.desktop" > "$data/applications/easy-notch.desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q "$data/applications" || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t "$data/icons/hicolor" || true

# Start at login: the same entry Settings > "Start when I log in" writes.
if [ "$autostart" = 1 ]; then
    mkdir -p "$config/autostart"
    printf '%s\n' "[Desktop Entry]" "Type=Application" "Name=Easy Notch" "Comment=A notch at the top of your screen" \
        "Exec=\"$app/easy-notch\"" "Icon=easy-notch" "Terminal=false" "X-GNOME-Autostart-enabled=true" "X-KDE-autostart-after=panel" \
        > "$config/autostart/easy-notch.desktop"
fi

echo "Easy Notch is installed in $app"
if [ "$autostart" = 1 ]; then echo "It starts when you log in (turn this off in Settings)."; fi
echo "Open it from your application menu, or run: easy-notch"
case ":$PATH:" in *":$bin:"*) ;; *) echo "(Add $bin to your PATH to run 'easy-notch' from a terminal.)";; esac

if [ "$start" = 1 ]; then
    if command -v setsid >/dev/null 2>&1; then setsid -f "$app/easy-notch" >/dev/null 2>&1 < /dev/null
    else nohup "$app/easy-notch" >/dev/null 2>&1 < /dev/null & fi
fi
