using Spectre.Console;

namespace JitRealm.Mud.Formatting;

/// <summary>
/// Color theme constants for MUD output formatting.
/// </summary>
public static class MudTheme
{
    // Room display
    public static readonly Color RoomName = Color.Cyan1;
    public static readonly Color RoomDescription = Color.Grey;
    public static readonly Color Exits = Color.Yellow;

    // Entity names
    public static readonly Color PlayerName = Color.Green;
    public static readonly Color NpcName = Color.Blue;
    public static readonly Color WizardName = Color.Magenta1;
    public static readonly Color MonsterName = Color.Red;

    // Chat and communication
    public static readonly Color Say = Color.White;
    public static readonly Color Tell = Color.Magenta1;
    public static readonly Color Emote = Color.Aqua;
    public static readonly Color Shout = Color.Yellow;

    // Combat
    public static readonly Color DamageDealt = Color.Green;
    public static readonly Color DamageReceived = Color.Red;
    public static readonly Color CombatInfo = Color.Orange1;
    public static readonly Color Death = Color.DarkRed;
    public static readonly Color XpGain = Color.Gold1;

    // HP bar colors (gradient based on percentage)
    public static readonly Color HpFull = Color.Green;        // 75%+
    public static readonly Color HpMedium = Color.Yellow;     // 50-75%
    public static readonly Color HpLow = Color.Orange1;       // 25-50%
    public static readonly Color HpCritical = Color.Red;      // <25%

    // Status and feedback
    public static readonly Color Success = Color.Green;
    public static readonly Color Error = Color.Red;
    public static readonly Color Warning = Color.Yellow;
    public static readonly Color Info = Color.Grey;

    // Items and equipment
    public static readonly Color ItemName = Color.White;
    public static readonly Color WeaponStats = Color.Orange1;
    public static readonly Color ArmorStats = Color.SteelBlue;
    public static readonly Color Weight = Color.Grey;

    // System
    public static readonly Color SystemMessage = Color.Grey;
    public static readonly Color Command = Color.Cyan1;

    /// <summary>
    /// Gets the appropriate HP bar color based on current HP percentage.
    /// </summary>
    public static Color GetHpColor(int current, int max)
    {
        if (max <= 0) return HpCritical;
        var percent = (double)current / max;
        return percent switch
        {
            >= 0.75 => HpFull,
            >= 0.50 => HpMedium,
            >= 0.25 => HpLow,
            _ => HpCritical
        };
    }
}
