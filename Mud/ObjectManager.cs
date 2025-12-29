using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace JitRealm.Mud;

public sealed class ObjectManager
{
    private readonly string _worldRoot;

    private sealed class LoadedObject
    {
        public required string Id { get; init; }
        public required AssemblyLoadContext Alc { get; init; }
        public required IMudObject Instance { get; init; }
    }

    private readonly Dictionary<string, LoadedObject> _loaded = new(StringComparer.OrdinalIgnoreCase);

    public ObjectManager(string worldRoot) => _worldRoot = worldRoot;

    public IEnumerable<string> ListLoadedIds() => _loaded.Keys.OrderBy(x => x);

    public T? Get<T>(string id) where T : class, IMudObject
    {
        id = Normalize(id);
        return _loaded.TryGetValue(id, out var lo) ? lo.Instance as T : null;
    }

    public async Task<T> LoadAsync<T>(string id, WorldState state) where T : class, IMudObject
    {
        id = Normalize(id);

        if (_loaded.TryGetValue(id, out var existing))
        {
            return existing.Instance as T
                ?? throw new InvalidOperationException($"Loaded object {id} is not {typeof(T).Name}");
        }

        var sourcePath = Path.Combine(_worldRoot, id);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"World object source not found: {sourcePath}");

        var code = await File.ReadAllTextAsync(sourcePath);
        var syntaxTree = CSharpSyntaxTree.ParseText(code, path: sourcePath);

        var compilation = CSharpCompilation.Create(
            assemblyName: $"JitRealmObj_{Guid.NewGuid():N}",
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
            throw new InvalidOperationException($"Compile failed for {id}:{Environment.NewLine}{errors}");
        }

        peStream.Position = 0;

        var alc = new AssemblyLoadContext($"ALC_{id}_{Guid.NewGuid():N}", isCollectible: true);
        var asm = alc.LoadFromStream(peStream);

        var instance = CreateInstance(asm, id);
        instance.Create(state);

        _loaded[id] = new LoadedObject { Id = id, Alc = alc, Instance = instance };

        return instance as T
            ?? throw new InvalidOperationException($"Loaded object {id} is not {typeof(T).Name}");
    }

    public async Task<T> ReloadAsync<T>(string id, WorldState state) where T : class, IMudObject
    {
        await UnloadAsync(id);
        return await LoadAsync<T>(id, state);
    }

    public Task UnloadAsync(string id)
    {
        id = Normalize(id);
        if (!_loaded.TryGetValue(id, out var lo))
            return Task.CompletedTask;

        _loaded.Remove(id);
        lo.Alc.Unload();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return Task.CompletedTask;
    }

    private static IMudObject CreateInstance(Assembly asm, string id)
    {
        var type = asm.GetTypes().FirstOrDefault(t =>
            typeof(IMudObject).IsAssignableFrom(t) &&
            t is { IsAbstract: false, IsInterface: false });

        if (type is null)
            throw new InvalidOperationException($"No IMudObject implementation found for {id}");

        var obj = Activator.CreateInstance(type) as IMudObject;
        if (obj is null)
            throw new InvalidOperationException($"Failed to instantiate {type.FullName} for {id}");

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

    private static string Normalize(string id) => id.Replace('\\', '/').TrimStart('/');
}
