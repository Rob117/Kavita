#!/bin/bash
set -e

# Build a production AMD64 Docker image from an ARM Mac (or any platform)
# Usage: ./build_production_amd64.sh <tag>
# Example: ./build_production_amd64.sh spiritian/kavitapg:1.1.1

if [ -z "$1" ]; then
    echo "Usage: $0 <tag>"
    echo "Example: $0 spiritian/kavitapg:1.1.1"
    exit 1
fi

TAG="$1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "=== Building production AMD64 image: $TAG ==="

# Ensure buildx builder exists
if ! docker buildx inspect amd64builder >/dev/null 2>&1; then
    echo "=== Creating buildx builder ==="
    docker buildx create --use --name amd64builder
else
    docker buildx use amd64builder
fi

# Build the builder image if it doesn't exist
if ! docker image inspect kavita-builder >/dev/null 2>&1; then
    echo "=== Building kavita-builder image ==="
    docker build -f "$SCRIPT_DIR/Dockerfile.builder" -t kavita-builder "$SCRIPT_DIR"
fi

# Build Kavita for linux-x64
echo "=== Building Kavita binaries for linux-x64 ==="
docker run --rm -v "$SCRIPT_DIR":/src kavita-builder \
    bash -c "dotnet restore /src/Kavita.sln && ./build.sh linux-x64"

# Build the final AMD64 image using buildx
echo "=== Building AMD64 Docker image ==="
docker buildx build \
    --platform linux/amd64 \
    --build-arg TARGETPLATFORM="linux/amd64" \
    --load \
    -t "$TAG" \
    "$SCRIPT_DIR"

# Verify architecture
ARCH=$(docker inspect "$TAG" --format='{{.Architecture}}')
echo "=== Build complete ==="
echo "Image: $TAG"
echo "Architecture: $ARCH"

if [ "$ARCH" != "amd64" ]; then
    echo "WARNING: Architecture is not amd64!"
    exit 1
fi

echo ""
echo "To push to Docker Hub:"
echo "  docker push $TAG"
