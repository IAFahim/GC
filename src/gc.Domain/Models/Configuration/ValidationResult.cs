using System.Text;

namespace gc.Domain.Models.Configuration;

public sealed record ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public override string ToString()
    {
        var sb = new StringBuilder();

        if (IsValid)
        {
            sb.AppendLine("✓ Configuration is valid");
            if (Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Warnings:");
                foreach (var warning in Warnings) sb.AppendLine($"  ⚠ {warning}");
            }
        }
        else
        {
            sb.AppendLine("✗ Configuration is invalid");
            sb.AppendLine();
            sb.AppendLine("Errors:");
            foreach (var error in Errors) sb.AppendLine($"  ✗ {error}");

            if (Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Warnings:");
                foreach (var warning in Warnings) sb.AppendLine($"  ⚠ {warning}");
            }
        }

        return sb.ToString();
    }
}
