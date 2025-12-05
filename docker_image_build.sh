#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Use linux-arm64 if ARM=true, otherwise default to linux-x64
if [ "$ARM" = "true" ]; then
    RID="linux-arm64"
else
    RID="${1:-linux-x64}"
fi

echo "=== Building Kavita for $RID ==="

# Build the builder image if it doesn't exist
if ! docker image inspect kavita-builder >/dev/null 2>&1; then
    echo "=== Building kavita-builder image ==="
    docker build -f "$SCRIPT_DIR/Dockerfile.builder" -t kavita-builder "$SCRIPT_DIR"
fi

# Build Kavita
echo "=== Running build.sh in Docker ==="
docker run --rm -v "$SCRIPT_DIR":/src kavita-builder \
    bash -c "dotnet restore /src/Kavita.sln && ./build.sh $RID"

# Build the final runtime image
echo "=== Building final Docker image ==="
docker build -t kavita "$SCRIPT_DIR"

echo "=== Build complete ==="
echo "Output: $SCRIPT_DIR/_output/kavita-$RID.tar.gz"
echo "Docker image: kavita"
