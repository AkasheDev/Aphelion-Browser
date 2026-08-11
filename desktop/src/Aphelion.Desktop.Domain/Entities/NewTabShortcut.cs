using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Domain.Entities;

/// <summary>
/// A launcher shown only on the New Tab page. It is deliberately independent
/// from bookmarks: launchers describe page layout, not the user's saved library.
/// </summary>
public sealed class NewTabShortcut
{
    public const int MaximumNameLength = 32;

    public NewTabShortcut(NewTabShortcutId id, string name, PageAddress address)
    {
        Id = id;
        Name = NormalizeName(name);
        Address = address ?? throw new ArgumentNullException(nameof(address));
    }

    public NewTabShortcutId Id { get; }

    public string Name { get; private set; }

    public PageAddress Address { get; private set; }

    public void Rename(string name) => Name = NormalizeName(name);

    public void Update(string name, PageAddress address)
    {
        Name = NormalizeName(name);
        Address = address ?? throw new ArgumentNullException(nameof(address));
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalized = name.Trim();

        if (normalized.Length > MaximumNameLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(name),
                $"Shortcut names cannot exceed {MaximumNameLength} characters.");
        }

        return normalized;
    }
}
