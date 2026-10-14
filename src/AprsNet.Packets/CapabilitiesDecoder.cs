using System.Text;

namespace AprsNet.Packets;

/// <summary>Decodes station capabilities (APRS101 ch. 15). A list of tokens cannot be malformed, so decoding never fails.</summary>
internal static class CapabilitiesDecoder
{
    public static CapabilitiesReport Decode(ReadOnlyMemory<byte> information)
    {
        var capabilities = new List<KeyValuePair<string, string?>>();
        foreach (var entry in Encoding.Latin1.GetString(information.Span[1..]).Split(','))
        {
            var equals = entry.IndexOf('=', StringComparison.Ordinal);
            var token = (equals < 0 ? entry : entry[..equals]).Trim();
            if (token.Length == 0)
            {
                continue; // an empty entry (",,", or a trailing comma) carries nothing
            }

            capabilities.Add(new(token, equals < 0 ? null : entry[(equals + 1)..].Trim()));
        }

        return new CapabilitiesReport { Capabilities = capabilities };
    }
}
