using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Aphelion.Desktop.UI.ViewModels;

namespace Aphelion.Desktop.UI.Views;

public partial class NewTabPage : UserControl
{
    public NewTabPage()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (IsVisible)
        {
            FocusSearch();
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != IsVisibleProperty || VisualRoot is null)
        {
            return;
        }

        if (change.GetNewValue<bool>())
        {
            FocusSearch();
        }
    }

    private void FocusSearch()
    {
        Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Loaded);
    }

    private void OnOpenShortcut(object? sender, RoutedEventArgs e)
        => ExecuteShortcutCommand(sender, static (viewModel, shortcut) =>
            viewModel.OpenShortcutCommand.Execute(shortcut));

    private void OnRenameShortcut(object? sender, RoutedEventArgs e)
        => ExecuteShortcutCommand(sender, static (viewModel, shortcut) =>
            viewModel.BeginRenameShortcutCommand.Execute(shortcut));

    private void OnRemoveShortcut(object? sender, RoutedEventArgs e)
        => ExecuteShortcutCommand(sender, static (viewModel, shortcut) =>
            viewModel.RemoveShortcutCommand.Execute(shortcut));

    private void OnAddShortcut(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewTabPageViewModel viewModel)
        {
            viewModel.BeginAddShortcutCommand.Execute(null);
        }
    }

    private void OnSearchEngineSelected(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewTabPageViewModel { SearchEngines: { } selector }
            && sender is Control { DataContext: SearchEngineOptionViewModel option })
        {
            selector.SelectCommand.Execute(option);
        }
    }

    private void ExecuteShortcutCommand(
        object? sender,
        Action<NewTabPageViewModel, NewTabShortcutViewModel> execute)
    {
        if (DataContext is NewTabPageViewModel viewModel
            && sender is Control { DataContext: NewTabShortcutViewModel shortcut })
        {
            execute(viewModel, shortcut);
        }
    }

}
