using System;
using System.Threading.Tasks;
using JitRealm.Mud;

/// <summary>
/// Base class for daemons - long-lived singleton service objects.
/// Extend this class to create custom daemons like TIME_D, WEATHER_D, etc.
///
/// Daemons are automatically loaded from World/daemons/ on server startup.
/// They provide shared game systems accessible to all world code.
/// </summary>
public abstract class DaemonBase : MudObjectBase, IDaemon, IDaemonHeartbeat
{
    /// <summary>
    /// Cached context for property access.
    /// Set during Initialize and Heartbeat.
    /// </summary>
    protected IMudContext? Ctx { get; set; }

    /// <summary>
    /// Unique identifier for this daemon.
    /// Override with a constant like "TIME_D", "WEATHER_D", etc.
    /// </summary>
    public abstract string DaemonId { get; }

    /// <summary>
    /// Display name for the daemon.
    /// Defaults to DaemonId.
    /// </summary>
    public override string Name => DaemonId;

    /// <summary>
    /// Description of what this daemon does.
    /// </summary>
    public override string Description => $"{DaemonId} daemon";

    /// <summary>
    /// Heartbeat interval for periodic updates.
    /// Override to customize. Default is 1 minute.
    /// </summary>
    public virtual TimeSpan HeartbeatInterval => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Called when the daemon is initialized during server startup.
    /// Override to set up initial state.
    /// </summary>
    public virtual void Initialize(IMudContext ctx)
    {
        Ctx = ctx;
        OnInitialize(ctx);
    }

    /// <summary>
    /// Override this to perform daemon-specific initialization.
    /// Called after Ctx is set.
    /// </summary>
    protected virtual void OnInitialize(IMudContext ctx)
    {
    }

    /// <summary>
    /// Called periodically based on HeartbeatInterval.
    /// Override to perform periodic updates.
    /// </summary>
    public virtual void Heartbeat(IMudContext ctx)
    {
        Ctx = ctx;
        OnHeartbeat(ctx);
    }

    /// <summary>
    /// Override this to perform daemon-specific heartbeat logic.
    /// Called after Ctx is updated.
    /// </summary>
    protected virtual void OnHeartbeat(IMudContext ctx)
    {
    }
}

/// <summary>
/// Base class for daemons that support shutdown cleanup.
/// </summary>
public abstract class ShutdownDaemonBase : DaemonBase, IDaemonShutdown
{
    /// <summary>
    /// Called when the server is shutting down.
    /// Override to save state or clean up resources.
    /// </summary>
    public virtual void Shutdown(IMudContext ctx)
    {
        Ctx = ctx;
        OnShutdown(ctx);
    }

    /// <summary>
    /// Override this to perform daemon-specific shutdown logic.
    /// </summary>
    protected virtual void OnShutdown(IMudContext ctx)
    {
    }
}
