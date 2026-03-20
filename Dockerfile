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

# Copy project files
COPY src/gc.CLI/gc.CLI.csproj gc/
COPY gc/Data/ gc/Data/
COPY gc/Utilities/ gc/Utilities/
COPY gc/Program.cs gc/

# Restore and build with Native AOT for static executable
RUN dotnet restore "src/gc.CLI/gc.CLI.csproj"
RUN dotnet publish "src/gc.CLI/gc.CLI.csproj" -c Release -o /app/publish \
    -p:PublishAot=true \
    -p:StaticExecutable=true

# Runtime image (Native AOT produces self-contained executable, no runtime needed)
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
WORKDIR /app

# Install git
RUN apt-get update && apt-get install -y git && rm -rf /var/lib/apt/lists/*

# Copy published files
COPY --from=build /app/publish .

# Native AOT produces a native executable, not a DLL
ENTRYPOINT ["./gc"]
