# DotNetInternals

C# and Razor compiler playground in the browser via Blazor WebAssembly.

- Razor/CSHTML to generated C# code / IR / Syntax Tree / Errors.
- C# to IL / Syntax / decompiled-C# / Errors / Execution console output.
- Any Roslyn/Razor compiler version (NuGet daily builds).
- Offline support (PWA).
- VSCode Monaco Editor.
- Multiple input sources (especially useful for interlinked Razor components).

## Related work

Razor REPLs (all can only render HTML):
- https://blazorrepl.telerik.com/
- https://netcorerepl.telerik.com/
- https://try.mudblazor.com/snippet
- https://blazorfiddle.com/

## Development

- `src/App`: the WebAssembly app.
  - `cd src/App; dotnet watch` - `src/Server` is better for development though.
- `src/Compiler`: self-contained project referencing Roslyn/Razor.
  It's reloaded at runtime with a user-chosen version of Roslyn/Razor.
  It should be small (for best reloading perf). It can reference shared code
  which does not depend on Roslyn/Razor from elsewhere (e.g., `Shared.csproj`).
- `src/RazorAccess`: `internal` access to Razor DLLs (via fake assembly name).
- `src/RoslynAccess`: `internal` access to Roslyn DLLs (via fake assembly name).
- `src/Server`: a Blazor Server entrypoint for easier development of the App
  (it has better tooling support for hot reload and debugging).
  - `cd src/Server; dotnet watch`
- `src/Shared`: code used by `Compiler` that does not depend on Roslyn/Razor.
- `test/UnitTests`
  - `dotnet test`

## Attribution

- Icon: [OpenMoji](https://openmoji.org/library/emoji-1FAD9-200D-1F7EA/)
