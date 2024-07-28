using System.Buffers.Text;
using System.IO.Compression;
using System.Text;

namespace DotNetInternals;

internal static class Compressor
{
    private static readonly Encoding s_encoding = Encoding.UTF8;

    public static string Compress(string text)
    {
        using var ms = new MemoryStream();
        using (var compressor = new DeflateStream(ms, CompressionLevel.Optimal))
        {
            var inputBytes = s_encoding.GetBytes(text);
            compressor.Write(inputBytes);
        }
        return Base64Url.EncodeToString(ms.ToArray());
    }

    public static string Uncompress(string slug)
    {
        try
        {
            var bytes = Base64Url.DecodeFromChars(slug);
            using var ms = new MemoryStream(bytes);
            using var compressor = new DeflateStream(ms, CompressionMode.Decompress);
            using var sr = new StreamReader(compressor, s_encoding);
            return sr.ReadToEnd();
        }
        catch (Exception ex)
        {
            return ex.ToString();
        }
    }
}