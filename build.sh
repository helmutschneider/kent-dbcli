#!/usr/bin/env bash

set -o errexit   # abort on nonzero exitstatus
set -o nounset   # abort on unbound variable
set -o pipefail  # don't hide errors within pipes

VERSION="${1:-}"

if [ -z "${VERSION}" ]; then
  echo "Usage:"
  echo "  ./build.sh [version]"
  exit 1
fi

mkdir -p build/

for RUNTIME in "osx-arm64" "win-x64" "linux-x64"; do
  BUILD_NAME="kent-dbcli-${VERSION}-${RUNTIME}"
  BUILD_DIR="build/${BUILD_NAME}"
  dotnet clean
  dotnet publish \
    --configuration Release \
    --no-cache \
    --self-contained \
    --runtime "${RUNTIME}" \
    --output "${BUILD_DIR}"
  tar --exclude=".[^/]*" -czf "build/${BUILD_NAME}.tar.gz" -C "${BUILD_DIR}" .
done
