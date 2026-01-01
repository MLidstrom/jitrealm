using System.Text;

namespace JitRealm.Mud.Formatting;

/// <summary>
/// Plain text formatter for clients that don't support ANSI colors.
/// </summary>
public sealed class PlainTextFormatter : IMudFormatter
{
    // Room display
    public string FormatRoomName(string name) => name;

    public string FormatRoomDescription(string description) => description;

    public string FormatExits(IEnumerable<string> exits) =>
        $"Exits: {string.Join(", ", exits)}";

    public string FormatPlayersHere(IEnumerable<string> playerNames) =>
        $"Players here: {string.Join(", ", playerNames)}";

    public string FormatObjectsHere(string formattedList) =>
        $"You see: {formattedList}";

    // Chat and communication
    public string FormatSay(string speaker, string message) =>
        $"{speaker} says: {message}";

    public string FormatYouSay(string message) =>
        $"You say: {message}";

    public string FormatTell(string speaker, string message) =>
        $"{speaker} tells you: {message}";

    public string FormatEmote(string actor, string action) =>
        $"{actor} {action}";

    public string FormatYouEmote(string action) =>
        $"You {action}";

    public string FormatShout(string speaker, string message) =>
        $"{speaker} shouts: {message}";

    // Combat
    public string FormatYouAttack(string target, int damage) =>
        $"You hit {target} for {damage} damage!";

    public string FormatYouAreAttacked(string attacker, int damage) =>
        $"{attacker} hits you for {damage} damage!";

    public string FormatAttack(string attacker, string target, int damage) =>
        $"{attacker} hits {target} for {damage} damage!";

    public string FormatDeath(string victimName) =>
        $"{victimName} has been slain!";

    public string FormatXpGain(int amount) =>
        $"You gain {amount} experience points!";

    public string FormatFleeSuccess(string direction) =>
        $"You flee to the {direction}!";

    public string FormatFleeFail() =>
        "You fail to escape!";

    public string FormatCombatStart(string targetName) =>
        $"You attack {targetName}!";

    public string FormatConsider(string targetName, string difficulty, int hp, int maxHp, int? armorClass, (int min, int max)? weaponDamage)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{targetName} looks like {difficulty}.");
        sb.AppendLine($"  HP: {hp}/{maxHp}");

        if (armorClass.HasValue)
            sb.AppendLine($"  Armor Class: {armorClass.Value}");

        if (weaponDamage.HasValue && weaponDamage.Value.max > 0)
            sb.Append($"  Weapon Damage: {weaponDamage.Value.min}-{weaponDamage.Value.max}");

        return sb.ToString().TrimEnd();
    }

    // Player stats
    public string FormatScoreHeader(string playerName, bool isWizard)
    {
        var header = $"=== {playerName} ===";
        if (isWizard)
            header += "\n  Status: Wizard";
        return header;
    }

    public string FormatLevel(int level) =>
        $"  Level: {level}";

    public string FormatHpBar(int current, int max, int barLength = 20)
    {
        var percent = max > 0 ? (double)current / max : 0;
        var filledLength = (int)(percent * barLength);

        var filled = new string('\u2588', filledLength);  // █
        var empty = new string('\u2591', barLength - filledLength);  // ░

        return $"  HP: [{filled}{empty}] {current}/{max}";
    }

    public string FormatXpProgress(int current, int needed) =>
        $"  XP: {current}/{needed} to next level";

    public string FormatCombatStats(int armorClass, int minDamage, int maxDamage) =>
        $"  Armor: {armorClass}  Damage: {minDamage}-{maxDamage}";

    public string FormatCarryWeight(int current, int max) =>
        $"  Carry: {current}/{max}";

    public string FormatSessionTime(TimeSpan time) =>
        $"  Session: {FormatTimeSpan(time)}";

    public string FormatTotalPlaytime(TimeSpan time) =>
        $"  Total playtime: {FormatTimeSpan(time)}";

    // Inventory
    public string FormatInventoryHeader() =>
        "You are carrying:";

    public string FormatInventoryEmpty() =>
        "You are not carrying anything.";

    public string FormatInventoryItem(string description, int weight, int count = 1)
    {
        var itemText = count > 1 ? $"{count} {description}" : description;
        return $"  {itemText} ({weight} lbs)";
    }

    public string FormatInventoryTotal(int currentWeight, int maxWeight) =>
        $"Total weight: {currentWeight}/{maxWeight} lbs";

    // Equipment
    public string FormatEquipmentHeader() =>
        "You have equipped:";

    public string FormatEquipmentEmpty() =>
        "You have nothing equipped.";

    public string FormatEquipmentSlot(string slot, string itemDescription, string? stats = null)
    {
        var line = $"  {slot,-12}: {itemDescription}";
        if (!string.IsNullOrEmpty(stats))
            line += $" {stats}";
        return line;
    }

    public string FormatEquipmentTotals(int totalAC, int minDamage, int maxDamage)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        if (totalAC > 0)
            sb.AppendLine($"Total Armor Class: {totalAC}");
        if (maxDamage > 0)
            sb.Append($"Weapon Damage: {minDamage}-{maxDamage}");
        return sb.ToString().TrimEnd();
    }

    // Items
    public string FormatPickup(string itemDescription) =>
        $"You pick up {itemDescription}.";

    public string FormatDrop(string itemDescription) =>
        $"You drop {itemDescription}.";

    public string FormatEquip(string itemDescription, string slot) =>
        $"You equip {itemDescription} ({slot}).";

    public string FormatUnequip(string itemDescription) =>
        $"You unequip {itemDescription}.";

    public string FormatExamine(string longDescription, int weight, int value)
    {
        var sb = new StringBuilder();
        sb.AppendLine(longDescription);
        sb.AppendLine($"  Weight: {weight} lbs");
        sb.Append($"  Value: {value} coins");
        return sb.ToString();
    }

    // System messages
    public string FormatError(string message) => message;

    public string FormatSuccess(string message) => message;

    public string FormatInfo(string message) => message;

    public string FormatWelcome(string message) => message;

    public string FormatHelp(string category, string content) =>
        $"=== {category} ===\n{content}";

    // Who list
    public string FormatWhoHeader(int count) =>
        $"Players online: {count}";

    public string FormatWhoEntry(string playerName, string location, bool isWizard = false) =>
        $"  {playerName} - {location}";

    // Time
    public string FormatServerTime(DateTimeOffset time) =>
        $"Server time: {time:yyyy-MM-dd HH:mm:ss zzz}";

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
