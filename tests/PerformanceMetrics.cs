using System.Diagnostics;

internal sealed record TimingSummary(int Samples, double P50Ms, double P95Ms, double MinMs, double MaxMs, double[] RawMs)
{
    public static TimingSummary From(IEnumerable<double> values)
    {
        var raw = values.ToArray(); var sorted = raw.Order().ToArray();
        if (sorted.Length == 0) throw new InvalidOperationException("No performance samples.");
        double Percentile(double p) => sorted[(int)Math.Ceiling(p * sorted.Length) - 1];
        return new(raw.Length, Percentile(.5), Percentile(.95), sorted[0], sorted[^1], raw);
    }
}
internal sealed class WorkingSetSampler : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task task;
    public long PeakBytes { get; private set; }
    public WorkingSetSampler()
    {
        task = Task.Run(async () =>
        {
            using var process = Process.GetCurrentProcess();
            try
            {
                while (true) { process.Refresh(); PeakBytes = Math.Max(PeakBytes, process.WorkingSet64); await Task.Delay(20, stop.Token); }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
    }
    public async ValueTask DisposeAsync() { await stop.CancelAsync(); await task; stop.Dispose(); }
}
