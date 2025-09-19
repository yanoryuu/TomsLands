#!/usr/bin/env bash
set -euo pipefail

IN_DIR="${1:-./Assets/Scripts}"
OUT_DIR="${2:-./uml}"

# Unity由来の巨大ディレクトリは除外
EXCLUDE="bin,obj,Library,Temp,Logs"

mkdir -p "$OUT_DIR"

dotnet tool run puml-gen "$IN_DIR" "$OUT_DIR" \
  -dir \
  -public \
  -createAssociation \
  -excludePaths "$EXCLUDE" \
  -allInOne

echo "✅ Generated PlantUML files in: $OUT_DIR"
echo "   Open $OUT_DIR/include.puml in Rider to preview."
