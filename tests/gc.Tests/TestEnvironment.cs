using System.Runtime.CompilerServices;

namespace gc.Tests;

internal static class TestEnvironment
{
    // ClipboardService skips the real OS clipboard when GC_TEST_MODE=true.
    [ModuleInitializer]
    internal static void DisableClipboard() => Environment.SetEnvironmentVariable("GC_TEST_MODE", "true");
}
