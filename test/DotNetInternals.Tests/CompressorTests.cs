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
        var savedState = new SavedState() { Inputs = [new() { FileName = "", Text = source }] };
        var compressed = Compressor.Compress(savedState);
        Assert.Equal((89, 112), (source.Length, compressed.Length));
        var uncompressed = Compressor.Uncompress(compressed);
        Assert.Equal(savedState.Inputs.Single(), uncompressed.Inputs.Single());
    }
}
