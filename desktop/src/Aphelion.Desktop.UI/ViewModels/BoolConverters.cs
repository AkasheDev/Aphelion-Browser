using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Aphelion.Desktop.UI.ViewModels;

public static class BoolConverters
{
    /// <summary>
    /// True while the window floats, so its edges can still be dragged. A maximised
    /// or full-screen window has no draggable edges.
    /// </summary>
    public static readonly IValueConverter IsResizableAtEdges =
        new FuncValueConverter<WindowState, bool>(state =>
            state is not (WindowState.Maximized or WindowState.FullScreen));

    /// <summary>True when the window fills the screen.</summary>
    public static readonly IValueConverter IsMaximized =
        new FuncValueConverter<WindowState, bool>(state =>
            state is WindowState.Maximized or WindowState.FullScreen);
}
