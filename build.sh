#!/usr/bin/env bash

set -o errexit   # abort on nonzero exitstatus
set -o nounset   # abort on unbound variable
set -o pipefail  # don't hide errors within pipes

RUNTIME="${1:-}"
VERSION="${2:-}"

if [ -z "${RUNTIME}" ] || [ -z "${VERSION}" ]; then
  echo "Usage:"
  echo "  ./build.sh [runtime] [version]"
  echo "  ./build.sh win-x64 v4"
  echo "  ./build.sh linux-x64 v4"
  echo "  ./build.sh osx-arm64 v4"
  exit 1
fi

mkdir -p build/

BUILD_NAME="kent-dbcli-${VERSION}-${RUNTIME}"
BUILD_DIR="build/${BUILD_NAME}"

dotnet clean
dotnet publish \
  --configuration Release \
  --no-cache \
  --self-contained \
  -p:PublishSingleFile=true \
  --runtime "${RUNTIME}" \
  --output "${BUILD_DIR}"

cd "${BUILD_DIR}"

tar -czf "../${BUILD_NAME}.tar.gz" *
