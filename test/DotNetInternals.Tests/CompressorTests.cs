namespace DotNetInternals.Tests;

public class CompressorTests
{
    [Fact]
    public void RazorInitialCode()
    {
        var source = """
            <TestComponent Param="1" />
            
            @code {
                [Parameter] public int Param { get; set; }
            }
            """.NormalizeLineEndings();
        var compressed = Compressor.Compress(source);
        Assert.Equal((89, 104), (source.Length, compressed.Length));
        var uncompressed = Compressor.Uncompress(compressed);
        Assert.Equal(source, uncompressed);
    }
}
