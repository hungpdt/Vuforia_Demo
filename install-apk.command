#!/bin/zsh

set -euo pipefail

if (( $# != 1 )); then
  print -u2 "Usage:"
  print -u2 "  $0 /path/to/file.apk"
  print -u2 ""
  print -u2 "You can also drag an APK file onto install-apk.command."
  exit 2
fi

if (( $+commands[adb] )); then
  adb_bin="$commands[adb]"
else
  adb_candidates=(/Applications/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb(N.om))
  if (( ${#adb_candidates} == 0 )); then
    print -u2 "ADB was not found. Install Android Build Support in Unity Hub."
    exit 1
  fi
  adb_bin="$adb_candidates[1]"
fi

apk_path="${1:A}"

if [[ ! -f "$apk_path" ]]; then
  print -u2 "File not found: $apk_path"
  exit 2
fi

if [[ "${apk_path:e:l}" != "apk" ]]; then
  print -u2 "The selected file is not an APK: $apk_path"
  exit 2
fi

print "[  0%] Validating APK and ADB..."
device_count=$("$adb_bin" devices | awk 'NR > 1 && $2 == "device" { count++ } END { print count + 0 }')
if (( device_count == 0 )); then
  print -u2 "No authorized ADB device was found. Unlock the phone and accept the USB debugging prompt."
  "$adb_bin" devices -l
  exit 1
fi

if (( device_count > 1 )); then
  print -u2 "More than one ADB device is connected. Disconnect the devices you do not want to use."
  "$adb_bin" devices -l
  exit 1
fi

remote_apk="/data/local/tmp/codex-install-$$.apk"

cleanup() {
  "$adb_bin" shell rm -f "$remote_apk" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM

print "[ 10%] Device connected."
print "[ 20%] Uploading: $apk_path"

# ADB reports the upload percentage. Map it into 20-80% of the full operation.
"$adb_bin" push -p "$apk_path" "$remote_apk" 2>&1 | \
  perl -pe 's{\[\s*(\d+)%\]}{sprintf("[ %3d%%]", 20 + int($1 * 0.6))}ge'

print "[ 80%] APK uploaded."
print "[ 90%] Installing package on the device..."

if install_output=$("$adb_bin" shell pm install -r "$remote_apk" 2>&1); then
  print "$install_output"
else
  print -u2 "$install_output"
  print -u2 "Installation failed."
  exit 1
fi

cleanup
trap - EXIT INT TERM

print "[100%] Installation completed successfully."

read "?Press Enter to close this window..."
