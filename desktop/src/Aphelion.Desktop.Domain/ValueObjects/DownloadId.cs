namespace Aphelion.Desktop.Domain.ValueObjects;

/// <summary>
/// Identifies a download. A dedicated type rather than a bare Guid so a download
/// id cannot be passed where some other identifier is expected.
/// </summary>
public readonly record struct DownloadId(Guid Value)
{
    public static DownloadId New() => new(Guid.NewGuid());

    public static bool TryParse(string? text, out DownloadId id)
    {
        if (Guid.TryParse(text, out var value))
        {
            id = new DownloadId(value);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value.ToString("N");
}
