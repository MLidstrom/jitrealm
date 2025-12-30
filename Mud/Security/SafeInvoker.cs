namespace JitRealm.Mud.Security;

/// <summary>
/// Provides timeout-protected invocation of world code methods.
/// Note: .NET cannot forcibly abort managed threads, so timeouts
/// throw exceptions but runaway code may still consume the thread
/// until the method returns.
/// </summary>
public static class SafeInvoker
{
    /// <summary>
    /// Invokes an action with a timeout.
    /// </summary>
    /// <param name="action">The action to invoke.</param>
    /// <param name="timeout">Maximum time to wait for completion.</param>
    /// <param name="context">Description for error messages.</param>
    /// <returns>True if completed within timeout, false if timed out.</returns>
    public static bool TryInvokeWithTimeout(Action action, TimeSpan timeout, string context)
    {
        try
        {
            var task = Task.Run(action);

            if (task.Wait(timeout))
            {
                // Check for exceptions that occurred during execution
                if (task.IsFaulted && task.Exception is not null)
                {
                    var inner = task.Exception.InnerException ?? task.Exception;
                    Console.WriteLine($"[{context}] Error: {inner.Message}");
                    return false;
                }
                return true;
            }

            // Timeout occurred
            Console.WriteLine($"[{context}] Timeout after {timeout.TotalSeconds:F1}s - execution may continue in background");
            return false;
        }
        catch (AggregateException ae)
        {
            var inner = ae.InnerException ?? ae;
            Console.WriteLine($"[{context}] Error: {inner.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{context}] Error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Invokes an action with the default hook timeout from SecurityPolicy.
    /// </summary>
    public static bool TryInvokeHook(Action action, string context)
    {
        return TryInvokeWithTimeout(action, SecurityPolicy.Default.HookTimeout, context);
    }

    /// <summary>
    /// Invokes an action with the default callout timeout from SecurityPolicy.
    /// </summary>
    public static bool TryInvokeCallout(Action action, string context)
    {
        return TryInvokeWithTimeout(action, SecurityPolicy.Default.CalloutTimeout, context);
    }

    /// <summary>
    /// Invokes an action with the default heartbeat timeout from SecurityPolicy.
    /// </summary>
    public static bool TryInvokeHeartbeat(Action action, string context)
    {
        return TryInvokeWithTimeout(action, SecurityPolicy.Default.HeartbeatTimeout, context);
    }
}
