using JitRealm.Mud.Network;

namespace JitRealm.Mud.Persistence;

/// <summary>
/// Service for saving and loading complete world state.
/// Coordinates between WorldState, ObjectManager, and the persistence provider.
/// </summary>
public sealed class WorldStatePersistence
{
    private readonly IPersistenceProvider _provider;
    private readonly IClock _clock;

    public WorldStatePersistence(IPersistenceProvider provider, IClock? clock = null)
    {
        _provider = provider;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>
    /// Save the current world state with session data.
    /// </summary>
    public async Task SaveAsync(WorldState state, ISession? session = null)
    {
        if (state.Objects is null)
            throw new InvalidOperationException("ObjectManager not initialized");

        var data = new WorldSaveData
        {
            Version = WorldSaveData.CurrentVersion,
            SavedAt = _clock.Now,

            Session = session?.PlayerId is not null && session.PlayerName is not null
                ? new SessionSaveData
                {
                    PlayerId = session.PlayerId,
                    PlayerName = session.PlayerName
                }
                : null,

            Instances = state.Objects.ExportInstances(),

            Containers = new ContainerSaveData
            {
                Contents = state.Containers.ToSerializable()
            }
        };

        await _provider.SaveAsync(data);
    }

    /// <summary>
    /// Load world state from storage.
    /// Returns true if state was loaded, false if no saved state exists.
    /// If session is provided, restores the player association.
    /// </summary>
    public async Task<bool> LoadAsync(WorldState state, ISession? session = null)
    {
        if (state.Objects is null)
            throw new InvalidOperationException("ObjectManager not initialized");

        var data = await _provider.LoadAsync();
        if (data is null)
            return false;

        // Validate version
        if (data.Version > WorldSaveData.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Save file version {data.Version} is newer than supported version {WorldSaveData.CurrentVersion}");
        }

        // Clear existing state
        state.Objects.ClearAll(state);
        state.Containers.FromSerializable(null);

        // Restore instances (includes player world objects)
        await state.Objects.RestoreInstancesAsync(data.Instances, state);

        // Restore container registry
        state.Containers.FromSerializable(data.Containers?.Contents);

        // Restore session data if session provided
        if (session is not null && data.Session is not null)
        {
            session.PlayerId = data.Session.PlayerId;
            session.PlayerName = data.Session.PlayerName;
        }

        return true;
    }

    /// <summary>
    /// Check if saved state exists.
    /// </summary>
    public Task<bool> ExistsAsync() => _provider.ExistsAsync();

    /// <summary>
    /// Delete saved state.
    /// </summary>
    public Task DeleteAsync() => _provider.DeleteAsync();
}
