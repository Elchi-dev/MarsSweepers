#!/usr/bin/env bash
# Builds dstorage.dll (x64) with clang and lld-link; no C runtime.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p out
llvm-dlltool -m i386:x86-64 -d kernel32.def -l out/kernel32.lib
clang --target=x86_64-pc-windows-msvc -O1 -fno-builtin -fno-stack-protector -mno-stack-arg-probe -c proxy.c -o out/proxy.obj
lld-link /dll /nodefaultlib /entry:DllMain /machine:x64 /def:dstorage.def /out:out/dstorage.dll out/proxy.obj out/kernel32.lib
echo out/dstorage.dll
