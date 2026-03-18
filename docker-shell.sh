#!/bin/bash

# Docker shell script for Mac/Linux

echo "Starting interactive shell in Docker container..."
echo "Type 'exit' to leave the shell"
echo ""

docker-compose run --rm gc-bash
