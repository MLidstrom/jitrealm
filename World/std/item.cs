namespace JitRealm.World.Std;

using JitRealm.Mud;

/// <summary>
/// Base class for all carryable items.
/// Extend this class to create items that can be picked up, dropped, and traded.
/// </summary>
public class ItemBase : MudObjectBase, ICarryable, IOnLoad
{
    protected IMudContext? Ctx { get; private set; }

    public override string Name => Ctx?.State.Get<string>("name") ?? "item";

    public virtual int Weight => Ctx?.State.Get<int>("weight") ?? 1;
    public virtual int Value => Ctx?.State.Get<int>("value") ?? 0;

    public virtual string ShortDescription => Ctx?.State.Get<string>("short_desc") ?? Name;
    public virtual string LongDescription => Ctx?.State.Get<string>("long_desc") ?? "A nondescript item.";

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
            ctx.State.Set("short_desc", "an item");
        }
        if (!ctx.State.Has("long_desc"))
        {
            ctx.State.Set("long_desc", "A nondescript item.");
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
    /// Set the item's long description.
    /// </summary>
    public void SetLongDescription(string desc, IMudContext ctx)
    {
        ctx.State.Set("long_desc", desc);
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
