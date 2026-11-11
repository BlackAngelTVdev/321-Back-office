#!/usr/bin/env bash
# Compile (si nécessaire) et lance 321 Back Office.
#   ./run.sh [fichier-business.json]   interface graphique
#   ./run.sh --dump [fichier]          affiche le business sans interface
#   ./run.sh --no-build ...            lance sans recompiler
set -euo pipefail

# Fonctionne aussi via un lien symbolique ou depuis un autre dossier.
cd "$(dirname "$(readlink -f "$0")")"

PROJECT="dotnet/BackOffice.csproj"
BUILD_DIR="${BUILD_DIR:-dotnet/build}"
BIN="$BUILD_DIR/BackOffice.dll"

if [ ! -f "$PROJECT" ]; then
  echo "Erreur : projet introuvable ($PROJECT)." >&2
  exit 1
fi

# SDK : $DOTNET, sinon installation locale .dotnet/, sinon SDK système.
if [ -n "${DOTNET:-}" ] && [ -x "$DOTNET" ]; then
  :
elif [ -x ".dotnet/dotnet" ]; then
  DOTNET="$PWD/.dotnet/dotnet"
elif command -v dotnet >/dev/null 2>&1; then
  DOTNET="dotnet"
else
  echo "Erreur : le SDK .NET est introuvable. Installez-le : https://dotnet.microsoft.com/download" >&2
  exit 1
fi

if [ ! -f ".env" ] && [ -f ".env.example" ]; then
  echo "Info : aucun .env ; copiez .env.example en .env pour configurer le broker MQTT."
fi

NO_BUILD=0
ARGS=()
for arg in "$@"; do
  case "$arg" in
    --no-build) NO_BUILD=1 ;;
    *) ARGS+=("$arg") ;;
  esac
done

# Recompile seulement si un source est plus récent que le binaire.
needs_build() {
  [ ! -f "$BIN" ] && return 0
  [ -n "$(find dotnet \( -name '*.cs' -o -name '*.axaml' -o -name '*.csproj' \) \
      -not -path '*/obj/*' -not -path '*/bin/*' -not -path '*/build/*' \
      -newer "$BIN" -print -quit)" ]
}

if [ "$NO_BUILD" -eq 0 ] && needs_build; then
  echo "Compilation ..."
  if ! "$DOTNET" build "$PROJECT" -c Release -o "$BUILD_DIR" --nologo -v minimal; then
    echo "Erreur : la compilation a échoué (voir les messages ci-dessus)." >&2
    exit 1
  fi
fi

if [ ! -f "$BIN" ]; then
  echo "Erreur : binaire introuvable ($BIN). Relancez sans --no-build." >&2
  exit 1
fi

echo "Lancement de 321 Back Office ..."
exec "$DOTNET" "$BIN" ${ARGS[@]+"${ARGS[@]}"}
