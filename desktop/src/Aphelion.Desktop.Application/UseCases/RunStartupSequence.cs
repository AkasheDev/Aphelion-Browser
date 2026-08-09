using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Application.UseCases;

/// <summary>Progress of the startup sequence, for the splash screen to display.</summary>
public sealed class StartupProgress(string description, int completed, int total)
{
    public string Description { get; } = description;

    public int Completed { get; } = completed;

    public int Total { get; } = total;

    /// <summary>Completion from 0 to 1. Zero total reads as complete, not as a divide by zero.</summary>
    public double Fraction => Total == 0 ? 1d : (double)Completed / Total;
}

/// <summary>
/// Runs every registered startup task in order and reports progress.
/// </summary>
public sealed class RunStartupSequence(IEnumerable<IStartupTask> tasks)
{
    private readonly IReadOnlyList<IStartupTask> _tasks = [.. tasks];

    public async Task ExecuteAsync(IProgress<StartupProgress>? progress, CancellationToken cancellationToken)
    {
        var total = _tasks.Count;
        var completed = 0;

        progress?.Report(new StartupProgress("Starting…", completed, total));

        foreach (var task in _tasks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new StartupProgress(task.Description, completed, total));

            await task.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            completed++;
            progress?.Report(new StartupProgress(task.Description, completed, total));
        }

        progress?.Report(new StartupProgress("Ready", total, total));
    }
}
