using System.Text.Json;
using Aphelion.Desktop.Application.Ports;
using Aphelion.Desktop.Domain.Enums;

namespace Aphelion.Desktop.Infrastructure.Storage;

public sealed class JsonSearchEnginePreferenceStore(IUserDataLocation location)
    : ISearchEnginePreferenceStore
{
    private readonly IUserDataLocation _location =
        location ?? throw new ArgumentNullException(nameof(location));

    private string FilePath => Path.Combine(_location.RootDirectory, "search-engine.json");

    public SearchEngineKind? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var value = JsonSerializer.Deserialize<string>(File.ReadAllText(FilePath));

            return Enum.TryParse<SearchEngineKind>(value, ignoreCase: true, out var engine)
                && Enum.IsDefined(engine)
                    ? engine
                    : null;
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(SearchEngineKind engine)
    {
        try
        {
            _location.EnsureCreated();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(engine.ToString()));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A preference write failure must never block navigation.
        }
    }
}
