namespace JitRealm.Mud.Commands.Inventory;

/// <summary>
/// Examine an item in detail.
/// </summary>
public class ExamineCommand : CommandBase
{
    public override string Name => "examine";
    public override IReadOnlyList<string> Aliases => new[] { "exam", "x" };
    public override string Usage => "examine <item>";
    public override string Description => "Examine an item closely";
    public override string Category => "Inventory";

    public override Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (!RequireArgs(context, args, 1)) return Task.CompletedTask;

        var itemName = JoinArgs(args);
        var roomId = context.GetPlayerLocation();
        if (roomId is null)
        {
            context.Output("You're not in a room.");
            return Task.CompletedTask;
        }

        // Find item in room or inventory
        var ctx = context.CreateContext(context.PlayerId);
        var itemId = ctx.FindItem(itemName, context.PlayerId);  // Check inventory first
        if (itemId is null)
        {
            itemId = ctx.FindItem(itemName, roomId);  // Then check room
        }

        if (itemId is null)
        {
            context.Output($"You don't see '{itemName}' here.");
            return Task.CompletedTask;
        }

        var item = context.State.Objects!.Get<IItem>(itemId);
        if (item is not null)
        {
            context.Output(item.LongDescription);
            context.Output($"  Weight: {item.Weight} lbs");
            context.Output($"  Value: {item.Value} coins");
        }
        else
        {
            var obj = context.State.Objects.Get<IMudObject>(itemId);
            if (obj is not null)
            {
                context.Output($"{obj.Name}");
            }
            else
            {
                context.Output("You examine it closely but see nothing special.");
            }
        }

        return Task.CompletedTask;
    }
}
