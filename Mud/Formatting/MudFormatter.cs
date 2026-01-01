using System.Text;
using Spectre.Console;

namespace JitRealm.Mud.Formatting;

/// <summary>
/// Formatter that produces ANSI-colored output using basic 16-color escape sequences.
/// Uses Spectre.Console's Color type for theme colors but generates ANSI codes directly.
/// </summary>
public sealed class MudFormatter : IMudFormatter
{
    private const string Reset = "\u001b[0m";
    private const string Bold = "\u001b[1m";

    // Basic ANSI color codes (foreground)
    private const string Black = "\u001b[30m";
    private const string Red = "\u001b[31m";
    private const string Green = "\u001b[32m";
    private const string Yellow = "\u001b[33m";
    private const string Blue = "\u001b[34m";
    private const string Magenta = "\u001b[35m";
    private const string Cyan = "\u001b[36m";
    private const string White = "\u001b[37m";
    private const string BrightBlack = "\u001b[90m";
    private const string BrightRed = "\u001b[91m";
    private const string BrightGreen = "\u001b[92m";
    private const string BrightYellow = "\u001b[93m";
    private const string BrightBlue = "\u001b[94m";
    private const string BrightMagenta = "\u001b[95m";
    private const string BrightCyan = "\u001b[96m";
    private const string BrightWhite = "\u001b[97m";

    /// <summary>
    /// Gets ANSI color code for a Spectre.Console Color by matching RGB values.
    /// Dictionary<Color, string> doesn't work reliably due to Color struct equality issues.
    /// </summary>
    private static string Fg(Color color)
    {
        // Compare by RGB values since Color struct equality is unreliable as dictionary keys
        var (r, g, b) = (color.R, color.G, color.B);

        // Room colors
        if (Matches(r, g, b, Color.Cyan1)) return BrightCyan;
        if (Matches(r, g, b, Color.Grey)) return BrightBlack;
        if (Matches(r, g, b, Color.Yellow)) return BrightYellow;

        // Entity colors
        if (Matches(r, g, b, Color.Green)) return BrightGreen;
        if (Matches(r, g, b, Color.Blue)) return BrightBlue;
        if (Matches(r, g, b, Color.Magenta1)) return BrightMagenta;
        if (Matches(r, g, b, Color.Red)) return BrightRed;

        // Chat colors
        if (Matches(r, g, b, Color.White)) return White;
        if (Matches(r, g, b, Color.Aqua)) return Cyan;

        // Combat colors
        if (Matches(r, g, b, Color.Orange1)) return Yellow;  // No orange in basic ANSI
        if (Matches(r, g, b, Color.DarkRed)) return Red;
        if (Matches(r, g, b, Color.Gold1)) return BrightYellow;

        // Stats colors
        if (Matches(r, g, b, Color.SteelBlue)) return Blue;
        if (Matches(r, g, b, Color.Grey37)) return BrightBlack;

        return White;  // Default fallback
    }

    private static bool Matches(byte r, byte g, byte b, Color target) =>
        r == target.R && g == target.G && b == target.B;

    /// <summary>
    /// Wraps text in ANSI color codes with optional bold.
    /// </summary>
    private static string Ansi(string text, Color color, bool bold = false)
    {
        if (bold)
            return $"{Bold}{Fg(color)}{text}{Reset}";
        return $"{Fg(color)}{text}{Reset}";
    }

    // Room display
    public string FormatRoomName(string name) =>
        Ansi(name, MudTheme.RoomName, bold: true);

    public string FormatRoomDescription(string description) =>
        Ansi(description, MudTheme.RoomDescription);

    public string FormatExits(IEnumerable<string> exits) =>
        $"{Ansi("Exits:", MudTheme.Exits)} {string.Join(", ", exits)}";

    public string FormatPlayersHere(IEnumerable<string> playerNames) =>
        $"{Ansi("Players here:", MudTheme.Info)} {Ansi(string.Join(", ", playerNames), MudTheme.PlayerName)}";

    public string FormatObjectsHere(string formattedList) =>
        $"{Ansi("You see:", MudTheme.Info)} {formattedList}";

    // Chat and communication
    public string FormatSay(string speaker, string message) =>
        $"{Ansi(speaker, MudTheme.PlayerName)} says: {Ansi(message, MudTheme.Say)}";

    public string FormatYouSay(string message) =>
        $"You say: {Ansi(message, MudTheme.Say)}";

    public string FormatTell(string speaker, string message) =>
        $"{Ansi(speaker, MudTheme.PlayerName)} tells you: {Ansi(message, MudTheme.Tell)}";

