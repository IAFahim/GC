#!/bin/bash

# Docker test script for Mac/Linux

echo "Running GC tests in Docker..."
docker-compose run --rm gc-test

if [ $? -eq 0 ]; then
    echo "✓ All tests passed!"
else
    echo "✗ Tests failed!"
    exit 1
fi
