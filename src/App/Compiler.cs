using ProtoBuf;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace DotNetInternals;

public interface ICompiler
{
    CompiledAssembly Compile(IEnumerable<InputCode> inputs);
}

internal sealed class CompilerProxy : ICompiler
{
    public CompiledAssembly Compile(IEnumerable<InputCode> inputs)
    {
        Assembly compilerAssembly = Assembly.Load("DotNetInternals.Compiler");
        Type compilerType = compilerAssembly.GetType("DotNetInternals.Compiler")!;
        ICompiler compiler = (ICompiler)Activator.CreateInstance(compilerType)!;
        return compiler.Compile(inputs);
    }
}

internal sealed record InitialCode(string SuggestedFileName, string TextTemplate)
{
    public static readonly InitialCode Razor = new("TestComponent.razor", """
        <TestComponent Param="1" />

        @code {
            [Parameter] public int Param { get; set; }
        }

        """);

    public static readonly InitialCode CSharp = new("Class.cs", """
        class Class
        {
            public void M()
            {
            }
        }

        """);

    public static readonly InitialCode Cshtml = new("TestPage.cshtml", """
        @page
        @using System.ComponentModel.DataAnnotations
        @model PageModel
        @addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers

        <form method="post">
            Name:
            <input asp-for="Customer.Name" />
            <input type="submit" />
        </form>

        @functions {
            public class PageModel
            {
                public Customer Customer { get; set; }
            }

            public class Customer
            {
                public int Id { get; set; }

                [Required, StringLength(10)]
                public string Name { get; set; }
            }
        }

        """);

    public string SuggestedFileNameWithoutExtension => Path.GetFileNameWithoutExtension(SuggestedFileName);
    public string SuggestedFileExtension => Path.GetExtension(SuggestedFileName);

    public string GetFinalFileName(string suffix)
    {
        return string.IsNullOrEmpty(suffix)
            ? SuggestedFileName
            : SuggestedFileNameWithoutExtension + suffix + SuggestedFileExtension;
    }

    public InputCode ToInputCode(string? finalFileName = null)
    {
        finalFileName ??= SuggestedFileName;

        return new()
        {
            FileName = finalFileName,
            Text = finalFileName == SuggestedFileName
                ? TextTemplate
                : TextTemplate.Replace(
                    SuggestedFileNameWithoutExtension,
                    Path.GetFileNameWithoutExtension(finalFileName),
                    StringComparison.Ordinal),
        };
    }
}

[ProtoContract]
public sealed record InputCode
{
    [ProtoMember(1)]
    public required string FileName { get; init; }
    [ProtoMember(2)]
    public required string Text { get; init; }

    public string FileExtension => Path.GetExtension(FileName);
}

public enum DiagnosticDataSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record DiagnosticData(
    string? FilePath,
    DiagnosticDataSeverity Severity,
    string Id,
    string HelpLinkUri,
    string Message,
    int StartLineNumber,
    int StartColumn,
    int EndLineNumber,
    int EndColumn
);

public sealed record CompiledAssembly(
    ImmutableDictionary<string, CompiledFile> Files,
    ImmutableArray<CompiledFileOutput> GlobalOutputs,
    int NumWarnings,
    int NumErrors,
    ImmutableArray<DiagnosticData> Diagnostics,
    string BaseDirectory)
{
    public static readonly string DiagnosticsOutputType = "Error List";

    public CompiledFileOutput? GetGlobalOutput(string type)
    {
        return GlobalOutputs.FirstOrDefault(o => o.Type == type);
    }
}

public sealed record CompiledFile(ImmutableArray<CompiledFileOutput> Outputs)
{
    public CompiledFileOutput? GetOutput(string type)
    {
        return Outputs.FirstOrDefault(o => o.Type == type);
    }
}

public sealed class CompiledFileOutput
{
    private object text;

    public CompiledFileOutput(string type, string eagerText)
    {
        Type = type;
        text = eagerText;
    }

    public CompiledFileOutput(string type, Func<ValueTask<string>> lazyText)
    {
        Type = type;
        text = lazyText;
    }

    public CompiledFileOutput(string type, Func<string> lazyTextSync)
    {
        Type = type;
        text = lazyTextSync;
    }

    public string Type { get; }
    public int Priority { get; init; }

    public bool IsLazy => !TryGetEagerText(out _);

    public bool TryGetEagerText([NotNullWhen(returnValue: true)] out string? result)
    {
        if (text is string eagerText)
        {
            result = eagerText;
            return true;
        }

        if (text is ValueTask<string> { IsCompletedSuccessfully: true, Result: var taskResult })
        {
            text = taskResult;
            result = taskResult;
            return true;
        }

        result = null;
        return false;
    }

    public ValueTask<string> GetTextAsync()
    {
        Debug.Assert(Thread.CurrentThread is { IsThreadPoolThread: false, IsBackground: false },
            "Expected this to run on the UI thread only (we don't perform any synchronization " +
            "when invoking the lazy text function)");

        if (TryGetEagerText(out var eagerText))
        {
            return new(eagerText);
        }

        if (text is ValueTask<string> existingTask)
        {
            return existingTask;
        }

        if (text is Func<ValueTask<string>> lazyText)
        {
            var task = lazyText();
            text = task;
            return task;
        }

        if (text is Func<string> lazyTextSync)
        {
            var result = lazyTextSync();
            text = result;
            return new(result);
        }

        throw new InvalidOperationException();
    }
}
