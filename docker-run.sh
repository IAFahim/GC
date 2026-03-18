#!/bin/bash

# Docker run script for Mac/Linux

echo "Running GC in Docker..."

if [ $# -eq 0 ]; then
    docker-compose run gc
else
    docker-compose run gc "$@"
fi
