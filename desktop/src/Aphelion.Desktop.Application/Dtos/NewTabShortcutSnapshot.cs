namespace Aphelion.Desktop.Application.Dtos;

/// <summary>Serializable representation of one New Tab launcher.</summary>
public sealed record NewTabShortcutSnapshot(Guid Id, string Name, string Address);