    public string FormatEmote(string actor, string action) =>
        Ansi($"{actor} {action}", MudTheme.Emote);

    public string FormatYouEmote(string action) =>
        Ansi($"You {action}", MudTheme.Emote);

    public string FormatShout(string speaker, string message) =>
        $"{Ansi(speaker, MudTheme.PlayerName)} shouts: {Ansi(message, MudTheme.Shout, bold: true)}";

    // Combat
    public string FormatYouAttack(string target, int damage) =>
        Ansi($"You hit {target} for {damage} damage!", MudTheme.DamageDealt);

    public string FormatYouAreAttacked(string attacker, int damage) =>
        Ansi($"{attacker} hits you for {damage} damage!", MudTheme.DamageReceived);

    public string FormatAttack(string attacker, string target, int damage) =>
        Ansi($"{attacker} hits {target} for {damage} damage!", MudTheme.CombatInfo);

    public string FormatDeath(string victimName) =>
        Ansi($"{victimName} has been slain!", MudTheme.Death, bold: true);

    public string FormatXpGain(int amount) =>
        Ansi($"You gain {amount} experience points!", MudTheme.XpGain);

    public string FormatFleeSuccess(string direction) =>
        Ansi($"You flee to the {direction}!", MudTheme.Success);

    public string FormatFleeFail() =>
        Ansi("You fail to escape!", MudTheme.Error);

    public string FormatCombatStart(string targetName) =>
        Ansi($"You attack {targetName}!", MudTheme.CombatInfo, bold: true);

