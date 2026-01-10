using System;
using JitRealm.Mud;

/// <summary>
/// WEATHER_D - Weather simulation daemon.
/// Manages weather conditions that can vary by region and time.
/// Works with TIME_D for time-aware weather changes.
///
/// Access from world code: ctx.World.GetDaemon&lt;IWeatherDaemon&gt;("WEATHER_D")
/// </summary>
public sealed class WeatherD : DaemonBase, IWeatherDaemon
{
    private static readonly Random _random = new();

    /// <summary>
    /// Daemon identifier - used for lookups.
    /// </summary>
    public override string DaemonId => "WEATHER_D";

    public override string Name => "Weather Daemon";
    public override string Description => "Manages weather conditions and atmospheric effects";

    /// <summary>
    /// Check weather conditions every 30 seconds.
    /// </summary>
    public override TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the current weather condition for the default region.
    /// </summary>
    public WeatherCondition CurrentWeather => Ctx is not null
        ? (WeatherCondition)Ctx.State.Get<int>("weather")
        : WeatherCondition.Clear;

    /// <summary>
    /// Gets the temperature (conceptual, 0-100 scale).
    /// 0 = freezing, 50 = moderate, 100 = scorching
    /// </summary>
    public int Temperature => Ctx?.State.Get<int>("temperature") ?? 50;

    /// <summary>
    /// Gets the wind strength (0-100).
    /// 0 = calm, 50 = breezy, 100 = gale
    /// </summary>
    public int WindStrength => Ctx?.State.Get<int>("wind") ?? 10;

    /// <summary>
    /// Returns a description of the current weather.
    /// </summary>
    public string WeatherDescription => CurrentWeather switch
    {
        WeatherCondition.Clear => WindStrength > 30
            ? "The sky is clear, with a brisk wind blowing."
            : "The sky is clear and calm.",
        WeatherCondition.Cloudy => "Grey clouds blanket the sky.",
        WeatherCondition.Overcast => "The sky is heavily overcast, threatening rain.",
        WeatherCondition.LightRain => "A light drizzle falls from the grey sky.",
        WeatherCondition.Rain => "Rain falls steadily, forming puddles on the ground.",
        WeatherCondition.HeavyRain => "Heavy rain pours down, reducing visibility.",
        WeatherCondition.Thunderstorm => "Lightning flashes and thunder rumbles as rain lashes down.",
        WeatherCondition.Fog => "A thick fog hangs in the air, limiting visibility.",
        WeatherCondition.Snow => "Snowflakes drift down gently from the grey sky.",
        WeatherCondition.Blizzard => "A fierce blizzard howls, blinding you with snow.",
        _ => "The weather is unremarkable."
    };

    /// <summary>
    /// Returns a short weather status string.
    /// </summary>
    public string WeatherStatus => CurrentWeather switch
    {
        WeatherCondition.Clear => "clear",
        WeatherCondition.Cloudy => "cloudy",
        WeatherCondition.Overcast => "overcast",
        WeatherCondition.LightRain => "light rain",
        WeatherCondition.Rain => "raining",
        WeatherCondition.HeavyRain => "heavy rain",
        WeatherCondition.Thunderstorm => "thunderstorm",
        WeatherCondition.Fog => "foggy",
        WeatherCondition.Snow => "snowing",
        WeatherCondition.Blizzard => "blizzard",
        _ => "unknown"
    };

    /// <summary>
    /// Returns true if it's currently raining.
    /// </summary>
    public bool IsRaining => CurrentWeather is WeatherCondition.LightRain
        or WeatherCondition.Rain
        or WeatherCondition.HeavyRain
        or WeatherCondition.Thunderstorm;

    /// <summary>
    /// Returns true if visibility is reduced (fog, heavy rain, blizzard).
    /// </summary>
    public bool IsLowVisibility => CurrentWeather is WeatherCondition.Fog
        or WeatherCondition.HeavyRain
        or WeatherCondition.Blizzard;

    /// <summary>
    /// Returns true if conditions are dangerous.
    /// </summary>
    public bool IsDangerous => CurrentWeather is WeatherCondition.Thunderstorm
        or WeatherCondition.Blizzard;

    protected override void OnInitialize(IMudContext ctx)
    {
        // Initialize weather if not already set
        if (!ctx.State.Has("weather"))
        {
            ctx.State.Set("weather", (int)WeatherCondition.Clear);
            ctx.State.Set("temperature", 50);
            ctx.State.Set("wind", 10);
            ctx.State.Set("change_countdown", 10); // Change after ~5 minutes
        }
    }

