#!/bin/sh
# Removes Easy Notch for the current user.
#   ./uninstall.sh          remove the app, keep your notes, clipboard and settings
#   ./uninstall.sh --purge  also remove them (~/.local/share/easy-notch)
set -eu

data=${XDG_DATA_HOME:-$HOME/.local/share}
config=${XDG_CONFIG_HOME:-$HOME/.config}
app="$HOME/.local/opt/easy-notch"

pkill -TERM -u "$(id -u)" -x easy-notch 2>/dev/null || true
for _ in 1 2 3 4 5 6 7 8 9 10; do pgrep -u "$(id -u)" -x easy-notch >/dev/null 2>&1 || break; sleep 0.3; done

# Only remove the login entry when it starts this installed copy.
if [ -f "$config/autostart/easy-notch.desktop" ] && grep -q "$app/easy-notch" "$config/autostart/easy-notch.desktop"; then
    rm -f "$config/autostart/easy-notch.desktop"
fi
[ "$(readlink "$HOME/.local/bin/easy-notch" 2>/dev/null || true)" = "$app/easy-notch" ] && rm -f "$HOME/.local/bin/easy-notch"
rm -f "$data/applications/easy-notch.desktop" "$data/icons/hicolor/256x256/apps/easy-notch.png"
rm -rf "$app"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q "$data/applications" || true

if [ "${1:-}" = "--purge" ]; then
    rm -rf "$data/easy-notch"
    echo "Easy Notch and its notes, clipboard and settings have been removed."
else
    echo "Easy Notch has been removed. Your notes, clipboard and settings remain in $data/easy-notch (use --purge to delete them)."
fi
