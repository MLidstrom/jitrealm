using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JitRealm.Mud.Persistence;

namespace JitRealm.Mud;

/// <summary>
/// Manages runtime compilation and loading of world objects.
/// Separates blueprints (compiled code) from instances (runtime objects with state).
/// </summary>
public sealed class ObjectManager
{
    private readonly string _worldRoot;
    private readonly IClock _clock;

    // Blueprint cache: blueprintPath -> handle
    private readonly Dictionary<string, BlueprintHandle> _blueprints =
        new(StringComparer.OrdinalIgnoreCase);

    // Instance registry: instanceId -> handle
    private readonly Dictionary<string, InstanceHandle> _instances =
        new(StringComparer.OrdinalIgnoreCase);

    public ObjectManager(string worldRoot, IClock? clock = null)
    {
        _worldRoot = worldRoot;
        _clock = clock ?? new SystemClock();
    }

    // === Public API ===

    public IEnumerable<string> ListBlueprintIds() => _blueprints.Keys.OrderBy(x => x);
    public IEnumerable<string> ListInstanceIds() => _instances.Keys.OrderBy(x => x);

    /// <summary>
    /// Get all loaded IDs (both blueprints acting as singletons and instances).
    /// For backward compatibility with existing code.
    /// </summary>
    public IEnumerable<string> ListLoadedIds() => _instances.Keys.OrderBy(x => x);

    /// <summary>
    /// Get an existing instance by ID.
    /// Returns null if not found.
    /// </summary>
    public T? Get<T>(string id) where T : class, IMudObject
    {
        id = ObjectId.Normalize(id);
        return _instances.TryGetValue(id, out var handle) ? handle.Instance as T : null;
    }

    /// <summary>
    /// Load a blueprint and create a singleton instance (legacy behavior).
    /// If already loaded, returns existing instance.
    /// The instance ID matches the blueprint ID.
    /// </summary>
    public async Task<T> LoadAsync<T>(string blueprintId, WorldState state) where T : class, IMudObject
    {
        blueprintId = ObjectId.Normalize(blueprintId);

        // If instance exists with this exact ID, return it
        if (_instances.TryGetValue(blueprintId, out var existing))
        {
            return existing.Instance as T
                ?? throw new InvalidOperationException($"Instance {blueprintId} is not {typeof(T).Name}");
        }

        // Ensure blueprint is compiled
        var blueprint = await EnsureBlueprintAsync(blueprintId);

        // Create singleton instance (legacy mode - ID matches blueprint)
        var instance = await CreateInstanceInternalAsync(blueprint, blueprintId, state);

        return instance.Instance as T
            ?? throw new InvalidOperationException($"Instance {blueprintId} is not {typeof(T).Name}");
    }

    /// <summary>
    /// Clone a blueprint to create a new instance with unique ID.
    /// </summary>
    public async Task<T> CloneAsync<T>(string blueprintId, WorldState state) where T : class, IMudObject
    {
        blueprintId = ObjectId.Normalize(blueprintId);

        var blueprint = await EnsureBlueprintAsync(blueprintId);
        var cloneNum = blueprint.GetNextCloneNumber();
        var instanceId = new ObjectId(blueprintId, cloneNum).ToString();

        var instance = await CreateInstanceInternalAsync(blueprint, instanceId, state);

        return instance.Instance as T
            ?? throw new InvalidOperationException($"Instance {instanceId} is not {typeof(T).Name}");
    }

