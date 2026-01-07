using Pgvector;

namespace JitRealm.Mud.AI;

public interface INpcMemoryStore
{
    Task AddAsync(NpcMemoryWrite write, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recall up to TopK memories for an NPC, optionally scoped to a subject player and/or tags.
    /// If QueryEmbedding is provided and pgvector is enabled, results are ranked by similarity.
    /// </summary>
    Task<IReadOnlyList<NpcMemory>> RecallAsync(NpcMemoryRecallQuery query, CancellationToken cancellationToken = default);
}

public sealed record NpcMemory(
    Guid Id,
    string NpcId,
    string? SubjectPlayer,
    string? RoomId,
    string? AreaId,
    string Kind,
    int Importance,
    IReadOnlyList<string> Tags,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record NpcMemoryWrite(
    Guid Id,
    string NpcId,
    string? SubjectPlayer,
    string? RoomId,
    string? AreaId,
    string Kind,
    int Importance,
    IReadOnlyList<string> Tags,
    string Content,
    DateTimeOffset? ExpiresAt,
    Vector? Embedding);

public sealed record NpcMemoryRecallQuery(
    string NpcId,
    string? SubjectPlayer,
    IReadOnlyList<string> Tags,
    int TopK,
    int CandidateLimit,
    Vector? QueryEmbedding);


