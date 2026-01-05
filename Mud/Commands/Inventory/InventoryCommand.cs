namespace JitRealm.Mud.Commands.Inventory;

/// <summary>
/// Show items in player's inventory.
/// </summary>
public class InventoryCommand : CommandBase
{
    public override string Name => "inventory";
    public override IReadOnlyList<string> Aliases => new[] { "inv", "i" };
    public override string Usage => "inventory";
    public override string Description => "Show your inventory";
    public override string Category => "Inventory";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        var contents = context.State.Containers.GetContents(context.PlayerId);
        if (contents.Count == 0)
        {
            context.Output("You are not carrying anything.");
            return Task.CompletedTask;
        }

        context.Output("You are carrying:");
        int totalWeight = 0;
        foreach (var itemId in contents)
        {
            var item = context.State.Objects!.Get<IItem>(itemId);
            if (item is not null)
            {
                context.Output($"  {item.ShortDescription} ({item.Weight} lbs)");
                totalWeight += item.Weight;
            }
            else
            {
                var obj = context.State.Objects.Get<IMudObject>(itemId);
                context.Output($"  {obj?.Name ?? itemId}");
            }
        }

        var player = context.GetPlayer();
        if (player is not null)
        {
            context.Output($"Total weight: {totalWeight}/{player.CarryCapacity} lbs");
        }

        return Task.CompletedTask;
    }
}
