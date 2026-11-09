#!/usr/bin/env bash
# Compile et lance la version C# / .NET de 321 Back Office.
set -euo pipefail

cd "$(dirname "$0")"

PROJECT="dotnet/BackOffice.csproj"
BUILD_DIR="${BUILD_DIR:-dotnet/build}"

if [ ! -f "$PROJECT" ]; then
  echo "Erreur : projet introuvable ($PROJECT)." >&2
  exit 1
fi

# SDK local installé dans .dotnet/ (sinon SDK système).
if [ -x ".dotnet/dotnet" ]; then
  DOTNET="$PWD/.dotnet/dotnet"
elif command -v dotnet >/dev/null 2>&1; then
  DOTNET="dotnet"
else
  echo "Erreur : le SDK .NET est introuvable. Installez-le : https://dotnet.microsoft.com/download" >&2
  exit 1
fi

echo "Compilation ..."
"$DOTNET" build "$PROJECT" -c Release -o "$BUILD_DIR" --nologo -v minimal

BIN="$BUILD_DIR/BackOffice.dll"
if [ ! -f "$BIN" ]; then
  echo "Erreur : compilation terminée sans binaire ($BIN)." >&2
  exit 1
fi

echo "Lancement de 321 Back Office ..."
exec "$DOTNET" "$BIN" "$@"
