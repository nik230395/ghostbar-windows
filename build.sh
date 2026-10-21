#!/bin/bash
set -e
cd "$(dirname "$0")"
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -o publish
echo "Built: publish/Ghostbar.exe"
