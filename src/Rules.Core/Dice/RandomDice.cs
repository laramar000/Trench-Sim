using System.Security.Cryptography;

namespace Rules.Core.Dice;

// Uniform D6 from the operating system's cryptographic generator, so neither
// player can predict or reproduce the server's rolls.
public sealed class RandomDice : IDiceSource
{
    public int RollD6() => RandomNumberGenerator.GetInt32(1, 7);
}
