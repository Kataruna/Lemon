namespace Lemon.Core.Tests;

/// <summary>Unlike Progress&lt;T&gt;, reports synchronously so tests see every value before the await returns.</summary>
public sealed class SyncProgress(Action<double> report) : IProgress<double>
{
    public void Report(double value) => report(value);
}
