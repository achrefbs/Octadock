#!/usr/bin/env bash
#
# build.sh - Build and test the CROSS-PLATFORM Octadock projects only.
#
# Octadock's WPF/Windows projects (Octadock.App, Octadock.Platform.Windows,
# Octadock.Cli) target net8.0-windows and can only be built on Windows, because
# that target framework needs the Windows SDK targeting pack. This script builds
# and tests just the cross-platform projects (Octadock.Core and Octadock.Data plus
# their test projects), which is what runs on Linux/macOS and Linux CI. Use
# build/build.ps1 on Windows to build the full solution.
#
# Usage:
#   build/build.sh [-c|--configuration <Debug|Release>] [--skip-tests]
#
set -euo pipefail

CONFIGURATION="Debug"
SKIP_TESTS=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    -c|--configuration)
      CONFIGURATION="$2"
      shift 2
      ;;
    --skip-tests)
      SKIP_TESTS=1
      shift
      ;;
    -h|--help)
      grep '^#' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

# Repo root is the parent of this script's directory.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ARTIFACTS_DIR="$REPO_ROOT/artifacts"

# CI parity: deterministic build, warnings as errors (see Directory.Build.props).
CI_ARGS="-p:ContinuousIntegrationBuild=true"

# The cross-platform (net8.0) projects that build on any OS.
CROSS_PLATFORM_PROJECTS=(
  "src/Octadock.Core/Octadock.Core.csproj"
  "src/Octadock.Data/Octadock.Data.csproj"
)
CROSS_PLATFORM_TESTS=(
  "tests/Octadock.Core.Tests/Octadock.Core.Tests.csproj"
  "tests/Octadock.Data.Tests/Octadock.Data.Tests.csproj"
)

echo "Octadock cross-platform build"
echo "  Configuration : $CONFIGURATION"
echo "  dotnet        : $(dotnet --version)"
echo "  NOTE: WPF/Windows projects (App, Platform.Windows, Cli) are SKIPPED on this OS."

for proj in "${CROSS_PLATFORM_PROJECTS[@]}"; do
  echo ""
  echo "==> Restore + build ($CONFIGURATION): $proj"
  dotnet build "$REPO_ROOT/$proj" -c "$CONFIGURATION" $CI_ARGS
done

if [[ "$SKIP_TESTS" -eq 0 ]]; then
  for test_proj in "${CROSS_PLATFORM_TESTS[@]}"; do
    echo ""
    echo "==> Test ($CONFIGURATION): $test_proj"
    dotnet test "$REPO_ROOT/$test_proj" -c "$CONFIGURATION" $CI_ARGS \
      --logger "trx" \
      --results-directory "$ARTIFACTS_DIR/test-results"
  done
else
  echo ""
  echo "==> Test (skipped)"
fi

echo ""
echo "Cross-platform build succeeded."
