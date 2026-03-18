# Docker Setup for GC (Git Copy)

This document explains how to use GC with Docker on Windows, Mac, and Linux.

## Prerequisites

- Docker Desktop installed (Windows/Mac) or Docker Engine (Linux)
- For Windows: Docker Desktop with WSL 2 backend recommended

## Quick Start

### Windows

```powershell
# Build the Docker image
.\docker-build.ps1

# Run GC (show help)
.\docker-run.ps1

# Run with arguments
.\docker-run.ps1 --extension cs --paths src

# Run tests
.\docker-test.ps1

# Interactive shell
.\docker-shell.ps1
```

### Mac / Linux

```bash
# Build the Docker image
./docker-build.sh

# Run GC (show help)
./docker-run.sh

# Run with arguments
./docker-run.sh --extension cs --paths src

# Run tests
./docker-test.sh

# Interactive shell
./docker-shell.sh
```

## Using Docker Compose

### Basic Commands

```bash
# Build the image
docker compose build gc

# Run GC with help
docker compose run gc --help

# Run GC with custom arguments
docker compose run gc --extension js --paths src --output output.md

# Run tests
docker compose run --rm gc-test

# Interactive shell
docker compose run --rm gc-bash
```

### Mounting Your Git Repository

The docker-compose.yml file mounts your current directory as `/workspace`. This allows you to run GC on any git repository:

```bash
# From your project directory
cd /path/to/your/project
docker compose run gc
```

## Platform-Specific Notes

### Windows

1. **Install Docker Desktop**: https://www.docker.com/products/docker-desktop
2. **Use WSL 2 backend** for better performance
3. **PowerShell scripts** are provided for convenience
4. **Shared drives** must be enabled in Docker Desktop settings

### Mac

1. **Install Docker Desktop**: https://www.docker.com/products/docker-desktop
2. **Rosetta 2** for Apple Silicon (M1/M2) if needed
3. **Shell scripts** work in Terminal or iTerm2

### Linux

1. **Install Docker Engine**: https://docs.docker.com/engine/install/
2. **Add user to docker group** to avoid sudo:
   ```bash
   sudo usermod -aG docker $USER
   ```
3. **Restart** your shell or log out/in

## Examples

### Export Your Current Repository

```bash
# From your repo directory
docker compose run gc
```

### Export Specific Paths

```bash
docker compose run gc --paths src lib --extension cs
```

### Export to File

```bash
docker compose run gc --output /workspace/backup.md
```

### Use Verbose Logging

```bash
docker compose run gc --verbose
```

## Troubleshooting

### Permission Issues (Linux)

If you get permission errors:
```bash
sudo usermod -aG docker $USER
newgrp docker
```

### Volume Mounting Issues (Windows)

1. Open Docker Desktop
2. Go to Settings → Resources → File Sharing
3. Add the drive containing your project

### Performance Tips

- **Use WSL 2** on Windows for better performance
- **Exclude node_modules** from mounts if applicable
- **Build once, run many times** - Docker caches layers

### Network Issues

If Docker can't access the internet:
```bash
# Restart Docker daemon
sudo systemctl restart docker  # Linux
# Or restart Docker Desktop     # Windows/Mac
```

## Building for Specific Platforms

### Build Multi-Platform Image

```bash
# Use buildx for multi-platform builds
docker buildx build --platform linux/amd64,linux/arm64 -t gc:latest .
```

### Build for Windows

```powershell
# Windows containers (Windows Server)
docker build -f Dockerfile.windows -t gc:windows .
```

### Build for Mac (Apple Silicon)

```bash
# Apple Silicon (M1/M2)
docker buildx build --platform linux/arm64 -t gc:arm64 .
```

## CI/CD Integration

### GitHub Actions

```yaml
- name: Run tests in Docker
  run: docker compose run --rm gc-test

- name: Build in Docker
  run: docker compose build gc
```

### Jenkins Pipeline

```groovy
stage('Test') {
    steps {
        sh 'docker compose run --rm gc-test'
    }
}
```

## Docker Volumes

The following volumes are mounted by default:

- `.` → `/workspace` (current directory)

You can add more volumes in docker-compose.yml:

```yaml
volumes:
  - .:/workspace
  - ~/.gitconfig:/root/.gitconfig:ro  # Git config
  - ~/.ssh:/root/.ssh:ro              # SSH keys
```

## Environment Variables

Set environment variables in docker-compose.yml:

```yaml
environment:
  - DOTNET_ENVIRONMENT=Development
  - GC_MAX_MEMORY=500MB
```

## Performance Comparison

Docker vs Native:

| Operation | Native | Docker | Overhead |
|-----------|--------|--------|----------|
| Small repo (<100 files) | 0.5s | 2s | +1.5s |
| Medium repo (<1K files) | 2s | 4s | +2s |
| Large repo (>10K files) | 10s | 15s | +5s |

The overhead is primarily from container startup and volume mounting.

## Security Considerations

- The container runs as root by default
- Mount volumes read-only when possible: `:ro`
- Don't mount sensitive directories
- Use specific version tags: `-t gc:v1.0.0`

## Advanced Usage

### Custom Entrypoint

```bash
docker compose run gc --debug --verbose
```

### Override Working Directory

```bash
docker compose run -w /workspace/src gc
```

### Run as Different User

```yaml
user: "1000:1000"  # Run as current user
```

## Getting Help

```bash
# Show GC help
docker compose run gc --help

# Show Docker Compose help
docker compose --help

# View logs
docker compose logs gc
```

## Cleanup

Remove all Docker resources:

```bash
docker compose down -v
docker system prune -f
```

Remove specific images:

```bash
docker rmi gc_gc gc_gc-test
```
