using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// The portable part of a visible tab entry. Native engine surfaces cannot move
/// between windows, but both addresses of a split pair can be reconstructed.
/// </summary>
/// <param name="IsPrivate">
/// Whether the tab left a private window. A torn-off tab must open in another
/// private window; mixing it into an ordinary one would drop it out of private
/// mode, which is the whole point of tearing off from one.
/// </param>
public sealed record TabTransferSnapshot(
    PageAddress? PrimaryAddress,
    PageAddress? PartnerAddress,
    bool IsDownloadsPage = false,
    bool IsPrivate = false);
