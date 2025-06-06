using System;
using System.Globalization;

namespace HarpoS7.PoC;

public static class Helpers
{
    public static void ParseAndReverseBytes(string fingerprint, Span<byte> destination)
    {
        if (!fingerprint.StartsWith("03:") && !fingerprint.StartsWith("00:") && !fingerprint.StartsWith("01:"))
        {
            throw new Exception("Invalid fingerprint");
        }

        fingerprint = fingerprint[3..];

        // I didn't see this happen, but let's better be safe than sorry
        if (fingerprint.Length % 2 != 0)
        {
            fingerprint = '0' + fingerprint;
        }

        if (destination.Length < fingerprint.Length / 2)
        {
            throw new ArgumentException($"Destination too small (need at least: {fingerprint.Length / 2}, got: {destination.Length})",
                nameof(destination));
        }

        var j = 0;
        for (var i = fingerprint.Length - 1; i >= 0; i -= 2)
        {
            var b = byte.Parse($"{fingerprint[i - 1]}{fingerprint[i]}", NumberStyles.HexNumber);
            destination[j++] = b;
        } 
    }
}