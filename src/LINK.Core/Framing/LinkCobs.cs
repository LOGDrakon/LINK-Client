namespace Link.Core.Framing;

/// <summary>Consistent Overhead Byte Stuffing : supprime les 0x00 d'un bloc (délimiteur de trame v2).</summary>
public static class LinkCobs
{
    public static byte[] Encode(ReadOnlySpan<byte> data)
    {
        var output = new byte[data.Length + data.Length / 254 + 2];
        int codePos = 0, o = 1;
        byte code = 1;

        foreach (var b in data)
        {
            if (b == 0)
            {
                output[codePos] = code;
                codePos = o++;
                code = 1;
            }
            else
            {
                output[o++] = b;
                code++;
                if (code == 0xFF)
                {
                    output[codePos] = code;
                    codePos = o++;
                    code = 1;
                }
            }
        }

        output[codePos] = code;
        return output.AsSpan(0, o).ToArray();
    }

    /// <summary>Retourne null si le bloc n'est pas un COBS valide.</summary>
    public static byte[]? Decode(ReadOnlySpan<byte> data)
    {
        var output = new byte[data.Length];
        int i = 0, o = 0;

        while (i < data.Length)
        {
            byte code = data[i++];
            if (code == 0)
                return null;

            for (int k = 1; k < code; k++)
            {
                if (i >= data.Length || data[i] == 0)
                    return null;
                output[o++] = data[i++];
            }

            if (code != 0xFF && i < data.Length)
                output[o++] = 0;
        }

        return output.AsSpan(0, o).ToArray();
    }
}
