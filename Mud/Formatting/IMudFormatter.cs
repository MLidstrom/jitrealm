namespace JitRealm.Mud.Formatting;

/// <summary>
/// Interface for formatting MUD output with optional ANSI colors.
/// </summary>
public interface IMudFormatter
{
    // Room display
    string FormatRoomName(string name);
    string FormatRoomDescription(string description);
    string FormatExits(IEnumerable<string> exits);
    string FormatPlayersHere(IEnumerable<string> playerNames);
    string FormatObjectsHere(string formattedList);

    // Chat and communication
    string FormatSay(string speaker, string message);
    string FormatYouSay(string message);
    string FormatTell(string speaker, string message);
    string FormatEmote(string actor, string action);
    string FormatYouEmote(string action);
    string FormatShout(string speaker, string message);

    // Combat
    string FormatYouAttack(string target, int damage);
    string FormatYouAreAttacked(string attacker, int damage);
    string FormatAttack(string attacker, string target, int damage);
    string FormatDeath(string victimName);
    string FormatXpGain(int amount);
    string FormatFleeSuccess(string direction);
    string FormatFleeFail();
    string FormatCombatStart(string targetName);
    string FormatConsider(string targetName, string difficulty, int hp, int maxHp, int? armorClass, (int min, int max)? weaponDamage);

    // Player stats
    string FormatScoreHeader(string playerName, bool isWizard);
    string FormatLevel(int level);
    string FormatHpBar(int current, int max, int barLength = 20);
    string FormatXpProgress(int current, int needed);
    string FormatCombatStats(int armorClass, int minDamage, int maxDamage);
    string FormatCarryWeight(int current, int max);
    string FormatSessionTime(TimeSpan time);
    string FormatTotalPlaytime(TimeSpan time);

    // Inventory
    string FormatInventoryHeader();
    string FormatInventoryEmpty();
    string FormatInventoryItem(string description, int weight, int count = 1);
    string FormatInventoryTotal(int currentWeight, int maxWeight);

    // Equipment
    string FormatEquipmentHeader();
    string FormatEquipmentEmpty();
    string FormatEquipmentSlot(string slot, string itemDescription, string? stats = null);
    string FormatEquipmentTotals(int totalAC, int minDamage, int maxDamage);

    // Items
    string FormatPickup(string itemDescription);
    string FormatDrop(string itemDescription);
    string FormatEquip(string itemDescription, string slot);
    string FormatUnequip(string itemDescription);
    string FormatExamine(string longDescription, int weight, int value);

    // System messages
    string FormatError(string message);
    string FormatSuccess(string message);
    string FormatInfo(string message);
    string FormatWelcome(string message);
    string FormatHelp(string category, string content);

    // Who list
    string FormatWhoHeader(int count);
    string FormatWhoEntry(string playerName, string location, bool isWizard = false);

    // Time
    string FormatServerTime(DateTimeOffset time);
}
