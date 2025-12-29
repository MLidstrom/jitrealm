namespace JitRealm.Mud.Persistence;

/// <summary>
/// Abstraction for saving and loading world state.
/// Allows different backends (JSON files, SQLite, etc.)
/// </summary>
public interface IPersistenceProvider
{
    /// <summary>
    /// Save world state to storage.
    /// </summary>
    Task SaveAsync(WorldSaveData data);

    /// <summary>
    /// Load world state from storage.
    /// Returns null if no saved state exists.
    /// </summary>
    Task<WorldSaveData?> LoadAsync();

    /// <summary>
    /// Check if saved state exists.
    /// </summary>
    Task<bool> ExistsAsync();

    /// <summary>
    /// Delete saved state.
    /// </summary>
    Task DeleteAsync();
}
