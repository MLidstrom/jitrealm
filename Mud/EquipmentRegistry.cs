namespace JitRealm.Mud;

/// <summary>
/// Driver-managed equipment system. Tracks which items are equipped in which slots.
/// </summary>
public sealed class EquipmentRegistry
{
    // livingId -> (slot -> itemId)
    private readonly Dictionary<string, Dictionary<EquipmentSlot, string>> _equipment = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Equip an item to a slot. Returns the previously equipped item ID, or null.
    /// </summary>
    public string? Equip(string livingId, EquipmentSlot slot, string itemId)
    {
        if (!_equipment.TryGetValue(livingId, out var slots))
        {
            slots = new Dictionary<EquipmentSlot, string>();
            _equipment[livingId] = slots;
        }

        // Get the currently equipped item (if any)
        slots.TryGetValue(slot, out var previousItemId);

        // Equip the new item
        slots[slot] = itemId;

        return previousItemId;
    }

    /// <summary>
    /// Unequip an item from a slot. Returns the unequipped item ID, or null if slot was empty.
    /// </summary>
    public string? Unequip(string livingId, EquipmentSlot slot)
    {
        if (!_equipment.TryGetValue(livingId, out var slots))
            return null;

        if (!slots.TryGetValue(slot, out var itemId))
            return null;

        slots.Remove(slot);
        return itemId;
    }

    /// <summary>
    /// Get the item equipped in a specific slot.
    /// </summary>
    public string? GetEquipped(string livingId, EquipmentSlot slot)
    {
        if (!_equipment.TryGetValue(livingId, out var slots))
            return null;

        return slots.TryGetValue(slot, out var itemId) ? itemId : null;
    }

    /// <summary>
    /// Get all equipped items for a living being.
    /// </summary>
    public IReadOnlyDictionary<EquipmentSlot, string> GetAllEquipped(string livingId)
    {
        if (!_equipment.TryGetValue(livingId, out var slots))
            return new Dictionary<EquipmentSlot, string>();

        return slots;
    }

    /// <summary>
    /// Check if an item is currently equipped by anyone.
    /// </summary>
    public bool IsEquipped(string itemId)
    {
        foreach (var kvp in _equipment)
        {
            foreach (var slotKvp in kvp.Value)
            {
                if (string.Equals(slotKvp.Value, itemId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Find which living has an item equipped and in which slot.
    /// </summary>
    public (string? livingId, EquipmentSlot? slot) FindEquippedBy(string itemId)
    {
        foreach (var kvp in _equipment)
        {
            foreach (var slotKvp in kvp.Value)
            {
                if (string.Equals(slotKvp.Value, itemId, StringComparison.OrdinalIgnoreCase))
                    return (kvp.Key, slotKvp.Key);
            }
        }
        return (null, null);
    }

    /// <summary>
    /// Remove all equipment for a living being (e.g., on death or destruct).
    /// Returns all previously equipped items.
    /// </summary>
    public IReadOnlyDictionary<EquipmentSlot, string> ClearEquipment(string livingId)
    {
        if (_equipment.TryGetValue(livingId, out var slots))
        {
            var result = new Dictionary<EquipmentSlot, string>(slots);
            slots.Clear();
            return result;
        }
        return new Dictionary<EquipmentSlot, string>();
    }

    /// <summary>
    /// Export all equipment data for serialization.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> ToSerializable()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _equipment)
        {
            if (kvp.Value.Count > 0)
            {
                var slotDict = new Dictionary<string, string>();
                foreach (var slotKvp in kvp.Value)
                {
                    slotDict[slotKvp.Key.ToString()] = slotKvp.Value;
                }
                result[kvp.Key] = slotDict;
            }
        }
        return result;
    }

    /// <summary>
    /// Import equipment data from serialization.
    /// </summary>
    public void FromSerializable(Dictionary<string, Dictionary<string, string>>? data)
    {
        _equipment.Clear();

        if (data is null)
            return;

        foreach (var kvp in data)
        {
            var slots = new Dictionary<EquipmentSlot, string>();
            foreach (var slotKvp in kvp.Value)
            {
                if (Enum.TryParse<EquipmentSlot>(slotKvp.Key, ignoreCase: true, out var slot))
                {
                    slots[slot] = slotKvp.Value;
                }
            }
            if (slots.Count > 0)
            {
                _equipment[kvp.Key] = slots;
            }
        }
    }
}
