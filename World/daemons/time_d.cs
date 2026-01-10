using System;
using JitRealm.Mud;

/// <summary>
/// TIME_D - World time daemon.
/// Tracks in-game time that progresses faster than real time.
/// Provides time-of-day and day/night cycle information.
///
/// Access from world code: ctx.World.GetDaemon&lt;ITimeDaemon&gt;("TIME_D")
/// </summary>
public sealed class TimeD : DaemonBase, ITimeDaemon
{
    /// <summary>
    /// Daemon identifier - used for lookups.
    /// </summary>
    public override string DaemonId => "TIME_D";

    public override string Name => "Time Daemon";
    public override string Description => "Manages world time and day/night cycles";

    /// <summary>
    /// Update world time every 10 seconds.
    /// </summary>
    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(10);

    /// <summary>
    /// How many in-game minutes pass per real minute.
    /// Default: 24 (1 real minute = 24 in-game minutes, so 1 day = 1 real hour)
    /// Classic LPMud style timing.
    /// </summary>
    public int TimeMultiplier { get; private set; } = 24;

    /// <summary>
    /// Gets the current world hour (0-23).
    /// </summary>
    public int Hour => Ctx?.State.Get<int>("hour") ?? 6;

    /// <summary>
    /// Gets the current world minute (0-59).
    /// </summary>
    public int Minute => Ctx?.State.Get<int>("minute") ?? 0;

    /// <summary>
    /// Gets the current world day (1+).
    /// </summary>
    public int Day => Ctx?.State.Get<int>("day") ?? 1;

    /// <summary>
    /// Gets the current month (1-12).
    /// </summary>
    public int Month => Ctx?.State.Get<int>("month") ?? 1;

    /// <summary>
    /// Gets the current year.
    /// </summary>
    public int Year => Ctx?.State.Get<int>("year") ?? 1;

    /// <summary>
    /// Gets a formatted time string (e.g., "14:30").
    /// </summary>
    public string TimeString => $"{Hour:D2}:{Minute:D2}";

    /// <summary>
    /// Gets a formatted date string (e.g., "Day 15, Month 3, Year 1").
    /// </summary>
    public string DateString => $"Day {Day}, Month {Month}, Year {Year}";

    /// <summary>
    /// Gets the current time of day period.
    /// </summary>
    public TimePeriod Period => Hour switch
    {
        >= 5 and < 7 => TimePeriod.Dawn,
        >= 7 and < 12 => TimePeriod.Morning,
        >= 12 and < 14 => TimePeriod.Midday,
        >= 14 and < 17 => TimePeriod.Afternoon,
        >= 17 and < 20 => TimePeriod.Evening,
        >= 20 and < 22 => TimePeriod.Dusk,
        _ => TimePeriod.Night
    };

    /// <summary>
    /// Returns true if it's currently night time (dark).
    /// </summary>
    public bool IsNight => Period == TimePeriod.Night;

    /// <summary>
    /// Returns true if it's currently day time (light).
    /// </summary>
    public bool IsDay => !IsNight && Period != TimePeriod.Dawn && Period != TimePeriod.Dusk;

    /// <summary>
    /// Gets a description of the current time of day.
    /// </summary>
    public string PeriodDescription => Period switch
    {
        TimePeriod.Dawn => "The sky begins to lighten as dawn approaches.",
        TimePeriod.Morning => "The morning sun casts long shadows.",
        TimePeriod.Midday => "The sun is high overhead.",
        TimePeriod.Afternoon => "The afternoon sun beats down warmly.",
        TimePeriod.Evening => "The evening light grows golden.",
        TimePeriod.Dusk => "The sun sets, painting the sky in hues of orange and purple.",
        TimePeriod.Night => "Stars twinkle in the dark night sky.",
        _ => "Time passes..."
    };

    protected override void OnInitialize(IMudContext ctx)
    {
        // Initialize time if not already set
        if (!ctx.State.Has("hour"))
        {
            ctx.State.Set("hour", 6);     // Start at 6 AM
            ctx.State.Set("minute", 0);
            ctx.State.Set("day", 1);
            ctx.State.Set("month", 1);
            ctx.State.Set("year", 1);
        }
    }

    protected override void OnHeartbeat(IMudContext ctx)
    {
        // Calculate elapsed real time since last update (in seconds)
        var lastUpdate = ctx.State.Get<long>("last_update");
        var now = ctx.World.Now.ToUnixTimeSeconds();

        if (lastUpdate == 0)
        {
            ctx.State.Set("last_update", now);
            return;
        }

        var elapsed = now - lastUpdate;
        if (elapsed <= 0)
            return;

        // Convert real seconds to in-game minutes
        var gameMinutes = (int)(elapsed * TimeMultiplier / 60.0);
        if (gameMinutes <= 0)
            return;

        // Update state
        ctx.State.Set("last_update", now);

        // Advance time
        var minute = ctx.State.Get<int>("minute") + gameMinutes;
        var hour = ctx.State.Get<int>("hour");
        var day = ctx.State.Get<int>("day");
        var month = ctx.State.Get<int>("month");
        var year = ctx.State.Get<int>("year");

        // Handle minute overflow
        while (minute >= 60)
        {
            minute -= 60;
            hour++;
        }

        // Handle hour overflow
        while (hour >= 24)
        {
            hour -= 24;
            day++;
        }

        // Handle day overflow (30 days per month for simplicity)
        while (day > 30)
        {
            day -= 30;
            month++;
        }

        // Handle month overflow
        while (month > 12)
        {
            month -= 12;
            year++;
        }

        ctx.State.Set("minute", minute);
        ctx.State.Set("hour", hour);
        ctx.State.Set("day", day);
        ctx.State.Set("month", month);
        ctx.State.Set("year", year);
    }

    /// <summary>
    /// Set the current time (for wizard commands).
    /// </summary>
    public void SetTime(int hour, int minute)
    {
        if (Ctx is null) return;
        Ctx.State.Set("hour", Math.Clamp(hour, 0, 23));
        Ctx.State.Set("minute", Math.Clamp(minute, 0, 59));
    }

    /// <summary>
    /// Set the current date (for wizard commands).
    /// </summary>
    public void SetDate(int day, int month, int year)
    {
        if (Ctx is null) return;
        Ctx.State.Set("day", Math.Clamp(day, 1, 30));
        Ctx.State.Set("month", Math.Clamp(month, 1, 12));
        Ctx.State.Set("year", Math.Max(year, 1));
    }
}

/// <summary>
/// Time periods of the day.
/// </summary>
public enum TimePeriod
{
    Dawn,       // 5-7
    Morning,    // 7-12
    Midday,     // 12-14
    Afternoon,  // 14-17
    Evening,    // 17-20
    Dusk,       // 20-22
    Night       // 22-5
}
