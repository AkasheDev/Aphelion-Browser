using Aphelion.Desktop.Domain.ValueObjects;

namespace Aphelion.Desktop.UI.ViewModels;

/// <summary>
/// The portable part of a visible tab entry. Native engine surfaces cannot move
/// between windows, but both addresses of a split pair can be reconstructed.
/// </summary>
public sealed record TabTransferSnapshot(
    PageAddress? PrimaryAddress,
    PageAddress? PartnerAddress);
