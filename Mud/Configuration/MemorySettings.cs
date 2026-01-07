namespace JitRealm.Mud.Configuration;

/// <summary>
/// Configuration for persistent NPC goals/memory and shared world knowledge base.
/// Backed by PostgreSQL (optionally with pgvector enabled).
/// </summary>
public sealed class MemorySettings
{
    /// <summary>
    /// Enable the memory system. When disabled, NPCs will not persist long-term memory or goals.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// PostgreSQL connection string for memory storage.
    /// Example: "Host=localhost;Port=5432;Username=jitrealm;Password=...;Database=jitrealm"
    /// If empty, will be built from environment variables (JITREALM_PG_HOST, etc.)
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Gets the effective connection string, building from environment variables if not explicitly set.
    /// </summary>
    public string GetEffectiveConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
            return ConnectionString;

        // Build from environment variables
        var host = Environment.GetEnvironmentVariable("JITREALM_PG_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("JITREALM_PG_PORT") ?? "5432";
        var database = Environment.GetEnvironmentVariable("JITREALM_PG_DATABASE") ?? "jitrealm";
        var user = Environment.GetEnvironmentVariable("JITREALM_PG_USER");
        var password = Environment.GetEnvironmentVariable("JITREALM_PG_PASSWORD");

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
            return ""; // Cannot build connection string without credentials

        return $"Host={host};Port={port};Database={database};Username={user};Password={password}";
    }

    /// <summary>
    /// If true, the driver will attempt to create/use the pgvector extension and store embeddings.
    /// </summary>
    public bool UsePgvector { get; set; } = true;

    /// <summary>
    /// Embedding vector dimensions (must match the embedding model you use).
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 1536;

    /// <summary>
    /// Maximum items kept in the in-process write queue before backpressure/drops occur.
    /// </summary>
    public int MaxWriteQueue { get; set; } = 10000;

    /// <summary>
    /// Maximum number of memory writes per second per process (soft cap).
    /// </summary>
    public int MaxWritesPerSecond { get; set; } = 200;

    /// <summary>
    /// Maximum number of candidate memories to fetch before reranking (for hybrid retrieval).
    /// </summary>
    public int CandidateLimit { get; set; } = 500;

    /// <summary>
    /// Default maximum memories returned per retrieval.
    /// </summary>
    public int DefaultMemoryTopK { get; set; } = 10;

    /// <summary>
    /// Default maximum KB facts returned per retrieval.
    /// </summary>
    public int DefaultKbTopK { get; set; } = 5;
}


