#!/usr/bin/env bash
# Build the MAUI app for Android and deploy it to a connected device.
#
# Pass --watch to use `dotnet watch` instead of a one-shot build: it rebuilds
# and redeploys on file changes and streams the app's console output to this
# terminal, which is handy for watching logs while debugging. It blocks in
# the foreground until Ctrl+C.
set -euo pipefail
cd "$(dirname "$0")/.."

WATCH=false
if [[ "${1:-}" == "--watch" ]]; then
    WATCH=true
    shift
fi

if [[ -z "${1:-}" ]]; then
    echo "Usage: $0 [--watch] <device-serial>" >&2
    echo "Run 'adb devices' to find the serial of a connected device." >&2
    exit 1
fi

SERIAL="$1"
PROJECT="example/Plugin.Maui.PullToRefresh.Example/Plugin.Maui.PullToRefresh.Example.csproj"

if ! adb devices | grep -q "^${SERIAL}[[:space:]]*device$"; then
    echo "Error: device $SERIAL not connected (check 'adb devices')." >&2
    exit 1
fi

if $WATCH; then
    dotnet watch --project "$PROJECT" -f net10.0-android \
        -p:AdbTarget="-s $SERIAL"
else
    dotnet build "$PROJECT" -f net10.0-android -t:Run -c Debug \
        -p:AdbTarget="-s $SERIAL"
fi
