namespace JitRealm.Mud;

/// <summary>
/// Interface for coin items that support stacking and automatic merging.
/// When coins of the same material are placed in the same container, they merge.
/// </summary>
public interface ICoin : ICarryable
{
    /// <summary>
    /// The material of this coin (Gold, Silver, or Copper).
    /// Determines the coin's value and how it merges with other coins.
    /// </summary>
    CoinMaterial Material { get; }

    /// <summary>
    /// The number of coins in this pile.
    /// </summary>
    int Amount { get; }
}

/// <summary>
/// Coin materials with their exchange rates (value represents copper equivalence).
/// Exchange rates: 1 GC = 100 SC, 1 SC = 100 CC
/// </summary>
public enum CoinMaterial
{
    /// <summary>Copper coin - base currency unit (1 CC = 1 copper)</summary>
    Copper = 1,

    /// <summary>Silver coin - 1 SC = 100 CC</summary>
    Silver = 100,

    /// <summary>Gold coin - 1 GC = 100 SC = 10,000 CC</summary>
    Gold = 10000
}
