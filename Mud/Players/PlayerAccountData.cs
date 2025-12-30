using System.Text.Json;
using System.Text.Json.Serialization;

namespace JitRealm.Mud.Players;

/// <summary>
/// Data class for persistent player account storage.
/// Serialized to JSON in players/{first_letter}/{name}.json.
/// </summary>
public sealed class PlayerAccountData
{
    /// <summary>
    /// File format version for migration support.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Player's display name (case-preserved).
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Base64-encoded SHA256 hash of password + salt.
    /// </summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>
    /// Base64-encoded random salt (16 bytes).
    /// </summary>
    public string PasswordSalt { get; set; } = "";

    /// <summary>
    /// When the account was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the player last logged in.
    /// </summary>
    public DateTime LastLogin { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this player has wizard privileges.
    /// </summary>
    public bool IsWizard { get; set; }

    /// <summary>
    /// Player state variables (HP, XP, Level, etc.) from IStateStore.
    /// </summary>
    public Dictionary<string, JsonElement> State { get; set; } = new();

    /// <summary>
    /// Last known location (room ID).
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Item IDs in player inventory (object IDs with clone numbers).
    /// </summary>
    public List<string> Inventory { get; set; } = new();

    /// <summary>
    /// Equipment by slot name -> item ID.
    /// </summary>
    public Dictionary<string, string> Equipment { get; set; } = new();
}
