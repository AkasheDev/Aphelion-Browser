using System.Globalization;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Entities;
using Aphelion.Desktop.Domain.Enums;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// One row of the downloads panel: a live transfer with pause, resume and
/// cancel, or a finished one with open, reveal and remove.
/// </summary>
/// <remarks>
/// The domain <see cref="DownloadItem"/> owns the state rules; the engine
/// operation, when there is one, is only asked to act, and the state follows
/// from what the engine then reports. A restored history row has no operation —
/// its transfer belongs to a process that no longer exists — so it offers only
/// the file actions.
/// </remarks>
public sealed partial class DownloadItemViewModel : ViewModelBase
{
    /// <summary>How often at most the progress text and bar repaint.</summary>
    private static readonly TimeSpan ProgressUpdateInterval = TimeSpan.FromMilliseconds(250);

    private readonly DownloadItem _item;
    private readonly IFileExplorer? _files;
    private readonly Action<DownloadItemViewModel> _stateChanged;
    private readonly Action<DownloadItemViewModel> _progressed;
    private readonly Action<DownloadItemViewModel> _removeRequested;
    private IEngineDownloadOperation? _operation;

    private DateTimeOffset _speedAnchorTime;
    private long _speedAnchorBytes;
    private double _bytesPerSecond;
    private DateTimeOffset _lastRefresh;

    public DownloadItemViewModel(
        DownloadItem item,
        IEngineDownloadOperation? operation,
        IFileExplorer? files,
        Action<DownloadItemViewModel> stateChanged,
        Action<DownloadItemViewModel> progressed,
        Action<DownloadItemViewModel> removeRequested,
        bool isPrivate = false)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _files = files;
        _stateChanged = stateChanged ?? throw new ArgumentNullException(nameof(stateChanged));
        _progressed = progressed ?? throw new ArgumentNullException(nameof(progressed));
        _removeRequested = removeRequested ?? throw new ArgumentNullException(nameof(removeRequested));
        IsPrivate = isPrivate;

        _speedAnchorTime = DateTimeOffset.Now;
        _speedAnchorBytes = item.ReceivedBytes;

