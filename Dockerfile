# Multi-stage Dockerfile for gc (Git Copy)
# Simple version without Native AOT for better compatibility

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Install build dependencies for Native AOT (if needed)
RUN apt-get update && apt-get install -y \
    clang \
    gcc \
    git \
    && rm -rf /var/lib/apt/lists/*

# Copy project files
COPY gc/gc.csproj gc/
COPY gc/Data/ gc/Data/
COPY gc/Utilities/ gc/Utilities/
COPY gc/Program.cs gc/

# Restore and build (without AOT for Docker compatibility)
RUN dotnet restore "gc/gc.csproj"
RUN dotnet publish "gc/gc.csproj" -c Release -o /app/publish

# Runtime image (smaller)
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app

# Install git
RUN apt-get update && apt-get install -y git && rm -rf /var/lib/apt/lists/*

# Copy published files
COPY --from=build /app/publish .

# Test entry point
ENTRYPOINT ["dotnet", "gc.dll"]
