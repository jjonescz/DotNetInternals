using MetadataReferenceService.BlazorWasm;
using Microsoft.NET.WebAssembly.Webcil;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace DotNetLab;

internal static class WebcilUtil
{
    private static readonly Func<Stream, bool, byte[]> convertFromWebcil = typeof(BlazorWasmMetadataReferenceService).Assembly
        .GetType("MetadataReferenceService.BlazorWasm.WasmWebcil.WebcilConverterUtil")!
        .GetMethod("ConvertFromWebcil", BindingFlags.Public | BindingFlags.Static)!
        .CreateDelegate<Func<Stream, bool, byte[]>>();

    public static ImmutableArray<byte> WebcilToDll(ImmutableArray<byte> bytes)
    {
        var inputStream = new MemoryStream(ImmutableCollectionsMarshal.AsArray(bytes)!);
        return ImmutableCollectionsMarshal.AsImmutableArray(convertFromWebcil(inputStream, /* wrappedInWebAssembly */ true));
    }

    public static Stream DllToWebcil(FileStream inputStream)
    {
        var converter = WebcilConverter.FromPortableExecutable("", "");

        using var reader = new PEReader(inputStream);
        converter.GatherInfo(reader, out var wcInfo, out var peInfo);

        var tempStream = new MemoryStream();
        converter.WriteConversionTo(tempStream, inputStream, peInfo, wcInfo);
        tempStream.Seek(0, SeekOrigin.Begin);

        var wrapper = new WebcilWasmWrapper(tempStream);
        var outputStream = new MemoryStream();
        wrapper.WriteWasmWrappedWebcil(outputStream);
        outputStream.Seek(0, SeekOrigin.Begin);

        return outputStream;
    }
}
