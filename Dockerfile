# Multi-stage Dockerfile for GC (Git Copy)
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
COPY GC/GC.csproj GC/
COPY GC/Data/ GC/Data/
COPY GC/Utilities/ GC/Utilities/
COPY GC/Program.cs GC/

# Restore and build (without AOT for Docker compatibility)
RUN dotnet restore "GC/GC.csproj"
RUN dotnet publish "GC/GC.csproj" -c Release -o /app/publish

# Runtime image (smaller)
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app

# Install git
RUN apt-get update && apt-get install -y git && rm -rf /var/lib/apt/lists/*

# Copy published files
COPY --from=build /app/publish .

# Test entry point
ENTRYPOINT ["dotnet", "GC.dll"]
