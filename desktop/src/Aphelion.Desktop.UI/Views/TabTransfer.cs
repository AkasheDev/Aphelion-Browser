using Aphelion.Desktop.Domain.ValueObjects;
using Aphelion.Desktop.UI.ViewModels;
using Avalonia;
using Avalonia.Controls;

namespace Aphelion.Desktop.UI.Views;

/// <summary>
/// Where a released tab drag lands: another window's strip, this window's strip,
/// or a window of its own.
/// </summary>
/// <remarks>
/// Shared by the strip and the overflow list so a tab dragged out of the list
/// behaves exactly as one dragged off the strip. Written once because the two
/// drifting apart is precisely what makes the list feel like a lesser place to
/// keep a tab.
/// </remarks>
internal static class TabTransfer
{
    /// <summary>
    /// Completes a drag released at <paramref name="screenPoint"/>.
    /// </summary>
    /// <param name="allowSameWindow">
    /// Whether a drop on the source window's own strip counts. The strip handler
    /// says no — it has already reordered the tab live as the pointer moved — while
    /// the overflow list says yes, since dropping a listed tab onto the strip is
    /// how it is put back in reach.
    /// </param>
    /// <returns>True when the tab moved.</returns>
    public static bool Complete(
        ShellViewModel shell,
        WindowManager manager,
        MainWindow owner,
        TabItemViewModel dragged,
        PixelPoint screenPoint,
        bool allowSameWindow)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(dragged);

        var target = manager.WindowAcceptingDropAt(
            screenPoint,
            exclude: allowSameWindow ? null : owner);

        if (target is not null)
        {
            var before = target.DropBeforeForScreenPoint(screenPoint);
            var group = target.GroupHintForScreenPoint(screenPoint);

            if (ReferenceEquals(target, owner))
            {
                shell.DropTab(dragged, before, group);
                return true;
            }

            if (!shell.TryExtractTransfer(dragged, out var transfer, out var sourceIsEmpty) ||
                transfer is null)
            {
                return false;
            }

            Adopt(target, transfer, before, group);

            // The last tab left with it, so the window it came from has nothing
            // to show.
            if (sourceIsEmpty)
            {
                owner.Close();
            }

            target.Activate();
            return true;
        }

        // A window with a single tab is already its own window; tearing it off
        // would close this one and open an identical replacement.
        if (shell.IsSingleTab)
        {
            return false;
        }

        if (!shell.TryExtractTransfer(dragged, out var torn, out _) || torn is null)
        {
            return false;
        }

        manager.TearOff(
            torn,
            new PixelPoint(screenPoint.X - 120, screenPoint.Y - 20),
            new Size(owner.Width, owner.Height));

        return true;
    }

    private static void Adopt(
        MainWindow target,
        TabTransferSnapshot transfer,
        TabItemViewModel? before,
        TabGroupId? group)
    {
        if (target.DataContext is MainWindowViewModel { Shell: { } shell })
        {
            shell.AdoptTab(transfer, before, group);
        }
    }
}