    /// <summary>
    /// Reload a blueprint, updating all instances that use it.
    /// State is preserved via IStateStore.
    /// </summary>
    public async Task ReloadBlueprintAsync(string blueprintId, WorldState state)
    {
        blueprintId = ObjectId.Normalize(blueprintId);

        if (!_blueprints.TryGetValue(blueprintId, out var oldBlueprint))
        {
            // Not loaded, nothing to reload
            return;
        }

        // Collect instances using this blueprint
        var affectedInstances = _instances.Values
            .Where(i => i.Blueprint == oldBlueprint)
            .ToList();

        // Compile new blueprint
        var newBlueprint = await CompileBlueprintAsync(blueprintId);
        newBlueprint.InstanceCount = affectedInstances.Count;

        // Swap each instance
        foreach (var oldHandle in affectedInstances)
        {
            var instanceId = oldHandle.Id.ToString();
            var newInstance = CreateObjectInstance(newBlueprint, instanceId);

            // Build context with preserved state
            var ctx = new MudContext
            {
                World = state,
                State = oldHandle.State, // Preserve state!
                Clock = _clock,
                CurrentObjectId = instanceId,
                RoomId = instanceId // For rooms, their own ID is the room ID
            };

            // Call lifecycle hooks - prefer IOnReload during reload
            var oldTypeName = oldHandle.Instance.GetType().FullName ?? "Unknown";
            if (newInstance is IOnReload onReload)
            {
                onReload.OnReload(ctx, oldTypeName);
            }
            else if (newInstance is IOnLoad onLoad)
            {
                onLoad.OnLoad(ctx);
            }
            else
            {
                // Fall back to legacy Create
                newInstance.Create(state);
            }

            // Update registry
            _instances[instanceId] = new InstanceHandle
            {
                Id = oldHandle.Id,
                Blueprint = newBlueprint,
                Instance = newInstance,
                State = oldHandle.State,
                CreatedAt = oldHandle.CreatedAt // Preserve creation time
            };

            // Update heartbeat registration
            state.Heartbeats.Unregister(instanceId);
            if (newInstance is IHeartbeat heartbeat)
            {
                state.Heartbeats.Register(instanceId, heartbeat.HeartbeatInterval);
            }
        }

        // Replace blueprint in registry
        _blueprints[blueprintId] = newBlueprint;

        // Unload old ALC
        oldBlueprint.Alc.Unload();
        TriggerGC();
    }

    /// <summary>
    /// Reload an object by its instance ID. For backward compatibility.
    /// </summary>
    public async Task<T> ReloadAsync<T>(string id, WorldState state) where T : class, IMudObject
    {
        id = ObjectId.Normalize(id);

        // Find the blueprint for this instance
        if (_instances.TryGetValue(id, out var handle))
        {
            await ReloadBlueprintAsync(handle.Blueprint.BlueprintId, state);
            return Get<T>(id) ?? throw new InvalidOperationException($"Instance {id} not found after reload");
        }

        // Maybe it's a blueprint ID used as singleton - try unload/load
        await UnloadAsync(id, state);
        return await LoadAsync<T>(id, state);
    }

