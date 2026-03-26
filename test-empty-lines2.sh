#!/bin/bash
mkdir -p test-empty-dir2
cat << 'FILE' > test-empty-dir2/test.cs

public class Test {
    public void Run() {
        
        System.Console.WriteLine("Hello");
    }
}
FILE

dotnet run --project src/gc.CLI/gc.CLI.csproj -- -p test-empty-dir2 -o test-empty-dir2/out.md --exclude-line-if-start // --exclude-line-if-start \n --force

cat test-empty-dir2/out.md
