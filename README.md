# DotNetInternals

C# and Razor compiler playground in the browser via Blazor WebAssembly.

| [C#](https://lab.razor.fyi/#4yrn4gooyk8vSszVSy4WyiwtzsxLVwiuLC5JzbXm4krOSSwuVoAq4KrmUgCC4pLEksxkhbL8zBQF38TMPA1NsDBEEgSc8_OK83NS9cKLMktSfTLzUjWUPFJzcvL1lDStwYpquWq5AA) | [Razor](https://lab.razor.fyi/#48rlEg5JLS5xzs8tyM9LzSvRK0qsyi8SCrNBEVUISCxKzLVVMlRS0Lfj4nJIzk9JVajmUgCCaLBUaklqUaxCQWlSTmayQiZMg0K1QnpqibVCMYio5arlAgA) |
|:-:|:-:|
| ![C# screenshot](docs/screenshots/csharp.png) | ![Razor screenshot](docs/screenshots/razor.png) |

## Features

- Razor/CSHTML to generated C# code / IR / Syntax Tree / Errors.
- C# to IL / Syntax / decompiled-C# / Errors / Execution console output.
- Any Roslyn/Razor compiler version (NuGet daily builds).
- Offline support (PWA).
- VSCode Monaco Editor.
- Multiple input sources (especially useful for interlinked Razor components).
- C# Language Services (completions, live diagnostics) - experimental.

## Development

The recommended startup app for development is `src/Server`.

To hit breakpoints, it is recommended to turn off the worker (in app settings).

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
- `src/Worker`: an app loaded in a web worker (a separate process in the browser),
  so it does all the CPU-intensive work to avoid lagging the user interface.
- `test/UnitTests`
  - `dotnet test`

## Attribution

- Logo: [OpenMoji](https://openmoji.org/library/emoji-1FAD9-200D-1F7EA/)
- Style: [Bootstrap](https://getbootstrap.com/)
- Icons: [Bootstrap Icons](https://icons.getbootstrap.com/)

## Related work

Razor REPLs:
- https://blazorrepl.telerik.com/
- https://netcorerepl.telerik.com/
- https://try.mudblazor.com/snippet
- https://blazorfiddle.com/

C# REPLs:
- https://dotnetfiddle.net/
- https://onecompiler.com/csharp

C# compiler playgrounds:
- https://sharplab.io/
- https://godbolt.org/

XAML REPLs:
- https://playground.platform.uno/
