#!/usr/bin/env bash
# Fails on em dashes and en dashes in tracked text files.
set -euo pipefail
cd "$(dirname "$0")/.."
git ls-files -z -- '*.cs' '*.csproj' '*.py' '*.md' '*.sh' '*.yml' '*.manifest' LICENSE NOTICE | python3 -c '
import sys
bad = 0
for path in sys.stdin.read().split("\0"):
    if not path:
        continue
    for n, line in enumerate(open(path, encoding="utf-8"), 1):
        if chr(0x2013) in line or chr(0x2014) in line:
            print(f"{path}:{n}: {line.rstrip()}")
            bad += 1
if bad:
    sys.exit("em or en dash found: use a comma, a colon or a full stop")
print("text ok")
'
