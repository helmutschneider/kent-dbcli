#!/usr/bin/env bash

set -o errexit   # abort on nonzero exitstatus
set -o nounset   # abort on unbound variable
set -o pipefail  # don't hide errors within pipes

# https://stackoverflow.com/a/246128
SCRIPT_DIR=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
VERSION="${1:-}"

if [ -z "${VERSION}" ]; then
  echo "Usage:"
  echo "  ./build.sh [version]"
  echo "  ./build.sh v4"
  exit 1
fi

rm -rf build/
mkdir -p build/

BUILD_PROJECT="src/Kent.DbCli.csproj"

for RUNTIME in "win-x64" "linux-x64" "osx-arm64"; do
  find . -type d \( -name 'bin' -or -name 'obj' \) | xargs rm -rf

  BUILD_NAME="kent-dbcli-${VERSION}-${RUNTIME}"
  BUILD_DIR="build/${BUILD_NAME}"

  dotnet publish \
    "${BUILD_PROJECT}" \
    --configuration Release \
    --no-cache \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:InformationalVersion="${VERSION}" \
    --runtime "${RUNTIME}" \
    --output "${BUILD_DIR}"

  cd "${BUILD_DIR}"

  if [ $RUNTIME = "win-x64" ]; then
    zip "../${BUILD_NAME}.zip" *
  else
    tar -czf "../${BUILD_NAME}.tar.gz" *
  fi

  cd "${SCRIPT_DIR}"
done
