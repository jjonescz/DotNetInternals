using Microsoft.AspNetCore.Components;

namespace DotNetInternals;

public abstract class CustomComponentBase : ComponentBase
{
    protected async Task RefreshAsync()
    {
        _ = InvokeAsync(StateHasChanged);
        await Task.Yield();
    }
}
