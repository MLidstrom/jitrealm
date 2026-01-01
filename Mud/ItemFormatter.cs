namespace JitRealm.Mud;

/// <summary>
/// Utility class for formatting item lists with grouping, pluralization, and articles.
/// </summary>
public static class ItemFormatter
{
    /// <summary>
    /// Format a list of item names with grouping and proper grammar.
    /// Examples: "A rusty sword", "2 rusty swords", "An iron helmet"
    /// </summary>
    public static IEnumerable<string> FormatGrouped(IEnumerable<string> names)
    {
        var grouped = names
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var count = group.Count();
            var name = group.Key;

            if (count == 1)
            {
                yield return WithArticle(name);
            }
            else
            {
                yield return $"{count} {Pluralize(name)}";
            }
        }
    }

    /// <summary>
    /// Format a list of item names as a single comma-separated string.
    /// </summary>
    public static string FormatGroupedList(IEnumerable<string> names)
    {
        var items = FormatGrouped(names).ToList();
        return items.Count > 0 ? string.Join(", ", items) : "";
    }

    /// <summary>
    /// Add an indefinite article (a/an) to a name.
    /// Skips if the name already starts with an article.
    /// </summary>
    public static string WithArticle(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        // Check if name already starts with an article
        var lower = name.ToLowerInvariant();
        if (lower.StartsWith("a ") || lower.StartsWith("an ") || lower.StartsWith("the "))
            return name;

        var firstChar = char.ToLowerInvariant(name[0]);
        var article = IsVowel(firstChar) ? "an" : "a";
        return $"{article} {name}";
    }

    /// <summary>
    /// Pluralize a noun using basic English rules.
    /// </summary>
    public static string Pluralize(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return word;

        var lower = word.ToLowerInvariant();

        // Handle common irregular plurals
        if (IrregularPlurals.TryGetValue(lower, out var irregular))
            return PreserveCase(word, irregular);

        // Words ending in s, x, z, ch, sh -> add "es"
        if (lower.EndsWith("s") || lower.EndsWith("x") || lower.EndsWith("z") ||
            lower.EndsWith("ch") || lower.EndsWith("sh"))
        {
            return word + "es";
        }

        // Words ending in consonant + y -> change y to ies
        if (lower.EndsWith("y") && lower.Length > 1 && !IsVowel(lower[^2]))
        {
            return word[..^1] + "ies";
        }

        // Words ending in f or fe -> change to ves (common cases)
        if (lower.EndsWith("fe"))
        {
            return word[..^2] + "ves";
        }
        if (lower.EndsWith("f") && !lower.EndsWith("ff") && !lower.EndsWith("roof") && !lower.EndsWith("chief"))
        {
            return word[..^1] + "ves";
        }

        // Words ending in o preceded by consonant -> add "es" (common cases)
        if (lower.EndsWith("o") && lower.Length > 1 && !IsVowel(lower[^2]))
        {
            // Exceptions that just take 's'
            if (!lower.EndsWith("photo") && !lower.EndsWith("piano") && !lower.EndsWith("zero"))
            {
                return word + "es";
            }
        }

        // Default: add "s"
        return word + "s";
    }

    private static bool IsVowel(char c) => "aeiou".Contains(c);

    private static string PreserveCase(string original, string replacement)
    {
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(replacement))
            return replacement;

        // If original starts with uppercase, capitalize replacement
        if (char.IsUpper(original[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];

        return replacement;
    }

    private static readonly Dictionary<string, string> IrregularPlurals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["man"] = "men",
        ["woman"] = "women",
        ["child"] = "children",
        ["tooth"] = "teeth",
        ["foot"] = "feet",
        ["goose"] = "geese",
        ["mouse"] = "mice",
        ["ox"] = "oxen",
        ["fish"] = "fish",
        ["sheep"] = "sheep",
        ["deer"] = "deer",
        ["staff"] = "staves",
        ["knife"] = "knives",
        ["life"] = "lives",
        ["wife"] = "wives",
        ["wolf"] = "wolves",
        ["elf"] = "elves",
        ["dwarf"] = "dwarves",
        ["thief"] = "thieves",
        ["leaf"] = "leaves",
        ["loaf"] = "loaves",
        ["calf"] = "calves",
        ["half"] = "halves",
        ["self"] = "selves",
        ["shelf"] = "shelves"
    };
}
