using JitRealm.Mud.Configuration;

namespace JitRealm.Mud.AI;

/// <summary>
/// Debug logger for LLM operations. Writes to a file for easy monitoring with tail -f.
/// </summary>
public sealed class LlmDebugLogger : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly bool _verbose;
    private readonly object _lock = new();
    private bool _disposed;

    public bool IsEnabled => _writer is not null;

    public LlmDebugLogger(LlmSettings settings)
    {
        if (!settings.DebugEnabled)
            return;

        _verbose = settings.DebugVerbose;

        try
        {
            var path = settings.DebugLogPath;
            if (string.IsNullOrWhiteSpace(path))
                path = "llm_debug.log";

            // Open in append mode, auto-flush enabled
            _writer = new StreamWriter(path, append: true) { AutoFlush = true };
            Log("=== LLM Debug Log Started ===");
            Log($"Verbose mode: {(_verbose ? "ON" : "OFF")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM Debug] Failed to open log file: {ex.Message}");
            _writer = null;
        }
    }

    /// <summary>
    /// Log a simple message.
    /// </summary>
    public void Log(string message)
    {
        if (_writer is null) return;

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var line = $"[{timestamp}] {message}";

        lock (_lock)
        {
            if (!_disposed)
                _writer.WriteLine(line);
        }
    }

    /// <summary>
    /// Log an LLM request (NPC prompting the model).
    /// </summary>
    public void LogRequest(string npcId, string? eventInfo, string systemPrompt, string userPrompt)
    {
        if (_writer is null) return;

        Log($"REQUEST: {npcId}");
        Log($"  Event: {eventInfo ?? "N/A"}");

        if (_verbose)
        {
            Log($"  System prompt ({systemPrompt.Length} chars):");
            LogIndented(systemPrompt, "    ");
            Log($"  User prompt ({userPrompt.Length} chars):");
            LogIndented(userPrompt, "    ");
        }
        else
        {
            Log($"  System prompt: {systemPrompt.Length} chars");
            Log($"  User prompt: {Truncate(userPrompt, 100)}");
        }
    }

    /// <summary>
    /// Log an LLM response.
    /// </summary>
    public void LogResponse(string npcId, string? response, int durationMs)
    {
        if (_writer is null) return;

        Log($"RESPONSE: {npcId} ({durationMs}ms)");

        if (response is null)
        {
            Log("  [No response / timeout]");
            return;
        }

        if (_verbose)
        {
            Log($"  Response ({response.Length} chars):");
            LogIndented(response, "    ");
        }
        else
        {
            Log($"  Response: {Truncate(response, 200)}");
        }
    }

    /// <summary>
    /// Log a goal change.
    /// </summary>
    public void LogGoalChange(string npcId, string action, string goalType, string? target = null)
    {
        if (_writer is null) return;

        var targetInfo = target is not null ? $" (target: {target})" : "";
        Log($"GOAL: {npcId} - {action}: {goalType}{targetInfo}");
    }

    /// <summary>
    /// Log a memory operation.
    /// </summary>
    public void LogMemory(string npcId, string operation, string details)
    {
        if (_writer is null) return;

        Log($"MEMORY: {npcId} - {operation}: {details}");
    }

    /// <summary>
    /// Log an NPC command execution.
    /// </summary>
    public void LogCommand(string npcId, string command, bool success)
    {
        if (_writer is null) return;

        var status = success ? "OK" : "FAILED";
        Log($"COMMAND: {npcId} - [{status}] {command}");
    }

    /// <summary>
    /// Log an event being processed.
    /// </summary>
    public void LogEvent(string npcId, string eventType, string? actorName, string? message)
    {
        if (_writer is null) return;

        var actor = actorName ?? "unknown";
        var msg = message is not null ? $": {Truncate(message, 80)}" : "";
        Log($"EVENT: {npcId} - {eventType} from {actor}{msg}");
    }

    /// <summary>
    /// Log a context build operation.
    /// </summary>
    public void LogContext(string npcId, int memoryCount, int goalCount, int needCount, int kbCount)
    {
        if (_writer is null) return;

        Log($"CONTEXT: {npcId} - memories:{memoryCount} goals:{goalCount} needs:{needCount} kb:{kbCount}");
    }

    private void LogIndented(string text, string indent)
    {
        foreach (var line in text.Split('\n'))
        {
            lock (_lock)
            {
                if (!_disposed)
                    _writer!.WriteLine($"{indent}{line.TrimEnd('\r')}");
            }
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        text = text.Replace("\n", " ").Replace("\r", "");
        if (text.Length <= maxLength)
            return text;
        return text[..(maxLength - 3)] + "...";
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_writer is not null)
            {
                Log("=== LLM Debug Log Closed ===");
                _writer.Dispose();
            }
        }
    }
}
