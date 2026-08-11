using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.Application.UseCases;

/// <summary>Resolves and persists per-origin page zoom preferences.</summary>
public sealed class ManageSiteZoom(ISiteZoomStore store)
{
    private readonly ISiteZoomStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public PageZoom Resolve(PageAddress? address)
    {
        if (address is null)
        {
            return PageZoom.Default;
        }

        return _store.Load(OriginOf(address)) is { } percent
            ? PageZoom.FromPercent(percent)
            : PageZoom.Default;
    }

    public PageZoom Save(PageAddress? address, PageZoom zoom)
    {
        if (address is not null)
        {
            _store.Save(OriginOf(address), zoom.Percent);
        }

        return zoom;
    }

    private static string OriginOf(PageAddress address) =>
        address.Value.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
}
