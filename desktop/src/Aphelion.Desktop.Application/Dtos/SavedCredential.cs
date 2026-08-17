namespace Aphelion.Desktop.Application.Dtos;

/// <summary>
/// A password the user chose to keep on this device. Autofill into pages is a
/// separate, engine-level step and is not implied by storing a row here.
/// </summary>
public sealed record SavedCredential(string Host, string Username, string Password);
