#!/bin/bash
mkdir -p test-empty-dir
cat << 'FILE' > test-empty-dir/test.cs
// some comment

public class Test {
    // another comment
    public void Run() {
        
        System.Console.WriteLine("Hello");
    }
}
FILE

dotnet run --project src/gc.CLI/gc.CLI.csproj -- -p test-empty-dir -o test-empty-dir/out.md --exclude-line-if-start // --exclude-line-if-start \n --force

cat test-empty-dir/out.md
