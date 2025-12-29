using System.Text.Json;

namespace JitRealm.Mud.Persistence;

/// <summary>
/// JSON file-based persistence provider.
/// Saves world state to a single JSON file.
/// </summary>
public sealed class JsonPersistenceProvider : IPersistenceProvider
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _options;

    public JsonPersistenceProvider(string filePath)
    {
        _filePath = filePath;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task SaveAsync(WorldSaveData data)
    {
        // Ensure directory exists
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Write to temp file first, then rename for atomicity
        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, data, _options);
        }

        // Atomic replace
        File.Move(tempPath, _filePath, overwrite: true);
    }

    public async Task<WorldSaveData?> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return null;

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<WorldSaveData>(stream, _options);
    }

    public Task<bool> ExistsAsync()
    {
        return Task.FromResult(File.Exists(_filePath));
    }

    public Task DeleteAsync()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
        return Task.CompletedTask;
    }
}
