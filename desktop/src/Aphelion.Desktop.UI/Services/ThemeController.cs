using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Enums;
using Avalonia;
using Avalonia.Styling;

namespace Aphelion.Desktop.UI.Services;

/// <summary>Applies the user's theme preference to the running application.</summary>
internal static class ThemeController
{
    public static void Apply(ThemePreference preference)
    {
        if (Avalonia.Application.Current is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    public static void Apply(IAppSettingsStore store) => Apply(store.Load().Theme);
}
