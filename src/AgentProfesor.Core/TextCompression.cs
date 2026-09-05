using System.IO.Compression;
using System.Text;

namespace AgentProfesor.Core;

/// <summary>
/// Brotli quality is a 0-11 scale, so the config's 0-10 "CompressionLevel" maps onto it almost
/// directly (clamped, since config files can carry stray values).
/// </summary>
public static class TextCompression
{
    public static byte[] Compress(string text, int configuredLevel)
    {
        var quality = Math.Clamp(configuredLevel, 0, 11);
        var input = Encoding.UTF8.GetBytes(text);
        var maxLength = BrotliEncoder.GetMaxCompressedLength(input.Length);
        var buffer = new byte[maxLength];
        if (!BrotliEncoder.TryCompress(input, buffer, out var written, quality, window: 22))
            throw new InvalidOperationException("Brotli compression selhala.");

        var result = new byte[written];
        Array.Copy(buffer, result, written);
        return result;
    }

    public static string Decompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(brotli, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
