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
    /// Save the current world state.
    /// </summary>
    public async Task SaveAsync(WorldState state)
    {
        if (state.Objects is null)
            throw new InvalidOperationException("ObjectManager not initialized");

        var data = new WorldSaveData
        {
            Version = WorldSaveData.CurrentVersion,
            SavedAt = _clock.Now,

            Player = state.Player is not null
                ? new PlayerSaveData
                {
                    Name = state.Player.Name,
                    LocationId = state.Player.LocationId
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
    /// </summary>
    public async Task<bool> LoadAsync(WorldState state)
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

        // Restore player
        if (data.Player is not null)
        {
            state.Player = new Player(data.Player.Name)
            {
                LocationId = data.Player.LocationId
            };
        }

        // Restore instances
        await state.Objects.RestoreInstancesAsync(data.Instances, state);

        // Restore container registry
        state.Containers.FromSerializable(data.Containers?.Contents);

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
