#!/bin/bash

# Docker build script for Mac/Linux

echo "Building GC Docker image..."
docker-compose build gc

if [ $? -eq 0 ]; then
    echo "✓ Build successful!"
    echo ""
    echo "Run commands:"
    echo "  ./docker-run.sh            - Show help"
    echo "  ./docker-run.sh 'ARGS'     - Run with arguments"
    echo "  ./docker-test.sh           - Run tests"
    echo "  ./docker-shell.sh          - Interactive shell"
else
    echo "✗ Build failed!"
    exit 1
fi
