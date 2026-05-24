namespace gc.Domain.Interfaces;

public interface IConsole
{
    void WriteLine(string? value = null);
    void Write(string? value);
    void WriteErrorLine(string? value);
    string? ReadLine();
}
