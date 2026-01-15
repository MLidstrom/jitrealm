using System.Text.Json;

namespace JitRealm.Mud;

/// <summary>
/// Manages player bans with JSON file persistence.
/// </summary>
public sealed class BanManager
{
    private readonly string _banFilePath;
    private readonly object _lock = new();
    private Dictionary<string, BanRecord> _bans = new(StringComparer.OrdinalIgnoreCase);

    public BanManager(string saveDirectory)
    {
        _banFilePath = Path.Combine(saveDirectory, "banned_players.json");
        Load();
    }

    /// <summary>
    /// Check if a player name is banned.
    /// </summary>
    public bool IsBanned(string playerName)
    {
        lock (_lock)
        {
            return _bans.ContainsKey(playerName.ToLowerInvariant());
        }
    }

    /// <summary>
    /// Get ban record for a player, or null if not banned.
    /// </summary>
    public BanRecord? GetBan(string playerName)
    {
        lock (_lock)
        {
            return _bans.TryGetValue(playerName.ToLowerInvariant(), out var record) ? record : null;
        }
    }

    /// <summary>
    /// Ban a player.
    /// </summary>
    public void Ban(string playerName, string bannedBy, string? reason = null)
    {
        lock (_lock)
        {
            var record = new BanRecord(
                playerName.ToLowerInvariant(),
                bannedBy,
                reason ?? "No reason given",
                DateTime.UtcNow
            );
            _bans[playerName.ToLowerInvariant()] = record;
            Save();
        }
    }

    /// <summary>
    /// Unban a player.
    /// </summary>
    public bool Unban(string playerName)
    {
        lock (_lock)
        {
            var removed = _bans.Remove(playerName.ToLowerInvariant());
            if (removed)
            {
                Save();
            }
            return removed;
        }
    }

    /// <summary>
    /// Get all current bans.
    /// </summary>
    public IReadOnlyList<BanRecord> GetAllBans()
    {
        lock (_lock)
        {
            return _bans.Values.ToList();
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_banFilePath))
            {
                var json = File.ReadAllText(_banFilePath);
                var records = JsonSerializer.Deserialize<List<BanRecord>>(json);
                if (records is not null)
                {
                    _bans = records.ToDictionary(r => r.PlayerName, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // If we can't load, start fresh
            _bans = new Dictionary<string, BanRecord>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_banFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_bans.Values.ToList(), new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_banFilePath, json);
        }
        catch
        {
            // Log error but don't crash
        }
    }
}

/// <summary>
/// Record of a player ban.
/// </summary>
public sealed record BanRecord(
    string PlayerName,
    string BannedBy,
    string Reason,
    DateTime BannedAt
);
