using Aphelion.Desktop.Application.Ports;

namespace Aphelion.Desktop.BrowserEngine;

/// <summary>
/// A download this application fetches itself with HttpClient, so pause, resume
/// and cancel work on every host — including those whose platform WebView never
/// raises a download event.
/// </summary>
internal sealed class HttpDownloadOperation : IEngineDownloadOperation, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private bool _paused;
    private bool _canceled;
    private bool _failed;
    private string? _failureReason;

    public HttpDownloadOperation(Uri source, string filePath)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? throw new ArgumentException("A file path is required.", nameof(filePath))
            : filePath;
    }

    public Uri? Source { get; }

    public string? FilePath { get; }

    public long BytesReceived { get; private set; }

    public long? TotalBytes { get; private set; }

    public bool CanResume => _paused && !_canceled && !_failed;

    public event EventHandler? ProgressChanged;

    public event EventHandler<EngineDownloadStateChangedEventArgs>? StateChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_worker is not null || _canceled)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _worker = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    public bool Pause()
    {
        lock (_gate)
        {
            if (_paused || _canceled || _failed || _worker is null)
            {
                return false;
            }

            _paused = true;
            _cts?.Cancel();
        }

        return true;
    }

    public bool ResumeDownload()
    {
        lock (_gate)
        {
            if (!_paused || _canceled || _failed)
            {
                return false;
            }

            _paused = false;
            _cts = new CancellationTokenSource();
            _worker = Task.Run(() => RunAsync(_cts.Token));
        }

        StateChanged?.Invoke(this, new EngineDownloadStateChangedEventArgs(EngineDownloadState.InProgress));
        return true;
    }

    public bool Cancel()
    {
        lock (_gate)
        {
            if (_canceled || _failed)
            {
                return false;
            }

            _canceled = true;
            _paused = false;
            _cts?.Cancel();
        }

        TryDeletePartial();
        StateChanged?.Invoke(this, new EngineDownloadStateChangedEventArgs(EngineDownloadState.Canceled));
        return true;
    }

    private async Task RunAsync(CancellationToken cancellation)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath!) ?? ".");

            var existing = File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0L;
            BytesReceived = existing;

            using var request = new HttpRequestMessage(HttpMethod.Get, Source);
            if (existing > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
            }

            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentRange?.Length is { } rangedTotal)
            {
                TotalBytes = rangedTotal;
            }
            else if (response.Content.Headers.ContentLength is { } length)
            {
                TotalBytes = existing + length;
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            await using var output = new FileStream(
                FilePath!,
                existing > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.Read);

            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                BytesReceived += read;
                ProgressChanged?.Invoke(this, EventArgs.Empty);
            }

            lock (_gate)
            {
                if (_canceled || _paused)
                {
                    return;
                }
            }

            StateChanged?.Invoke(this, new EngineDownloadStateChangedEventArgs(EngineDownloadState.Completed));
        }
        catch (OperationCanceledException)
        {
            if (_paused && !_canceled)
            {
                StateChanged?.Invoke(this, new EngineDownloadStateChangedEventArgs(EngineDownloadState.Paused));
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (_canceled || _paused)
                {
                    return;
                }

                _failed = true;
                _failureReason = exception.Message;
            }

            StateChanged?.Invoke(
                this,
                new EngineDownloadStateChangedEventArgs(EngineDownloadState.Failed, _failureReason));
        }
    }

    private void TryDeletePartial()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch (Exception)
        {
            // Leaving a partial file is preferable to throwing from Cancel.
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _http.Dispose();
    }
}