    protected override void OnHeartbeat(IMudContext ctx)
    {
        // Decrement change countdown
        var countdown = ctx.State.Get<int>("change_countdown") - 1;
        if (countdown <= 0)
        {
            // Time for weather change
            ChangeWeather(ctx);
            countdown = 10 + _random.Next(10); // 5-10 minutes until next change
        }
        ctx.State.Set("change_countdown", countdown);

        // Slight temperature and wind variations
        var temp = ctx.State.Get<int>("temperature");
        temp = Math.Clamp(temp + _random.Next(-2, 3), 0, 100);
        ctx.State.Set("temperature", temp);

        var wind = ctx.State.Get<int>("wind");
        wind = Math.Clamp(wind + _random.Next(-5, 6), 0, 100);
        ctx.State.Set("wind", wind);
    }

    private void ChangeWeather(IMudContext ctx)
    {
        var current = (WeatherCondition)ctx.State.Get<int>("weather");
        var temp = ctx.State.Get<int>("temperature");

        // Weather transitions are gradual
        WeatherCondition next = current switch
        {
            WeatherCondition.Clear => _random.Next(100) < 70
                ? WeatherCondition.Clear
                : WeatherCondition.Cloudy,

            WeatherCondition.Cloudy => _random.Next(100) switch
            {
                < 30 => WeatherCondition.Clear,
                < 60 => WeatherCondition.Cloudy,
                < 80 => WeatherCondition.Overcast,
                _ => temp < 30 ? WeatherCondition.Fog : WeatherCondition.Cloudy
            },

            WeatherCondition.Overcast => _random.Next(100) switch
            {
                < 20 => WeatherCondition.Cloudy,
                < 40 => WeatherCondition.Overcast,
                < 70 => temp < 20 ? WeatherCondition.Snow : WeatherCondition.LightRain,
                _ => WeatherCondition.Fog
            },

            WeatherCondition.LightRain => _random.Next(100) switch
            {
                < 20 => WeatherCondition.Overcast,
                < 50 => WeatherCondition.LightRain,
                < 80 => WeatherCondition.Rain,
                _ => WeatherCondition.LightRain
            },

            WeatherCondition.Rain => _random.Next(100) switch
            {
                < 20 => WeatherCondition.LightRain,
                < 50 => WeatherCondition.Rain,
                < 70 => WeatherCondition.HeavyRain,
                _ => WeatherCondition.Thunderstorm
            },

            WeatherCondition.HeavyRain => _random.Next(100) switch
            {
                < 30 => WeatherCondition.Rain,
                < 60 => WeatherCondition.HeavyRain,
                _ => WeatherCondition.Thunderstorm
            },

            WeatherCondition.Thunderstorm => _random.Next(100) switch
            {
                < 40 => WeatherCondition.HeavyRain,
                < 70 => WeatherCondition.Rain,
                _ => WeatherCondition.Thunderstorm
            },

            WeatherCondition.Fog => _random.Next(100) switch
            {
                < 40 => WeatherCondition.Cloudy,
                < 70 => WeatherCondition.Fog,
                _ => WeatherCondition.Overcast
            },

            WeatherCondition.Snow => _random.Next(100) switch
            {
                < 20 => WeatherCondition.Overcast,
                < 60 => WeatherCondition.Snow,
                _ => temp < 10 ? WeatherCondition.Blizzard : WeatherCondition.Snow
            },

            WeatherCondition.Blizzard => _random.Next(100) switch
            {
                < 50 => WeatherCondition.Snow,
                _ => WeatherCondition.Blizzard
            },

            _ => WeatherCondition.Clear
        };

        ctx.State.Set("weather", (int)next);
    }

    /// <summary>
    /// Set the weather condition (for wizard commands).
    /// </summary>
    public void SetWeather(WeatherCondition condition)
    {
        Ctx?.State.Set("weather", (int)condition);
    }

    /// <summary>
    /// Set the temperature (for wizard commands).
    /// </summary>
    public void SetTemperature(int temp)
    {
        Ctx?.State.Set("temperature", Math.Clamp(temp, 0, 100));
    }

    /// <summary>
    /// Set the wind strength (for wizard commands).
    /// </summary>
    public void SetWind(int wind)
    {
        Ctx?.State.Set("wind", Math.Clamp(wind, 0, 100));
    }
}

/// <summary>
/// Weather conditions.
/// </summary>
public enum WeatherCondition
{
    Clear,
    Cloudy,
    Overcast,
    LightRain,
    Rain,
    HeavyRain,
    Thunderstorm,
    Fog,
    Snow,
    Blizzard
}
