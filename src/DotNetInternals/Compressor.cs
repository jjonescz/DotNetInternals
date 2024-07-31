using System.Buffers.Text;
using System.IO.Compression;
using DotNetInternals.RazorAccess;
using ProtoBuf;

namespace DotNetInternals;

internal static class Compressor
{
    public static string Compress(SavedState input)
    {
        using var ms = new MemoryStream();
        using (var compressor = new DeflateStream(ms, CompressionLevel.Optimal))
        {
            Serializer.Serialize(compressor, input);
        }
        return Base64Url.EncodeToString(ms.ToArray());
    }

    public static SavedState Uncompress(string slug)
    {
        try
        {
            var bytes = Base64Url.DecodeFromChars(slug);
            using var ms = new MemoryStream(bytes);
            using var compressor = new DeflateStream(ms, CompressionMode.Decompress);
            return Serializer.Deserialize<SavedState>(compressor);
        }
        catch (Exception ex)
        {
            return new SavedState
            {
                Inputs =
                [
                    new InputCode { FileName = "(error)", Text = ex.ToString() },
                ],
            };
        }
    }
}
