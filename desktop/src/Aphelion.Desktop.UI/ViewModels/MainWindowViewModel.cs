namespace Aphelion.Desktop.UI.ViewModels;

public sealed class MainWindowViewModel(ShellViewModel shell) : ViewModelBase
{
    public ShellViewModel Shell { get; } = shell ?? throw new ArgumentNullException(nameof(shell));
}
