namespace JitRealm.Mud;

/// <summary>
/// Represents an object identifier, which can be either a blueprint path or an instance ID.
/// Blueprint: "Rooms/meadow.cs"
/// Instance: "Rooms/meadow.cs#000001"
/// </summary>
public readonly struct ObjectId : IEquatable<ObjectId>
{
    public string BlueprintPath { get; }
    public int? CloneNumber { get; }

    public bool IsBlueprint => CloneNumber is null;
    public bool IsInstance => CloneNumber is not null;

    public ObjectId(string blueprintPath, int? cloneNumber = null)
    {
        BlueprintPath = Normalize(blueprintPath);
        CloneNumber = cloneNumber;
    }

    public override string ToString() =>
        CloneNumber.HasValue
            ? $"{BlueprintPath}#{CloneNumber.Value:D6}"
            : BlueprintPath;

    public static ObjectId Parse(string id)
    {
        var hashIndex = id.LastIndexOf('#');
        if (hashIndex < 0)
            return new ObjectId(id);

        var path = id[..hashIndex];
        if (int.TryParse(id[(hashIndex + 1)..], out var num))
            return new ObjectId(path, num);

        return new ObjectId(id);
    }

    public static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    public bool Equals(ObjectId other) =>
        BlueprintPath.Equals(other.BlueprintPath, StringComparison.OrdinalIgnoreCase) &&
        CloneNumber == other.CloneNumber;

    public override bool Equals(object? obj) => obj is ObjectId other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(BlueprintPath.ToLowerInvariant(), CloneNumber);

    public static bool operator ==(ObjectId left, ObjectId right) => left.Equals(right);
    public static bool operator !=(ObjectId left, ObjectId right) => !left.Equals(right);
}
