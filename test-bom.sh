#!/bin/bash
mkdir -p test-bom-dir
cat << 'FILE' > test-bom-dir/test.cs
﻿// <copyright file="BakerExtensions.cs" company="BovineLabs">
public class Test {
    public void Run() {
        System.Console.WriteLine("Hello");
    }
}
FILE

dotnet run --project src/gc.CLI/gc.CLI.csproj -- -p test-bom-dir -o test-bom-dir/out.md --exclude-line-if-start // --force

cat test-bom-dir/out.md