        if (operation is not null && _item.IsLive)
        {
            _operation = operation;
            _operation.ProgressChanged += OnEngineProgressChanged;
            _operation.StateChanged += OnEngineStateChanged;
        }
    }

    /// <summary>The domain record behind this row, read when persisting.</summary>
    public DownloadItem Item => _item;

    /// <summary>A private window's download: shown, but never persisted.</summary>
    public bool IsPrivate { get; }

    public string FileName => _item.FileName;

    public string FilePath => _item.FilePath;

    /// <summary>The site the file came from, as Chrome shows under the name.</summary>
    public string SourceHost =>
        string.IsNullOrEmpty(_item.Source.Host) ? _item.Source.ToString() : _item.Source.Host;

    /// <summary>
    /// The extension, shown on the row's tile. Chrome draws a real file-type
    /// icon; the extension itself carries the same information and needs no
    /// icon set to keep in step with the formats people actually download.
    /// </summary>
    public string TypeBadge
    {
        get
        {
            var extension = Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();

            return extension.Length switch
            {
                0 => "FILE",
                > 4 => extension[..4],
                _ => extension,
            };
        }
    }

    /// <summary>
    /// Tile tints, grouped rather than per-extension: five families the eye can
    /// tell apart at a glance beat forty colours it cannot. Driven as style
    /// classes, so the palette stays in the theme with every other colour.
    /// </summary>
    public bool IsImageKind => KindOf(TypeBadge) == "image";

    public bool IsMediaKind => KindOf(TypeBadge) == "media";

    public bool IsArchiveKind => KindOf(TypeBadge) == "archive";

    public bool IsDocumentKind => KindOf(TypeBadge) == "document";

    public bool IsCodeKind => KindOf(TypeBadge) == "code";

    private static string KindOf(string extension) => extension switch
    {
        "PNG" or "JPG" or "JPEG" or "GIF" or "WEBP" or "SVG" or "BMP" or "HEIC" or "AVIF" => "image",
        "MP4" or "MOV" or "MKV" or "AVI" or "WEBM" or "MP3" or "WAV" or "FLAC" or "M4A" or "OGG" => "media",
        "ZIP" or "RAR" or "7Z" or "TAR" or "GZ" or "ISO" or "MSI" or "EXE" or "DMG" or "APPX" => "archive",
        "PDF" or "DOC" or "DOCX" or "XLS" or "XLSX" or "PPT" or "PPTX" or "TXT" or "MD" or "CSV" => "document",
        "JSON" or "XML" or "HTML" or "CSS" or "JS" or "TS" or "CS" or "PY" or "SH" or "YML" => "code",
        _ => "other",
    };

    /// <summary>The percentage beside the bar, blank until the size is known.</summary>
    public string ProgressLabel =>
        _item.ProgressFraction is { } fraction ? $"{fraction * 100:0}%" : string.Empty;

    public bool IsLive => _item.IsLive;

    public bool CanPause => _item.State == DownloadState.InProgress && _operation is not null;

    public bool CanResume => _item.State == DownloadState.Paused && _operation is not null;

    public bool CanCancel => IsLive && _operation is not null;

    public bool CanRemove => !IsLive;

    /// <summary>Opening only makes sense for a completed file still on disk.</summary>
    public bool CanOpenFile => _item.State == DownloadState.Completed && File.Exists(_item.FilePath);

    public bool ShowsProgress => IsLive;

    public bool IsProgressIndeterminate => IsLive && _item.ProgressFraction is null;

    public double ProgressPercent => (_item.ProgressFraction ?? 0d) * 100d;

    public string StatusText => _item.State switch
    {
        DownloadState.InProgress => FormatLiveStatus(paused: false),
        DownloadState.Paused => FormatLiveStatus(paused: true),
        DownloadState.Completed when !File.Exists(_item.FilePath) => "Deleted from disk",
        DownloadState.Completed => $"{FormatBytes(_item.TotalBytes ?? _item.ReceivedBytes)} • {SourceHost}",
        DownloadState.Canceled => "Canceled",
        DownloadState.Failed => _item.FailureReason ?? "Download failed",
        _ => string.Empty,
    };

    [RelayCommand]
    private void Pause()
    {
        // State follows from the engine's report rather than being assumed
        // here; see OnEngineStateChanged.
        _operation?.Pause();
    }

    [RelayCommand]
    private void Resume() => _operation?.ResumeDownload();

    [RelayCommand]
    private void Cancel() => _operation?.Cancel();

    [RelayCommand]
    private void OpenFile()
    {
        if (_files?.OpenFile(_item.FilePath) != true)
        {
            // The file may have been deleted since the last refresh; make the
            // row say so instead of a button that silently does nothing.
            Refresh();
        }
    }

    [RelayCommand]
    private void RevealInFolder() => _files?.RevealInFolder(_item.FilePath);

    [RelayCommand]
    private void Remove() => _removeRequested(this);

    /// <summary>Stops the live transfer, used when its row is being removed.</summary>
    public void CancelIfLive()
    {
        if (IsLive)
        {
            _operation?.Cancel();

            // The engine may already be gone, in which case no event will ever
            // arrive; the domain still has to leave the live state.
            if (_item.Cancel())
            {
                DetachOperation();
            }
        }
    }

    private void OnEngineProgressChanged(object? sender, EventArgs e) =>
        OnUiThread(() =>
        {
            if (sender is not IEngineDownloadOperation operation ||
                !ReferenceEquals(operation, _operation))
            {
                return;
            }

            _item.ReportProgress(operation.BytesReceived, operation.TotalBytes);

            var now = DateTimeOffset.Now;

            // Repainting on every network buffer is wasteful and unreadable;
            // Chrome's row also updates a few times a second at most.
            if (now - _lastRefresh < ProgressUpdateInterval)
            {
                return;
            }

            UpdateSpeed(now);
            _lastRefresh = now;
            Refresh();
            _progressed(this);
        });

    private void OnEngineStateChanged(object? sender, EngineDownloadStateChangedEventArgs e) =>
        OnUiThread(() =>
        {
            if (sender is not IEngineDownloadOperation operation ||
                !ReferenceEquals(operation, _operation))
            {
                return;
            }

            _item.ReportProgress(operation.BytesReceived, operation.TotalBytes);

            var transitioned = e.State switch
            {
                EngineDownloadState.InProgress => _item.Resume(),
                EngineDownloadState.Paused => _item.Pause(),
                EngineDownloadState.Completed => _item.Complete(),
                EngineDownloadState.Canceled => _item.Cancel(),
                EngineDownloadState.Failed => _item.Fail(e.FailureReason),
                _ => false,
            };

            if (!transitioned)
            {
                return;
            }

            if (!_item.IsLive)
            {
                DetachOperation();
            }

            _bytesPerSecond = 0;
            _speedAnchorTime = DateTimeOffset.Now;
            _speedAnchorBytes = _item.ReceivedBytes;

            Refresh();
            _stateChanged(this);
        });

    private void DetachOperation()
    {
        if (_operation is null)
        {
            return;
        }

        _operation.ProgressChanged -= OnEngineProgressChanged;
        _operation.StateChanged -= OnEngineStateChanged;
        _operation = null;
    }

    private void UpdateSpeed(DateTimeOffset now)
    {
        var elapsed = (now - _speedAnchorTime).TotalSeconds;

        if (elapsed < 0.5)
        {
            return;
        }

        _bytesPerSecond = Math.Max(0, (_item.ReceivedBytes - _speedAnchorBytes) / elapsed);
        _speedAnchorTime = now;
        _speedAnchorBytes = _item.ReceivedBytes;
    }

    /// <summary>Raises every derived property; state rows change wholesale.</summary>
    private void Refresh()
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanOpenFile));
        OnPropertyChanged(nameof(ShowsProgress));
        OnPropertyChanged(nameof(IsProgressIndeterminate));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(StatusText));
    }

    private string FormatLiveStatus(bool paused)
    {
        var received = FormatBytes(_item.ReceivedBytes);
        var progress = _item.TotalBytes is { } total
            ? $"{received} / {FormatBytes(total)}"
            : received;

        if (paused)
        {
            return $"Paused • {progress}";
        }

        return _bytesPerSecond >= 1
            ? $"{progress} ({FormatBytes((long)_bytesPerSecond)}/s)"
            : progress;
    }

    /// <summary>"532 KB", "2.4 MB" — one decimal from megabytes up, as Chrome does.</summary>
    private static string FormatBytes(long bytes)
    {
        const double kilo = 1024d;

        return bytes switch
        {
            < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
            < 1024 * 1024 => string.Create(
                CultureInfo.InvariantCulture, $"{bytes / kilo:0} KB"),
            < 1024L * 1024 * 1024 => string.Create(
                CultureInfo.InvariantCulture, $"{bytes / (kilo * kilo):0.#} MB"),
            _ => string.Create(
                CultureInfo.InvariantCulture, $"{bytes / (kilo * kilo * kilo):0.##} GB"),
        };
    }

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
