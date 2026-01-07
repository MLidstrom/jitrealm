using JitRealm.Mud.Configuration;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using System.Threading.Channels;

namespace JitRealm.Mud.AI;

/// <summary>
/// Driver-owned persistent memory/goals system backed by PostgreSQL (optionally with pgvector).
/// This is not exposed through IMudContext to world code; it is consumed by driver systems.
/// </summary>
public sealed class NpcMemorySystem : IAsyncDisposable
{
    private readonly MemorySettings _settings;
    private readonly NpgsqlDataSource _dataSource;
    private readonly bool _pgvectorEnabled;

    public INpcMemoryStore NpcMemory { get; }
    public IWorldKnowledgeBase WorldKnowledge { get; }
    public INpcGoalStore Goals { get; }

    public int DefaultMemoryTopK => _settings.DefaultMemoryTopK;
    public int DefaultKbTopK => _settings.DefaultKbTopK;
    public int CandidateLimit => _settings.CandidateLimit;

    private readonly Channel<NpcMemoryWrite> _writeQueue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;

    private NpcMemorySystem(MemorySettings settings, NpgsqlDataSource dataSource, bool pgvectorEnabled)
    {
        _settings = settings;
        _dataSource = dataSource;
        _pgvectorEnabled = pgvectorEnabled;

        NpcMemory = new PostgresNpcMemoryStore(_dataSource, _settings, _pgvectorEnabled);
        WorldKnowledge = new PostgresWorldKnowledgeBase(_dataSource);
        Goals = new PostgresNpcGoalStore(_dataSource);

        _writeQueue = Channel.CreateBounded<NpcMemoryWrite>(new BoundedChannelOptions(Math.Max(100, _settings.MaxWriteQueue))
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        _writerTask = Task.Run(() => MemoryWriterLoopAsync(_cts.Token));
    }

    public static async Task<NpcMemorySystem> CreateAsync(MemorySettings settings, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
            throw new InvalidOperationException("Memory system is disabled.");

        var connectionString = settings.GetEffectiveConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Memory.ConnectionString must be set or JITREALM_PG_* environment variables must be configured when Memory.Enabled is true.");

        // Log connection info (masked password)
        var maskedConnStr = MaskPassword(connectionString);
        Console.WriteLine($"[Memory] Connecting to: {maskedConnStr}");

        // Try to create database if it doesn't exist
        await EnsureDatabaseExistsAsync(connectionString, cancellationToken);

        // NpgsqlDataSource enables pooling and is the recommended modern approach.
        var builder = new NpgsqlDataSourceBuilder(connectionString);

        // Register pgvector type mappings (safe even if extension isn't installed yet).
        if (settings.UsePgvector)
            builder.UseVector();

        var dataSource = builder.Build();

        // Ensure schema exists (idempotent).
        Console.WriteLine("[Memory] Creating tables if needed...");
        var pgvectorEnabled = await PostgresMemorySchema.EnsureAsync(dataSource, settings, cancellationToken);
        Console.WriteLine($"[Memory] Schema ready (pgvector: {(pgvectorEnabled ? "enabled" : "disabled")})");

        return new NpcMemorySystem(settings, dataSource, pgvectorEnabled);
    }

    private static string MaskPassword(string connectionString)
    {
        // Simple masking for logging
        var parts = connectionString.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("Password=", StringComparison.OrdinalIgnoreCase))
                parts[i] = "Password=***";
        }
        return string.Join(";", parts);
    }

    private static async Task EnsureDatabaseExistsAsync(string connectionString, CancellationToken cancellationToken)
    {
        // Parse connection string to extract database name
        var connBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        var targetDatabase = connBuilder.Database;

        if (string.IsNullOrWhiteSpace(targetDatabase))
            return; // No database specified, let normal connection fail

        // Connect to 'postgres' database to check/create target database
        connBuilder.Database = "postgres";
        var adminConnStr = connBuilder.ToString();

        try
        {
            await using var conn = new NpgsqlConnection(adminConnStr);
            await conn.OpenAsync(cancellationToken);

            // Check if database exists
            await using var checkCmd = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @db",
                conn);
            checkCmd.Parameters.AddWithValue("db", targetDatabase);

            var exists = await checkCmd.ExecuteScalarAsync(cancellationToken);
            if (exists is null)
            {
                Console.WriteLine($"[Memory] Database '{targetDatabase}' does not exist. Creating...");
                // CREATE DATABASE cannot be parameterized, but we control the name
                await using var createCmd = new NpgsqlCommand(
                    $"CREATE DATABASE \"{targetDatabase.Replace("\"", "\"\"")}\"",
                    conn);
                await createCmd.ExecuteNonQueryAsync(cancellationToken);
                Console.WriteLine($"[Memory] Database '{targetDatabase}' created.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Memory] Note: Could not verify/create database: {ex.Message}");
            // Continue anyway - the main connection will fail with a clearer error if DB doesn't exist
        }
    }

    /// <summary>
    /// Enqueue a memory write to be persisted asynchronously.
    /// Uses a bounded queue (DropOldest) to protect the game loop.
    /// </summary>
    public bool TryEnqueueMemoryWrite(NpcMemoryWrite write) =>
        _writeQueue.Writer.TryWrite(write);

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts.Cancel();
            _writeQueue.Writer.TryComplete();
            await _writerTask;
        }
        catch
        {
            // Ignore background worker exceptions on shutdown
        }
        finally
        {
            _cts.Dispose();
        }

        await _dataSource.DisposeAsync();
    }

    private async Task MemoryWriterLoopAsync(CancellationToken cancellationToken)
    {
        // Soft rate-limit per process to avoid hammering Postgres.
        var maxWritesPerSecond = Math.Max(1, _settings.MaxWritesPerSecond);
        var minDelayMs = (int)Math.Max(0, 1000.0 / maxWritesPerSecond);

        while (!cancellationToken.IsCancellationRequested)
        {
            NpcMemoryWrite write;
            try
            {
                write = await _writeQueue.Reader.ReadAsync(cancellationToken);
            }
            catch
            {
                break;
            }

            try
            {
                await NpcMemory.AddAsync(write, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Memory] Write failed: {ex.Message}");
            }

            if (minDelayMs > 0)
            {
                try
                {
                    await Task.Delay(minDelayMs, cancellationToken);
                }
                catch
                {
                    break;
                }
            }
        }
    }
}


