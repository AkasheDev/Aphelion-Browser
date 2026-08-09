using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.Infrastructure.Storage;

/// <summary>
/// Creates the profile directory before anything tries to read or write it.
/// </summary>
/// <remarks>
/// The first real startup task. Loading history, bookmarks and settings will be
/// added as sibling tasks once persistence exists.
/// </remarks>
public sealed class PrepareUserDataStartupTask(IUserDataLocation location) : IStartupTask
{
    private readonly IUserDataLocation _location =
        location ?? throw new ArgumentNullException(nameof(location));

    public string Description => "Preparing profile…";

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _location.EnsureCreated();
        return Task.CompletedTask;
    }
}
