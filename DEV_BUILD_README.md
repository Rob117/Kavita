# Development Build Guide

This document explains how to build Kavita using Docker, which provides a consistent build environment with .NET 8.0 SDK and Node.js 24.

## Prerequisites

- Docker installed and running

## Quick Start

```bash
# Build for linux-x64 (default)
./docker_image_build.sh

# Build for linux-arm64
ARM=true ./docker_image_build.sh
```

## Build Process

The build uses two Docker images:

### 1. Builder Image (`Dockerfile.builder`)

A development image containing:
- .NET 8.0 SDK
- Node.js 24 (LTS)

This image is built automatically on first run and cached for subsequent builds.

### 2. Runtime Image (`Dockerfile`)

The final production image containing only the compiled application.

## Build Script (`docker_image_build.sh`)

The script performs three steps:

1. **Build the builder image** (if not already cached)
2. **Compile Kavita** - Restores NuGet packages, builds the Angular UI, and publishes the .NET application
3. **Build the runtime image** - Creates the final Docker image

### Options

| Method | Command |
|--------|---------|
| Default (linux-x64) | `./docker_image_build.sh` |
| ARM64 via env var | `ARM=true ./docker_image_build.sh` |
| Specific runtime | `./docker_image_build.sh linux-arm64` |

### Supported Runtimes

- `linux-x64`
- `linux-arm64`
- `linux-arm`
- `linux-musl-x64`

## Output

After a successful build:

- **Tarball**: `_output/kavita-<runtime>.tar.gz`
- **Docker image**: `kavita`

## Manual Build Steps

If you need more control over the build process:

```bash
# 1. Build the builder image
docker build -f Dockerfile.builder -t kavita-builder .

# 2. Restore and build
docker run --rm -v "$(pwd)":/src kavita-builder \
    bash -c "dotnet restore /src/Kavita.sln && ./build.sh linux-x64"

# 3. Build runtime image
docker build -t kavita .
```

## Troubleshooting

### Package restore errors during clean

If you see `NETSDK1064: Package ... was not found` errors, ensure `dotnet restore` runs before `build.sh`:

```bash
docker run --rm -v "$(pwd)":/src kavita-builder \
    bash -c "dotnet restore /src/Kavita.sln && ./build.sh linux-x64"
```

### Rebuilding the builder image

To force a rebuild of the builder image:

```bash
docker rmi kavita-builder
./docker_image_build.sh
```
