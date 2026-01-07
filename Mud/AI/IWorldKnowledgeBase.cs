using System.Text.Json;

namespace JitRealm.Mud.AI;

public interface IWorldKnowledgeBase
{
    Task UpsertAsync(WorldKbEntry entry, CancellationToken cancellationToken = default);
    Task<WorldKbEntry?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorldKbEntry>> SearchByTagsAsync(IReadOnlyList<string> tags, int topK, CancellationToken cancellationToken = default);
}

public sealed record WorldKbEntry(
    string Key,
    JsonDocument Value,
    IReadOnlyList<string> Tags,
    string Visibility,
    DateTimeOffset UpdatedAt);


