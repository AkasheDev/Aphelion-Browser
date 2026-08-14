namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>Where a bookmark's context menu asked for its pages to be opened.</summary>
public enum BookmarkOpenTarget
{
    /// <summary>The tab already on screen.</summary>
    CurrentTab,

    NewTab,

    NewWindow,

    PrivateWindow,

    /// <summary>Beside the current page, in the second pane of a split.</summary>
    SplitPane,
}
