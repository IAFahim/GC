#!/bin/bash
mkdir -p test-exclude-dir
cat << 'FILE' > test-exclude-dir/test.cs
// some comment
public class Test {
    // another comment
    public void Run() {
        
        System.Console.WriteLine("Hello");
    }
}
FILE

dotnet run --project src/gc.CLI/gc.CLI.csproj -- -p test-exclude-dir -o test-exclude-dir/out.md --exclude-line-if-start // --exclude-line-if-start \n --force

cat test-exclude-dir/out.md
