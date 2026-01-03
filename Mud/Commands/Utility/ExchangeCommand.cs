namespace JitRealm.Mud.Commands.Utility;

/// <summary>
/// Exchange coins between denominations.
/// Example: "exchange 1 gold to silver" → 1 GC becomes 100 SC
/// </summary>
public class ExchangeCommand : CommandBase
{
    public override string Name => "exchange";
    public override IReadOnlyList<string> Aliases => new[] { "convert" };
    public override string Usage => "exchange <amount> <material> to <material>";
    public override string Description => "Exchange coins between denominations";
    public override string Category => "Items";

    public override async Task ExecuteAsync(CommandContext context, string[] args)
    {
        // Parse: "1 gold to silver" or "100 sc to gc"
        if (args.Length < 4)
        {
            context.Output("Usage: exchange <amount> <material> to <material>");
            context.Output("Example: exchange 1 gold to silver");
            context.Output("Exchange rates: 1 GC = 100 SC = 10,000 CC");
            return;
        }

        // Parse amount
        if (!int.TryParse(args[0], out var amount) || amount <= 0)
        {
            context.Output("Please specify a valid amount.");
            return;
        }

        // Parse source material
        var fromMaterial = CoinHelper.ParseMaterial(args[1]);
        if (!fromMaterial.HasValue)
        {
            context.Output($"Unknown coin type: {args[1]}");
            return;
        }

        // Expect "to"
        if (!args[2].Equals("to", StringComparison.OrdinalIgnoreCase))
        {
            context.Output("Usage: exchange <amount> <material> to <material>");
            return;
        }

        // Parse destination material
        var toMaterial = CoinHelper.ParseMaterial(args[3]);
        if (!toMaterial.HasValue)
        {
            context.Output($"Unknown coin type: {args[3]}");
            return;
        }

        if (fromMaterial == toMaterial)
        {
            context.Output("You can't exchange coins to the same type.");
            return;
        }

        var playerId = context.PlayerId;
        if (playerId is null)
        {
            context.Output("No player.");
            return;
        }

        // Calculate exchange
        var fromValue = amount * (int)fromMaterial.Value;
        var toValue = (int)toMaterial.Value;

        // Check if exchange is possible (must be whole coins)
        if (fromValue < toValue)
        {
            var needed = (toValue + (int)fromMaterial.Value - 1) / (int)fromMaterial.Value;
            context.Output($"You need at least {needed} {fromMaterial.Value.ToString().ToLower()} coins to exchange for 1 {toMaterial.Value.ToString().ToLower()} coin.");
            return;
        }

        var resultAmount = fromValue / toValue;
        var remainder = fromValue % toValue;

        // Check player has enough coins
        var coinId = CoinHelper.FindCoinPile(context.State, playerId, fromMaterial.Value);
        if (coinId is null)
        {
            context.Output($"You don't have any {fromMaterial.Value.ToString().ToLower()} coins.");
            return;
        }

        var coin = context.State.Objects!.Get<ICoin>(coinId);
        if (coin is null || coin.Amount < amount)
        {
            context.Output($"You only have {coin?.Amount ?? 0} {fromMaterial.Value.ToString().ToLower()} coins.");
            return;
        }

        // Perform exchange: deduct from source, add to destination
        var coinState = context.State.Objects.GetStateStore(coinId);
        var currentAmount = coinState?.Get<int>("amount") ?? 0;

        if (currentAmount == amount)
        {
            // Remove entire pile
            context.State.Containers.Remove(coinId);
            await context.State.Objects.DestructAsync(coinId, context.State);
        }
        else
        {
            // Reduce pile
            coinState?.Set("amount", currentAmount - amount);
        }

        // Add destination coins
        await CoinHelper.AddCoinsAsync(context.State, playerId, resultAmount, toMaterial.Value);

        // Handle remainder (return as source material)
        if (remainder > 0)
        {
            var remainderAmount = remainder / (int)fromMaterial.Value;
            if (remainderAmount > 0)
            {
                await CoinHelper.AddCoinsAsync(context.State, playerId, remainderAmount, fromMaterial.Value);
            }
        }

        // Report result
        var fromDesc = CoinHelper.FormatCoins(amount, fromMaterial.Value);
        var toDesc = CoinHelper.FormatCoins(resultAmount, toMaterial.Value);
        context.Output($"You exchange {fromDesc} for {toDesc}.");
    }
}
