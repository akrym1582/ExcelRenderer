#!/usr/bin/env bash
set -euo pipefail

missing=()
for package in "$@"; do
    if [[ "$(dpkg-query -W -f='${Status}' "$package" 2>/dev/null)" != "install ok installed" ]]; then
        missing+=("$package")
    fi
done

if (( ${#missing[@]} == 0 )); then
    exit 0
fi

sudo apt-get -o Acquire::Retries=3 update
sudo apt-get -o Acquire::Retries=3 install -y "${missing[@]}"
