using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;

namespace gc.CLI.Services;

public static class HistoryMenu
{
    public static async Task<int> ShowAsync(
        IHistoryService historyService,
        CliParser parser,
        GcConfiguration config,
        int? preselectedIndex,
        IConsole console,
        CancellationToken ct)
    {
        var historyResult = await historyService.GetHistoryAsync(ct);

        if (!historyResult.IsSuccess)
        {
            console.WriteLine($"Error loading history: {historyResult.Error}");
            return 1;
        }

        var history = historyResult.Value!;

        if (history.Count == 0)
        {
            console.WriteLine("No history found.");
            return 0;
        }

        PrintHistory(history, console);

        if (preselectedIndex.HasValue)
        {
            if (preselectedIndex.Value < 1 || preselectedIndex.Value > history.Count)
            {
                console.WriteLine($"Invalid history index: {preselectedIndex.Value} (valid: 1-{history.Count})");
                return 1;
            }

            return await ExecuteFromHistoryAsync(
                historyService, parser, config,
                history[preselectedIndex.Value - 1], console, ct);
        }

        console.Write("Select a number to run (Enter to cancel): ");
        var input = console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input) || !int.TryParse(input, out var selectedIndex) ||
            selectedIndex < 1 || selectedIndex > history.Count)
        {
            return 0;
        }

        return await ExecuteFromHistoryAsync(
            historyService, parser, config,
            history[selectedIndex - 1], console, ct);
    }

    private static void PrintHistory(IReadOnlyList<HistoryEntry> history, IConsole console)
    {
        console.WriteLine();
        console.WriteLine("Recent GC Runs:");
        console.WriteLine();

        for (var i = 0; i < history.Count; i++)
        {
            var entry = history[i];
            var relativeTime = Formatting.FormatRelativeTime(entry.LastRun);
            var args = entry.Arguments.Length > 0
                ? string.Join(" ", entry.Arguments)
                : "(no args)";

            console.WriteLine($"  [{i + 1}] {entry.Directory} ({relativeTime})");
            console.WriteLine($"      args: {args}");
        }

        console.WriteLine();
    }

    private static async Task<int> ExecuteFromHistoryAsync(
        IHistoryService historyService,
        CliParser parser,
        GcConfiguration config,
        HistoryEntry entry,
        IConsole console,
        CancellationToken ct)
    {
        if (!Directory.Exists(entry.Directory))
        {
            console.WriteLine($"Directory no longer exists: {entry.Directory}");
            return 1;
        }

        console.WriteLine($"\nRe-running: gc {string.Join(" ", entry.Arguments)}");
        console.WriteLine($"Directory:  {entry.Directory}\n");

        var cleanArgs = entry.Arguments
            .Where(a => !a.Equals("--history", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var parseResult = parser.Parse(cleanArgs, config);
        if (!parseResult.IsSuccess)
        {
            console.WriteLine($"Failed to parse saved arguments: {parseResult.Error}");
            return 1;
        }

        var cliArgs = parseResult.Value!;

        var logger = new gc.Infrastructure.Logging.ConsoleLogger(null, console);
        var discovery = new gc.Infrastructure.Discovery.FileDiscovery(logger);
        var filter = new gc.Application.Services.FileFilter(logger);
        var contentFilter = new gc.Application.Services.ContentFilter(logger);
        var reader = new gc.Infrastructure.IO.FileReader(logger);
        var generator = new gc.Application.Services.MarkdownGenerator(logger, reader);
        var clipboard = new gc.Infrastructure.System.ClipboardService(logger);
        var useCase = new gc.Application.UseCases.GenerateContextUseCase(discovery, filter, contentFilter, reader, generator, clipboard, logger);
        var validator = new gc.Application.Validators.ConfigurationValidator();
        var configService = new gc.Application.Services.ConfigurationService(logger, validator);

        var exitCode = await Program.ExecuteAsync(entry.Directory, cliArgs, config, useCase, configService, logger, ct);

        if (exitCode == 0)
        {
            await historyService.AddEntryAsync(entry.Directory, entry.Arguments, ct);
        }

        return exitCode;
    }
}
