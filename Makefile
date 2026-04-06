# Makefile for gc (Git Copy) - Cross-platform Docker commands
# Works on Windows (with WSL), Mac, and Linux

.PHONY: help build test run shell clean publish bench lint build-platform

# Default target
help:
	@echo "gc (Git Copy) - Docker Commands"
	@echo ""
	@echo "Usage:"
	@echo "  make build          - Build Docker image"
	@echo "  make test           - Run tests in Docker"
	@echo "  make run            - Run gc with default settings"
	@echo "  make shell          - Start interactive shell in container"
	@echo "  make clean          - Remove Docker images and containers"
	@echo "  make publish        - Build native AOT binary locally"
	@echo "  make bench          - Run benchmarks"
	@echo "  make lint           - Run dotnet format check"
	@echo ""
	@echo "Examples:"
	@echo "  make run ARGS='--extension cs --paths src'"
	@echo "  make run ARGS='--output /workspace/output.md'"

# Build Docker image
build:
	docker-compose build gc

# Run tests
test:
	docker-compose run --rm gc-test

# Run gc with custom arguments
run:
	docker-compose run --rm gc $(ARGS)

# Start interactive shell
shell:
	docker-compose run --rm gc-bash

# Clean up Docker resources
clean:
	docker-compose down -v
	docker system prune -f

# Build for specific platform (Windows, Mac, Linux)
build-platform:
	@echo "Building for $(PLATFORM)"
	docker buildx build --platform $(PLATFORM) -f Dockerfile.simple -t gc:latest .

# Publish native AOT binary locally
publish:
	dotnet publish src/gc.CLI/gc.CLI.csproj -c Release -o ./publish \
		-p:PublishAot=true \
		-p:StripSymbols=true

# Run benchmarks
bench:
	dotnet run --project src/gc.CLI/gc.CLI.csproj -c Release -- --benchmark

# Lint: check formatting without changing files
lint:
	dotnet format gc.sln --verify-no-changes --verbosity diagnostic
