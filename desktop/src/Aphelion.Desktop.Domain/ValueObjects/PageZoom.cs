namespace Aphelion.Desktop.Domain.ValueObjects;

/// <summary>
/// A browser page zoom level expressed as a stable percentage rather than an
/// engine-specific scale factor.
/// </summary>
public readonly record struct PageZoom
{
    public const int MinimumPercent = 25;
    public const int MaximumPercent = 500;
    public const int DefaultPercent = 100;
    public const int StepPercent = 10;

    private PageZoom(int percent) => Percent = percent;

    public int Percent { get; }

    public double Factor => Percent / 100d;

    public static PageZoom Default { get; } = new(DefaultPercent);

    public static PageZoom FromPercent(int percent) =>
        new(Math.Clamp(percent, MinimumPercent, MaximumPercent));

    public PageZoom Increase() => FromPercent(Percent + StepPercent);

    public PageZoom Decrease() => FromPercent(Percent - StepPercent);
}
