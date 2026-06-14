using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace TestBrainCrusher;

public static class Program
{
    public static void Main()
    {
        // Test basic functionality
        var crusher = new BrainCrusher(".cs");
        var input = "public class Foo { // comment\n    int x = 5; }";
        var result = crusher.Crush(input);
        Console.WriteLine($"Result: {result}");
        Console.WriteLine(!result.Contains("comment") ? "PASS: Comment removed" : "FAIL: Comment still present");
    }
}
