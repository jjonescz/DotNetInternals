using BlazorMonaco.Editor;
using ProtoBuf;

namespace DotNetInternals.Lab;

partial class Page
{
    private SavedState savedState = SavedState.Initial;

    private string GetCurrentSlug()
    {
        var uri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);
        return uri.Fragment.TrimStart('#');
    }

    private async Task LoadStateFromUrlAsync()
    {
        var slug = GetCurrentSlug();

        savedState = string.IsNullOrWhiteSpace(slug)
            ? SavedState.Initial
            : Compressor.Uncompress(slug);

        // Load inputs.
        inputs.Clear();
        currentInputIndex = 0;
        foreach (var (index, input) in savedState.Inputs.Index())
        {
            var model = await CreateModelAsync(input);
            inputs.Add(new(input.FileName, model));

            if (index == 0)
            {
                await inputEditor.SetModel(model);
            }
        }

        // Load settings.
        await settings.LoadFromStateAsync(savedState);
    }

    internal async Task<SavedState> SaveStateToUrlAsync(Func<SavedState, SavedState>? updater = null)
    {
        // Always save the current editor texts.
        savedState = savedState with { Inputs = await getInputsAsync() };

        if (updater != null)
        {
            savedState = updater(savedState);
        }

        var newSlug = Compressor.Compress(savedState);
        if (newSlug != GetCurrentSlug())
        {
            NavigationManager.NavigateTo(NavigationManager.BaseUri + "#" + newSlug, forceLoad: false);
        }

        return savedState;

        async Task<ImmutableArray<InputCode>> getInputsAsync()
        {
            var builder = ImmutableArray.CreateBuilder<InputCode>(inputs.Count);
            foreach (var (fileName, model) in inputs)
            {
                var text = await model.GetValue(EndOfLinePreference.TextDefined, preserveBOM: true);
                builder.Add(new() { FileName = fileName, Text = text });
            }
            return builder.ToImmutable();
        }
    }
}

[ProtoContract]
internal sealed record SavedState
{
    public static SavedState Initial { get; } = new() { Inputs = [InitialCode.Razor.ToInputCode()] };

    [ProtoMember(1)]
    public ImmutableArray<InputCode> Inputs { get; init; }

    [ProtoMember(2)]
    public string? RoslynVersion { get; init; }

    [ProtoMember(3)]
    public string? RazorVersion { get; init; }
}
