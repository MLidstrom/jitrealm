using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace JitRealm.Mud.AI;

internal sealed class PostgresNpcGoalStore : INpcGoalStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresNpcGoalStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task UpsertAsync(NpcGoal goal, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(@"
INSERT INTO npc_goals (npc_id, goal_type, target_player, params, status, importance, updated_at)
VALUES ($1, $2, $3, $4::jsonb, $5, $6, now())
ON CONFLICT (npc_id, goal_type) DO UPDATE
SET target_player = EXCLUDED.target_player,
    params = EXCLUDED.params,
    status = EXCLUDED.status,
    importance = EXCLUDED.importance,
    updated_at = now();
", conn);

        cmd.Parameters.AddWithValue(goal.NpcId);
        cmd.Parameters.AddWithValue(goal.GoalType);
        cmd.Parameters.AddWithValue((object?)NormalizePlayer(goal.TargetPlayer) ?? DBNull.Value);

        var json = goal.Params.RootElement.GetRawText();
        cmd.Parameters.Add(new NpgsqlParameter { Value = json, NpgsqlDbType = NpgsqlDbType.Jsonb });

        cmd.Parameters.AddWithValue(goal.Status);
        cmd.Parameters.AddWithValue(goal.Importance);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<NpcGoal?> GetAsync(string npcId, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(@"
SELECT npc_id, goal_type, target_player, params, status, importance, updated_at
FROM npc_goals
WHERE npc_id = $1
  AND goal_type != 'survive'
ORDER BY importance ASC
LIMIT 1;
", conn);
        cmd.Parameters.AddWithValue(npcId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadGoal(reader);
    }

    public async Task<IReadOnlyList<NpcGoal>> GetAllAsync(string npcId, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(@"
SELECT npc_id, goal_type, target_player, params, status, importance, updated_at
FROM npc_goals
WHERE npc_id = $1
  AND goal_type != 'survive'
ORDER BY importance ASC;
", conn);
        cmd.Parameters.AddWithValue(npcId);

        var results = new List<NpcGoal>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadGoal(reader));
        }

        return results;
    }

    public async Task UpdateParamsAsync(string npcId, string goalType, JsonDocument newParams, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(@"
UPDATE npc_goals
SET params = $3::jsonb, updated_at = now()
WHERE npc_id = $1 AND goal_type = $2;
", conn);
        cmd.Parameters.AddWithValue(npcId);
        cmd.Parameters.AddWithValue(goalType);

        var json = newParams.RootElement.GetRawText();
        cmd.Parameters.Add(new NpgsqlParameter { Value = json, NpgsqlDbType = NpgsqlDbType.Jsonb });

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAsync(string npcId, string goalType, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM npc_goals WHERE npc_id = $1 AND goal_type = $2;", conn);
        cmd.Parameters.AddWithValue(npcId);
        cmd.Parameters.AddWithValue(goalType);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAllAsync(string npcId, bool preserveSurvival = true, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

        var sql = preserveSurvival
            ? "DELETE FROM npc_goals WHERE npc_id = $1 AND goal_type != 'survive';"
            : "DELETE FROM npc_goals WHERE npc_id = $1;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(npcId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpcGoal ReadGoal(NpgsqlDataReader reader)
    {
        var id = reader.GetString(0);
        var goalType = reader.GetString(1);
        var targetPlayer = reader.IsDBNull(2) ? null : reader.GetString(2);
        var paramsJson = reader.GetString(3);
        var status = reader.GetString(4);
        var importance = reader.GetInt32(5);
        var updatedAt = reader.GetFieldValue<DateTimeOffset>(6);

        return new NpcGoal(
            NpcId: id,
            GoalType: goalType,
            TargetPlayer: targetPlayer,
            Params: JsonDocument.Parse(paramsJson),
            Status: status,
            Importance: importance,
            UpdatedAt: updatedAt);
    }

    private static string? NormalizePlayer(string? playerName) =>
        string.IsNullOrWhiteSpace(playerName) ? null : playerName.Trim().ToLowerInvariant();
}
