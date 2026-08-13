namespace Aphelion.Desktop.Application.Ports;

/// <summary>
/// The user's choice for New Tab features that reach the network on their own —
/// without a search or an address typed in — before being asked to. Both default
/// to on, matching what shipped; this store only remembers a change once made.
/// </summary>
public sealed record PrivacyPreferences(bool WeatherEnabled, bool SearchSuggestionsEnabled)
{
    public static PrivacyPreferences Default { get; } = new(true, true);
}

public interface IPrivacyPreferenceStore
{
    PrivacyPreferences Load();

    void Save(PrivacyPreferences preferences);
}
