using Aphelion.Desktop.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Aphelion.Desktop.UI;

/// <summary>
/// Single place where concrete implementations are bound to the ports declared by
/// the inner layers. Application, Domain, Infrastructure and BrowserEngine
/// registrations are added here as those layers gain real services.
/// </summary>
internal static class CompositionRoot
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
