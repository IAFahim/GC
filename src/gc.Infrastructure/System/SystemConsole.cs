using gc.Domain.Interfaces;

namespace gc.Infrastructure.System;

public sealed class SystemConsole : IConsole
{
    public void WriteLine(string? value = null)
    {
        if (value == null) Console.WriteLine();
        else Console.WriteLine(value);
    }

    public void Write(string? value)
    {
        Console.Write(value);
    }

    public void WriteErrorLine(string? value)
    {
        Console.Error.WriteLine(value);
    }

    public string? ReadLine()
    {
        return Console.ReadLine();
    }
}