    public string FormatConsider(string targetName, string difficulty, int hp, int maxHp, int? armorClass, (int min, int max)? weaponDamage)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Ansi(targetName, MudTheme.NpcName)} looks like {Ansi(difficulty, GetDifficultyColor(difficulty))}.");

        var hpColor = MudTheme.GetHpColor(hp, maxHp);
        sb.AppendLine($"  HP: {Ansi($"{hp}/{maxHp}", hpColor)}");

        if (armorClass.HasValue)
            sb.AppendLine($"  Armor Class: {Ansi(armorClass.Value.ToString(), MudTheme.ArmorStats)}");

        if (weaponDamage.HasValue && weaponDamage.Value.max > 0)
            sb.Append($"  Weapon Damage: {Ansi($"{weaponDamage.Value.min}-{weaponDamage.Value.max}", MudTheme.WeaponStats)}");

        return sb.ToString().TrimEnd();
    }

    private static Color GetDifficultyColor(string difficulty) => difficulty switch
    {
        "an easy target" => Color.Green,
        "a fair fight" => Color.Yellow,
        "a challenging opponent" => Color.Orange1,
        "a dangerous foe" => Color.Red,
        "certain death" => Color.DarkRed,
        _ => Color.Grey
    };

    // Player stats
    public string FormatScoreHeader(string playerName, bool isWizard)
    {
        var nameColor = isWizard ? MudTheme.WizardName : MudTheme.PlayerName;
        var header = $"=== {Ansi(playerName, nameColor, bold: true)} ===";
        if (isWizard)
            header += $"\n  Status: {Ansi("Wizard", MudTheme.WizardName)}";
        return header;
    }

    public string FormatLevel(int level) =>
        $"  Level: {Ansi(level.ToString(), MudTheme.Info, bold: true)}";

    public string FormatHpBar(int current, int max, int barLength = 20)
    {
        var percent = max > 0 ? (double)current / max : 0;
        var filledLength = (int)(percent * barLength);

        var hpColor = MudTheme.GetHpColor(current, max);
        var filled = new string('\u2588', filledLength);  // █
        var empty = new string('\u2591', barLength - filledLength);  // ░

        return $"  HP: [{Ansi(filled, hpColor)}{Ansi(empty, Color.Grey37)}] {Ansi($"{current}/{max}", hpColor)}";
    }

    public string FormatXpProgress(int current, int needed) =>
        $"  XP: {Ansi($"{current}/{needed}", MudTheme.XpGain)} to next level";

    public string FormatCombatStats(int armorClass, int minDamage, int maxDamage) =>
        $"  Armor: {Ansi(armorClass.ToString(), MudTheme.ArmorStats)}  Damage: {Ansi($"{minDamage}-{maxDamage}", MudTheme.WeaponStats)}";

    public string FormatCarryWeight(int current, int max) =>
        $"  Carry: {Ansi($"{current}/{max}", current > max * 0.9 ? MudTheme.Warning : MudTheme.Info)}";

    public string FormatSessionTime(TimeSpan time) =>
        $"  Session: {Ansi(FormatTimeSpan(time), MudTheme.Info)}";

    public string FormatTotalPlaytime(TimeSpan time) =>
        $"  Total playtime: {Ansi(FormatTimeSpan(time), MudTheme.Info)}";

    // Inventory
    public string FormatInventoryHeader() =>
        Ansi("You are carrying:", MudTheme.Info, bold: true);

    public string FormatInventoryEmpty() =>
        Ansi("You are not carrying anything.", MudTheme.Info);

    public string FormatInventoryItem(string description, int weight, int count = 1)
    {
        var itemText = count > 1 ? $"{count} {description}" : description;
        return $"  {Ansi(itemText, MudTheme.ItemName)} {Ansi($"({weight} lbs)", MudTheme.Weight)}";
    }

    public string FormatInventoryTotal(int currentWeight, int maxWeight)
    {
        var color = currentWeight > maxWeight * 0.9 ? MudTheme.Warning : MudTheme.Info;
        return $"Total weight: {Ansi($"{currentWeight}/{maxWeight} lbs", color)}";
    }

    // Equipment
    public string FormatEquipmentHeader() =>
        Ansi("You have equipped:", MudTheme.Info, bold: true);

    public string FormatEquipmentEmpty() =>
        Ansi("You have nothing equipped.", MudTheme.Info);

    public string FormatEquipmentSlot(string slot, string itemDescription, string? stats = null)
    {
        var line = $"  {Ansi($"{slot,-12}", MudTheme.Info)}: {Ansi(itemDescription, MudTheme.ItemName)}";
        if (!string.IsNullOrEmpty(stats))
            line += $" {Ansi(stats, MudTheme.WeaponStats)}";
        return line;
    }

    public string FormatEquipmentTotals(int totalAC, int minDamage, int maxDamage)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        if (totalAC > 0)
            sb.AppendLine($"Total Armor Class: {Ansi(totalAC.ToString(), MudTheme.ArmorStats)}");
        if (maxDamage > 0)
            sb.Append($"Weapon Damage: {Ansi($"{minDamage}-{maxDamage}", MudTheme.WeaponStats)}");
        return sb.ToString().TrimEnd();
    }

    // Items
    public string FormatPickup(string itemDescription) =>
        $"You pick up {Ansi(itemDescription, MudTheme.ItemName)}.";

    public string FormatDrop(string itemDescription) =>
        $"You drop {Ansi(itemDescription, MudTheme.ItemName)}.";

    public string FormatEquip(string itemDescription, string slot) =>
        $"You equip {Ansi(itemDescription, MudTheme.ItemName)} ({Ansi(slot, MudTheme.Info)}).";

    public string FormatUnequip(string itemDescription) =>
        $"You unequip {Ansi(itemDescription, MudTheme.ItemName)}.";

    public string FormatExamine(string longDescription, int weight, int value)
    {
        var sb = new StringBuilder();
        sb.AppendLine(longDescription);
        sb.AppendLine($"  Weight: {Ansi($"{weight} lbs", MudTheme.Weight)}");
        sb.Append($"  Value: {Ansi($"{value} coins", MudTheme.XpGain)}");
        return sb.ToString();
    }

    // System messages
    public string FormatError(string message) =>
        Ansi(message, MudTheme.Error);

    public string FormatSuccess(string message) =>
        Ansi(message, MudTheme.Success);

    public string FormatInfo(string message) =>
        Ansi(message, MudTheme.Info);

    public string FormatWelcome(string message) =>
        Ansi(message, MudTheme.Success, bold: true);

    public string FormatHelp(string category, string content) =>
        $"{Ansi($"=== {category} ===", MudTheme.Info, bold: true)}\n{content}";

    // Who list
    public string FormatWhoHeader(int count) =>
        $"Players online: {Ansi(count.ToString(), MudTheme.Info, bold: true)}";

    public string FormatWhoEntry(string playerName, string location, bool isWizard = false)
    {
        var nameColor = isWizard ? MudTheme.WizardName : MudTheme.PlayerName;
        return $"  {Ansi(playerName, nameColor)} - {Ansi(location, MudTheme.Info)}";
    }

    // Time
    public string FormatServerTime(DateTimeOffset time) =>
        $"Server time: {Ansi(time.ToString("yyyy-MM-dd HH:mm:ss zzz"), MudTheme.Info)}";

    // Helper methods
    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        if (ts.TotalMinutes >= 1)
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
    }
}
