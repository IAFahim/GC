# Multi-stage Dockerfile for gc (Git Copy)
# Native AOT build for optimized performance and smaller image size

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Install build dependencies for Native AOT (if needed)
RUN apt-get update && apt-get install -y \
    clang \
    gcc \
    git \
    && rm -rf /var/lib/apt/lists/*

# Copy solution and project files
COPY gc.sln .
COPY src/gc.CLI/gc.CLI.csproj src/gc.CLI/
COPY src/gc.Application/gc.Application.csproj src/gc.Application/
COPY src/gc.Domain/gc.Domain.csproj src/gc.Domain/
COPY src/gc.Infrastructure/gc.Infrastructure.csproj src/gc.Infrastructure/

# Restore dependencies
RUN dotnet restore "gc.sln"

# Copy source code
COPY src/ src/

# Build and publish with Native AOT
RUN dotnet publish "src/gc.CLI/gc.CLI.csproj" -c Release -o /app/publish \
    -p:PublishAot=true \
    -p:StripSymbols=true

# Runtime image (Native AOT produces self-contained executable, no runtime needed)
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
WORKDIR /app

# Install git
RUN apt-get update && apt-get install -y git && rm -rf /var/lib/apt/lists/*

# Copy published files
COPY --from=build /app/publish .

# Native AOT produces a native executable, not a DLL
ENTRYPOINT ["./gc"]
