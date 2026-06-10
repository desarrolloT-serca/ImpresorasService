#!/usr/bin/env bash
# check-repository-cleanliness.sh
# Detecta archivos que no deben estar en el repositorio.
# Uso: bash scripts/check-repository-cleanliness.sh
# Sale con codigo 1 si encuentra problemas.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ERRORS=0

red()    { printf '\033[0;31m%s\033[0m\n' "$*"; }
yellow() { printf '\033[0;33m%s\033[0m\n' "$*"; }
green()  { printf '\033[0;32m%s\033[0m\n' "$*"; }

echo "=== Verificacion de limpieza del repositorio ==="
echo "Raiz: $ROOT"
echo ""

# 1. Binarios de compilacion en el indice git
echo "--- Binarios de compilacion ---"
BINARIES=$(git -C "$ROOT" ls-files | grep -E '\.(dll|exe|pdb)$' | grep -v '\.nuget/' || true)
if [ -n "$BINARIES" ]; then
  red "ERROR: Binarios rastreados en git:"
  echo "$BINARIES"
  ERRORS=$((ERRORS + 1))
else
  green "OK: Sin binarios DLL/EXE/PDB rastreados"
fi

# 2. Directorios de compilacion
echo ""
echo "--- Directorios bin/ obj/ en el indice ---"
BUILD_DIRS=$(git -C "$ROOT" ls-files | grep -E '^[^/]+/(src|tests)/[^/]+/(bin|obj)/' || true)
if [ -n "$BUILD_DIRS" ]; then
  red "ERROR: Directorios de compilacion rastreados:"
  echo "$BUILD_DIRS" | head -10
  ERRORS=$((ERRORS + 1))
else
  green "OK: Sin directorios bin/obj rastreados"
fi

# 3. Paquetes NuGet
echo ""
echo "--- Cache NuGet (.nuget/packages/) ---"
NUGET=$(git -C "$ROOT" ls-files | grep -E '^[^/]+/\.nuget/' | head -1 || true)
if [ -n "$NUGET" ]; then
  red "ERROR: Paquetes NuGet rastreados en git (muestra):"
  git -C "$ROOT" ls-files | grep -E '^[^/]+/\.nuget/' | head -5
  ERRORS=$((ERRORS + 1))
else
  green "OK: Sin paquetes NuGet rastreados"
fi

# 4. Bases de datos SQLite
echo ""
echo "--- Bases de datos locales ---"
DATABASES=$(git -C "$ROOT" ls-files | grep -E '\.(db|sqlite|db-shm|db-wal)$' || true)
if [ -n "$DATABASES" ]; then
  red "ERROR: Bases de datos rastreadas en git:"
  echo "$DATABASES"
  ERRORS=$((ERRORS + 1))
else
  green "OK: Sin bases de datos rastreadas"
fi

# 5. Archivos .env con posibles secretos
echo ""
echo "--- Archivos .env (secretos) ---"
ENV_FILES=$(git -C "$ROOT" ls-files | grep -E '\.env$' || true)
if [ -n "$ENV_FILES" ]; then
  red "ERROR: Archivos .env rastreados (pueden contener secretos):"
  echo "$ENV_FILES"
  ERRORS=$((ERRORS + 1))
else
  green "OK: Sin archivos .env rastreados"
fi

# 6. Archivos de log
echo ""
echo "--- Archivos de log ---"
LOGS=$(git -C "$ROOT" ls-files | grep -E '\.log$' || true)
if [ -n "$LOGS" ]; then
  red "ERROR: Archivos .log rastreados:"
  echo "$LOGS"
  ERRORS=$((ERRORS + 1))
else
  green "OK: Sin archivos .log rastreados"
fi

# 7. Directorio templates/ (demo KeenThemes)
echo ""
echo "--- Directorio templates/ ---"
TEMPLATES=$(git -C "$ROOT" ls-files | grep -E '^[^/]+/templates/' | head -1 || true)
if [ -n "$TEMPLATES" ]; then
  red "ERROR: Directorio templates/ rastreado (demo KeenThemes, no debe versionar):"
  git -C "$ROOT" ls-files | grep -E '^[^/]+/templates/' | wc -l
  ERRORS=$((ERRORS + 1))
else
  green "OK: Sin templates/ rastreados"
fi

# 8. Cache PHPUnit
echo ""
echo "--- Cache de test ---"
PHPUNIT_CACHE=$(git -C "$ROOT" ls-files | grep '\.phpunit\.result\.cache' || true)
if [ -n "$PHPUNIT_CACHE" ]; then
  red "ERROR: Cache PHPUnit rastreada:"
  echo "$PHPUNIT_CACHE"
  ERRORS=$((ERRORS + 1))
else
  green "OK: Sin cache PHPUnit rastreada"
fi

# Resultado final
echo ""
echo "================================"
if [ "$ERRORS" -gt 0 ]; then
  red "RESULTADO: $ERRORS problema(s) encontrado(s). Ejecuta 'git rm --cached' para limpiar."
  exit 1
else
  green "RESULTADO: Repositorio limpio. Sin artefactos rastreados."
  exit 0
fi
