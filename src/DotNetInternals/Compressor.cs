using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DotNetInternals;

internal static class Compressor
{
    private static readonly char[] s_padding = ['='];

    public static string Compress(string text)
    {
        using var ms = new MemoryStream();
        using (var compressor = new DeflateStream(ms, CompressionLevel.Optimal))
        {
            var inputBytes = Encoding.Unicode.GetBytes(text);
            compressor.Write(inputBytes);
        }
        return ToBase64(ms.ToArray());
    }

    private static string ToBase64(byte[] input)
        => Convert.ToBase64String(input).TrimEnd(s_padding).Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64(string input)
        => Convert.FromBase64String(input.Replace('_', '/').Replace('-', '+') +
            (input.Length % 4) switch
            {
                0 => "",
                2 => "==",
                3 => "=",
                _ => throw new ArgumentException()
            });

    public static string Uncompress(string slug)
    {
        try
        {
            var bytes = FromBase64(slug);

            using var ms = new MemoryStream(bytes);
            using (var compressor = new DeflateStream(ms, CompressionMode.Decompress))
            using (var sr = new StreamReader(compressor, Encoding.Unicode))
            {
                return sr.ReadToEnd();
            }
        }
        catch (Exception ex)
        {
            return ex.ToString();
        }
    }
}