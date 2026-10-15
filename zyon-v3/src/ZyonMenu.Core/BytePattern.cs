// C# / src/ZyonMenu.Core/BytePattern.cs / .NET 8 / immutable byte signature parser and unique-match scanner.
using System.Globalization;

namespace ZyonMenu.Core;

public sealed class BytePattern
{
    private readonly byte[] _bytes;
    private readonly bool[] _wildcards;

    private BytePattern(byte[] bytes, bool[] wildcards)
    {
        _bytes = bytes;
        _wildcards = wildcards;
    }

    public int Length => _bytes.Length;

    /// <summary>Parses whitespace-separated hexadecimal byte pairs and whole-byte ?? wildcards.</summary>
    public static BytePattern Parse(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var tokens = pattern.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new FormatException("A assinatura não pode estar vazia.");
        var bytes = new byte[tokens.Length];
        var wildcards = new bool[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] == "??")
            {
                wildcards[i] = true;
                continue;
            }
            if (tokens[i].Length != 2 || !byte.TryParse(tokens[i], NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out bytes[i]))
                throw new FormatException($"Byte inválido na posição {i + 1}; use dois dígitos hexadecimais ou ??.");
        }
        return new BytePattern(bytes, wildcards);
    }

    /// <summary>Returns the only matching offset; missing or ambiguous signatures never resolve to a guessed address.</summary>
    public int FindUnique(ReadOnlySpan<byte> data)
    {
        var found = -1;
        for (var offset = 0; offset <= data.Length - Length; offset++)
        {
            if (!MatchesAt(data, offset)) continue;
            if (found >= 0) throw new InvalidOperationException("A assinatura corresponde a mais de um endereço.");
            found = offset;
        }
        if (found < 0) throw new InvalidOperationException("A assinatura não foi encontrada.");
        return found;
    }

    /// <summary>Returns all matching offsets, including overlapping matches, in ascending order.</summary>
    public int[] FindAll(ReadOnlySpan<byte> data)
    {
        var matches = new List<int>();
        for (var offset = 0; offset <= data.Length - Length; offset++)
            if (MatchesAt(data, offset)) matches.Add(offset);
        return matches.ToArray();
    }

    private bool MatchesAt(ReadOnlySpan<byte> data, int offset)
    {
        for (var index = 0; index < Length; index++)
            if (!_wildcards[index] && _bytes[index] != data[offset + index]) return false;
        return true;
    }
}
