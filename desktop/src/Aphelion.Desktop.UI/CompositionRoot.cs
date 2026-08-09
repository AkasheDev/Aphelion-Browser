using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Application.UseCases;
using Aphelion.Desktop.Infrastructure.Network;
using Aphelion.Desktop.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Aphelion.Desktop.UI;

/// <summary>
/// Single place where concrete implementations are bound to the ports declared by
/// the inner layers.
/// </summary>
/// <remarks>
/// <see cref="IBrowserEngineSession"/> is deliberately absent: an engine session
/// wraps a live native web view, so it is created by the view that owns that view
/// and handed to the view model. See ADR-0001.
/// </remarks>
internal static class CompositionRoot
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        // Infrastructure
        services.AddSingleton<ISearchQueryBuilder, DuckDuckGoSearchQueryBuilder>();

        // Application
        services.AddSingleton<NavigateFromAddressBar>();

        // Presentation
        services.AddSingleton<BrowserViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
