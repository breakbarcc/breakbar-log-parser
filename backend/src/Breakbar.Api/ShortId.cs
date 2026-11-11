using System.Security.Cryptography;

namespace Breakbar.Api;

/// <summary>Unguessable 10-character base62 ids for report URLs (~59 bits of entropy).</summary>
public static class ShortId
{
    public const int Length = 10;
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string New() => new(RandomNumberGenerator.GetItems<char>(Alphabet, Length));

    public static bool IsValid(string? value)
        => value is { Length: Length } && value.All(char.IsAsciiLetterOrDigit);
}
