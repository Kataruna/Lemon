using System.Runtime.CompilerServices;

namespace Lemon.Core.Tests;

internal static class TestLog
{
    /// <summary>Tests break files on purpose; what that logs goes to the temp folder, not the user's errors.log.</summary>
    [ModuleInitializer]
    internal static void Redirect() =>
        ErrorLog.FilePath = Path.Combine(Path.GetTempPath(), "Lemon.Core.Tests", "errors.log");
}
