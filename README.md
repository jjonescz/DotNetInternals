# DotNetInternals

C# and Razor compiler playground in the browser via Blazor WebAssembly.

- Razor/CSHTML to generated C# code / IR / Syntax Tree / Errors.
- C# to IL / Syntax / decompiled-C# / Errors / Execution console output.
- More in the future?
- Offline support (PWA).
- VSCode Monaco Editor.

## Related work

Razor REPLs (all can only render HTML):
- https://blazorrepl.telerik.com/
- https://netcorerepl.telerik.com/
- https://try.mudblazor.com/snippet
- https://blazorfiddle.com/

## Development

- `src/App`: the WebAssembly app.
  - `cd src/App; dotnet watch`
- `src/Compiler`: self-contained project referencing Roslyn/Razor.
  It's reloaded at runtime with a user-chosen version of Roslyn/Razor.
  It should be small (for best reloading perf). It can reference shared code
  which does not depend on Roslyn/Razor from elsewhere (e.g., `App`).
- `src/RazorAccess`: `internal` access to Razor DLLs (via fake assembly name).
- `src/RoslynAccess`: `internal` access to Roslyn DLLs (via fake assembly name).
- `test/UnitTests`
  - `dotnet test`