    /// <summary>
    /// Destruct (remove) an instance.
    /// </summary>
    public Task DestructAsync(string instanceId, WorldState state)
    {
        instanceId = ObjectId.Normalize(instanceId);

        if (!_instances.TryGetValue(instanceId, out var handle))
            return Task.CompletedTask;

        // Cancel scheduled callouts
        state.CallOuts.CancelAllForTarget(instanceId);

        // Unregister heartbeat if applicable
        state.Heartbeats.Unregister(instanceId);

        _instances.Remove(instanceId);
        handle.Blueprint.InstanceCount--;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Unload a blueprint and all its instances.
    /// For backward compatibility, also works with instance IDs.
    /// </summary>
    public Task UnloadAsync(string id, WorldState state)
    {
        id = ObjectId.Normalize(id);

        // Check if it's an instance first
        if (_instances.TryGetValue(id, out var instHandle))
        {
            var blueprintId = instHandle.Blueprint.BlueprintId;
            return UnloadBlueprintAsync(blueprintId, state);
        }

        // Otherwise treat as blueprint ID
        return UnloadBlueprintAsync(id, state);
    }

    /// <summary>
    /// Unload a blueprint and all its instances.
    /// </summary>
    public Task UnloadBlueprintAsync(string blueprintId, WorldState state)
    {
        blueprintId = ObjectId.Normalize(blueprintId);

        if (!_blueprints.TryGetValue(blueprintId, out var blueprint))
            return Task.CompletedTask;

        // Remove all instances of this blueprint
        var toRemove = _instances
            .Where(kv => kv.Value.Blueprint == blueprint)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in toRemove)
        {
            // Cancel scheduled callouts
            state.CallOuts.CancelAllForTarget(id);

            // Unregister heartbeat if applicable
            state.Heartbeats.Unregister(id);
            _instances.Remove(id);
        }

        // Remove blueprint and unload ALC
        _blueprints.Remove(blueprintId);
        blueprint.Alc.Unload();
        TriggerGC();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Get statistics about an object (blueprint or instance).
    /// </summary>
    public ObjectStats? GetStats(string id)
    {
        id = ObjectId.Normalize(id);

        // Check if it's an instance
        if (_instances.TryGetValue(id, out var inst))
        {
            return new ObjectStats
            {
                Id = id,
                IsBlueprint = false,
                BlueprintId = inst.Blueprint.BlueprintId,
                TypeName = inst.Instance.GetType().FullName ?? "Unknown",
                CreatedAt = inst.CreatedAt,
                StateKeys = inst.State.Keys.ToArray()
            };
        }

        // Check if it's a blueprint
        if (_blueprints.TryGetValue(id, out var bp))
        {
            return new ObjectStats
            {
                Id = id,
                IsBlueprint = true,
                BlueprintId = bp.BlueprintId,
                TypeName = bp.ObjectType.FullName ?? "Unknown",
                SourceMtime = bp.SourceMtime,
                InstanceCount = bp.InstanceCount
            };
        }

        return null;
    }

    // === Persistence Methods ===

    /// <summary>
    /// Export all instances for persistence.
    /// </summary>
    public List<InstanceSaveData> ExportInstances()
    {
        var result = new List<InstanceSaveData>();
        foreach (var kvp in _instances)
        {
            var handle = kvp.Value;
            var stateDict = handle.State is DictionaryStateStore dss
                ? dss.ToJsonDictionary()
                : null;

            result.Add(new InstanceSaveData
            {
                InstanceId = kvp.Key,
                BlueprintId = handle.Blueprint.BlueprintId,
                CreatedAt = handle.CreatedAt,
                State = stateDict
            });
        }
        return result;
    }

    /// <summary>
    /// Restore instances from saved data.
    /// Re-compiles blueprints and re-creates instances with saved state.
    /// </summary>
    public async Task RestoreInstancesAsync(List<InstanceSaveData>? instances, WorldState state)
    {
        if (instances is null || instances.Count == 0)
            return;

        foreach (var saved in instances)
        {
            try
            {
                // Ensure blueprint is compiled
                var blueprint = await EnsureBlueprintAsync(saved.BlueprintId);

                // Create object instance
                var instance = CreateObjectInstance(blueprint, saved.InstanceId);

                // Restore state
                var stateStore = new DictionaryStateStore();
                stateStore.FromJsonDictionary(saved.State);

                var ctx = new MudContext
                {
                    World = state,
                    State = stateStore,
                    Clock = _clock,
                    CurrentObjectId = saved.InstanceId,
                    RoomId = saved.InstanceId
                };

                // Call lifecycle hooks (OnLoad for restored instances)
                if (instance is IOnLoad onLoad)
                {
                    onLoad.OnLoad(ctx);
                }
                else
                {
                    instance.Create(state);
                }

                var handle = new InstanceHandle
                {
                    Id = ObjectId.Parse(saved.InstanceId),
                    Blueprint = blueprint,
                    Instance = instance,
                    State = stateStore,
                    CreatedAt = saved.CreatedAt
                };

                blueprint.InstanceCount++;
                _instances[saved.InstanceId] = handle;

                // Register for heartbeat if applicable
                if (instance is IHeartbeat heartbeat)
                {
                    state.Heartbeats.Register(saved.InstanceId, heartbeat.HeartbeatInterval);
                }
            }
            catch (Exception ex)
            {
                // Log but continue with other instances
                Console.WriteLine($"Failed to restore {saved.InstanceId}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Clear all instances and blueprints (for fresh restore).
    /// </summary>
    public void ClearAll(WorldState state)
    {
        // Clear all callouts and heartbeats
        foreach (var id in _instances.Keys.ToList())
        {
            state.CallOuts.CancelAllForTarget(id);
            state.Heartbeats.Unregister(id);
        }

        _instances.Clear();

        // Unload all blueprints
        foreach (var bp in _blueprints.Values)
        {
            bp.Alc.Unload();
        }
        _blueprints.Clear();

        TriggerGC();
    }

    // === Internal Methods ===

    private async Task<BlueprintHandle> EnsureBlueprintAsync(string blueprintId)
    {
        if (_blueprints.TryGetValue(blueprintId, out var existing))
            return existing;

        var blueprint = await CompileBlueprintAsync(blueprintId);
        _blueprints[blueprintId] = blueprint;
        return blueprint;
    }

    private async Task<BlueprintHandle> CompileBlueprintAsync(string blueprintId)
    {
        var sourcePath = Path.Combine(_worldRoot, blueprintId);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Blueprint source not found: {sourcePath}");

        var mtime = File.GetLastWriteTimeUtc(sourcePath);
        var code = await File.ReadAllTextAsync(sourcePath);
        var syntaxTree = CSharpSyntaxTree.ParseText(code, path: sourcePath);

        var compilation = CSharpCompilation.Create(
            assemblyName: $"JitRealmBP_{Guid.NewGuid():N}",
            syntaxTrees: new[] { syntaxTree },
            references: GetReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        await using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream);
        if (!emit.Success)
        {
            var errors = string.Join(Environment.NewLine,
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
            throw new InvalidOperationException($"Compile failed for {blueprintId}:{Environment.NewLine}{errors}");
        }

        peStream.Position = 0;

        var alc = new AssemblyLoadContext($"ALC_{blueprintId}_{Guid.NewGuid():N}", isCollectible: true);
        var asm = alc.LoadFromStream(peStream);

        var type = asm.GetTypes().FirstOrDefault(t =>
            typeof(IMudObject).IsAssignableFrom(t) &&
            t is { IsAbstract: false, IsInterface: false })
            ?? throw new InvalidOperationException($"No IMudObject implementation in {blueprintId}");

        return new BlueprintHandle
        {
            BlueprintId = blueprintId,
            Alc = alc,
            Assembly = asm,
            ObjectType = type,
            SourceMtime = mtime
        };
    }

    private Task<InstanceHandle> CreateInstanceInternalAsync(
        BlueprintHandle blueprint,
        string instanceId,
        WorldState state)
    {
        var instance = CreateObjectInstance(blueprint, instanceId);
        var stateStore = new DictionaryStateStore();

        var ctx = new MudContext
        {
            World = state,
            State = stateStore,
            Clock = _clock,
            CurrentObjectId = instanceId,
            RoomId = instanceId // For rooms, their own ID is the room ID
        };

        // Call lifecycle hooks
        if (instance is IOnLoad onLoad)
        {
            onLoad.OnLoad(ctx);
        }
        else
        {
            // Legacy Create method
            instance.Create(state);
        }

        var handle = new InstanceHandle
        {
            Id = ObjectId.Parse(instanceId),
            Blueprint = blueprint,
            Instance = instance,
            State = stateStore,
            CreatedAt = _clock.Now
        };

        blueprint.InstanceCount++;
        _instances[instanceId] = handle;

        // Register for heartbeat if applicable
        if (instance is IHeartbeat heartbeat)
        {
            state.Heartbeats.Register(instanceId, heartbeat.HeartbeatInterval);
        }

        return Task.FromResult(handle);
    }

    private IMudObject CreateObjectInstance(BlueprintHandle blueprint, string instanceId)
    {
        var obj = Activator.CreateInstance(blueprint.ObjectType) as IMudObject
            ?? throw new InvalidOperationException($"Failed to instantiate {blueprint.ObjectType.FullName}");

        // If using MudObjectBase, driver assigns the ID
        if (obj is MudObjectBase mob)
        {
            mob.Id = instanceId;
        }
        // Otherwise, object defines its own ID (legacy)

        return obj;
    }

    private static List<MetadataReference> GetReferences()
    {
        var refs = new List<MetadataReference>();

        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (!string.IsNullOrWhiteSpace(tpa))
        {
            foreach (var path in tpa.Split(Path.PathSeparator))
                refs.Add(MetadataReference.CreateFromFile(path));
        }

        var hostAsm = typeof(IMudObject).Assembly;
        if (!string.IsNullOrWhiteSpace(hostAsm.Location))
            refs.Add(MetadataReference.CreateFromFile(hostAsm.Location));

        return refs;
    }

    private static void TriggerGC()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
