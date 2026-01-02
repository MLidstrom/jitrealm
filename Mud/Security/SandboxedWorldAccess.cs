namespace JitRealm.Mud.Security;

/// <summary>
/// Sandboxed implementation of world access that wraps WorldState
/// but only exposes safe, read-only operations.
/// </summary>
public sealed class SandboxedWorldAccess : ISandboxedWorldAccess
{
    private readonly WorldState _state;
    private readonly IClock _clock;

    public SandboxedWorldAccess(WorldState state, IClock clock)
    {
        _state = state;
        _clock = clock;
    }

    public T? GetObject<T>(string id) where T : class, IMudObject
    {
        return _state.Objects?.Get<T>(id);
    }

    public IEnumerable<string> ListObjectIds()
    {
        return _state.Objects?.ListLoadedIds() ?? Enumerable.Empty<string>();
    }

    public IReadOnlyCollection<string> GetRoomContents(string roomId)
    {
        return _state.Containers.GetContents(roomId);
    }

    public string? GetObjectLocation(string objectId)
    {
        return _state.Containers.GetContainer(objectId);
    }

    public IEnumerable<string> GetPlayersInRoom(string roomId)
    {
        // Get all sessions and check which players are in this room
        var sessions = _state.Sessions.GetSessionsInRoom(roomId, _state.Containers.GetContainer);
        foreach (var session in sessions)
        {
            if (session.PlayerName is not null)
            {
                yield return session.PlayerName;
            }
        }
    }

    public DateTimeOffset Now => _clock.Now;

    public IReadOnlyDictionary<EquipmentSlot, string> GetEquipment(string livingId)
    {
        return _state.Equipment.GetAllEquipped(livingId);
    }

    public string? GetEquippedInSlot(string livingId, EquipmentSlot slot)
    {
        return _state.Equipment.GetEquipped(livingId, slot);
    }

    public IStateStore? GetStateStore(string objectId)
    {
        return _state.Objects?.GetStateStore(objectId);
    }
}
