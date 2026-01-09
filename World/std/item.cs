namespace JitRealm.World.Std;

using JitRealm.Mud;

/// <summary>
/// Base class for all carryable items.
/// Extend this class to create items that can be picked up, dropped, and traded.
/// Items are stackable by default - identical items merge when placed in the same container.
/// </summary>
public class ItemBase : MudObjectBase, ICarryable, IStackable, IOnLoad
{
    protected IMudContext? Ctx { get; private set; }

    public override string Name => Ctx?.State.Get<string>("name") ?? GetDefaultName();

    /// <summary>
    /// Number of items in this stack. Default 1.
    /// </summary>
    public virtual int Amount => Ctx?.State.Get<int>("amount") ?? 1;

    /// <summary>
    /// Key used to identify items that can stack together.
    /// Default uses the blueprint ID, so identical items stack.
    /// Override to prevent stacking (return unique value like Id).
    /// </summary>
    public virtual string StackKey => GetBlueprintId();

    /// <summary>
    /// Default name when not overridden in state. Override in subclasses.
    /// </summary>
    protected virtual string GetDefaultName() => "item";

    /// <summary>
    /// Description shown when examining. Can be patched via state.
    /// </summary>
    public override string Description => Ctx?.State.Get<string>("description") ?? GetDefaultDescription();

    /// <summary>
    /// Default description when not overridden in state. Override in subclasses.
    /// </summary>
    protected virtual string GetDefaultDescription() => "A nondescript item.";

    public virtual int Weight => Ctx?.State.Get<int>("weight") ?? GetDefaultWeight();
    protected virtual int GetDefaultWeight() => 1;

    public virtual int Value => Ctx?.State.Get<int>("value") ?? GetDefaultValue();
    protected virtual int GetDefaultValue() => 0;

    public virtual string ShortDescription => FormatShortDescription();

    protected virtual string GetDefaultShortDescription() => Name;

    /// <summary>
    /// Format the short description, including count for stacked items.
    /// </summary>
    protected virtual string FormatShortDescription()
    {
        var baseDesc = Ctx?.State.Get<string>("short_desc") ?? GetDefaultShortDescription();
        if (Amount <= 1)
            return baseDesc;

        // For stacked items, pluralize
        return $"{Amount} {ItemFormatter.Pluralize(StripArticle(baseDesc))}";
    }

    /// <summary>
    /// Remove leading article (a/an/the) from a string.
    /// </summary>
    private static string StripArticle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var lower = text.ToLowerInvariant();
        if (lower.StartsWith("a "))
            return text[2..];
        if (lower.StartsWith("an "))
            return text[3..];
        if (lower.StartsWith("the "))
            return text[4..];
        return text;
    }

    /// <summary>
    /// Get the blueprint ID from this item's instance ID.
    /// </summary>
    protected string GetBlueprintId()
    {
        var hashIndex = Id.IndexOf('#');
        return hashIndex >= 0 ? Id[..hashIndex] : Id;
    }

    /// <summary>
    /// Alternative names/keywords for looking up this item.
    /// Override in subclasses or call SetAliases() to customize.
    /// Default returns an empty list (falls back to Name matching).
    /// </summary>
    public virtual IReadOnlyList<string> Aliases => Ctx?.State.Get<string[]>("aliases") ?? Array.Empty<string>();

    public virtual void OnLoad(IMudContext ctx)
    {
        Ctx = ctx;

        // Initialize defaults if not set
        if (!ctx.State.Has("name"))
        {
            ctx.State.Set("name", "item");
        }
        if (!ctx.State.Has("weight"))
        {
            ctx.State.Set("weight", 1);
        }
        if (!ctx.State.Has("value"))
        {
            ctx.State.Set("value", 0);
        }
        if (!ctx.State.Has("short_desc"))
        {
            ctx.State.Set("short_desc", GetDefaultShortDescription());
        }
        if (!ctx.State.Has("description"))
        {
            ctx.State.Set("description", GetDefaultDescription());
        }
        if (!ctx.State.Has("amount"))
        {
            ctx.State.Set("amount", 1);
        }
    }

    public virtual void OnGet(IMudContext ctx, string pickerId)
    {
        // Default: no special behavior
    }

    public virtual void OnDrop(IMudContext ctx, string dropperId)
    {
        // Default: no special behavior
    }

    public virtual void OnGive(IMudContext ctx, string giverId, string receiverId)
    {
        // Default: no special behavior
    }

    /// <summary>
    /// Set the item's name.
    /// </summary>
    public void SetName(string name, IMudContext ctx)
    {
        ctx.State.Set("name", name);
    }

    /// <summary>
    /// Set the item's weight.
    /// </summary>
    public void SetWeight(int weight, IMudContext ctx)
    {
        ctx.State.Set("weight", weight);
    }

    /// <summary>
    /// Set the item's value.
    /// </summary>
    public void SetValue(int value, IMudContext ctx)
    {
        ctx.State.Set("value", value);
    }

    /// <summary>
    /// Set the item's short description.
    /// </summary>
    public void SetShortDescription(string desc, IMudContext ctx)
    {
        ctx.State.Set("short_desc", desc);
    }

    /// <summary>
    /// Set the item's description.
    /// </summary>
    public void SetDescription(string desc, IMudContext ctx)
    {
        ctx.State.Set("description", desc);
    }

    /// <summary>
    /// Set the item's aliases (alternative lookup names).
    /// </summary>
    public void SetAliases(string[] aliases, IMudContext ctx)
    {
        ctx.State.Set("aliases", aliases);
    }
}

/// <summary>
/// Base class for container items that can hold other items.
/// </summary>
public class ContainerBase : ItemBase, IContainer
{
    public virtual int MaxCapacity => Ctx?.State.Get<int>("max_capacity") ?? 100;
    public virtual bool IsOpen => Ctx?.State.Get<bool>("is_open") ?? false;

    public override void OnLoad(IMudContext ctx)
    {
        base.OnLoad(ctx);

        // Initialize container defaults if not set
        if (!ctx.State.Has("max_capacity"))
        {
            ctx.State.Set("max_capacity", 100);
        }
        if (!ctx.State.Has("is_open"))
        {
            ctx.State.Set("is_open", false);
        }
    }

    public virtual bool Open(IMudContext ctx)
    {
        if (IsOpen)
            return false;

        ctx.State.Set("is_open", true);
        return true;
    }

    public virtual bool Close(IMudContext ctx)
    {
        if (!IsOpen)
            return false;

        ctx.State.Set("is_open", false);
        return true;
    }

    /// <summary>
    /// Set the container's maximum capacity.
    /// </summary>
    public void SetMaxCapacity(int capacity, IMudContext ctx)
    {
        ctx.State.Set("max_capacity", capacity);
    }
}
