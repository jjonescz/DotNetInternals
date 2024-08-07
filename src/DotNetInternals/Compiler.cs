using DotNetInternals.RazorAccess;
using DotNetInternals.RoslynAccess;
using Microsoft.AspNetCore.Mvc.Razor.Extensions;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.CodeAnalysis.Test.Utilities;
using ProtoBuf;
using Roslyn.Test.Utilities;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DotNetInternals;

public static class Compiler
{

    public static readonly InitialCode InitialRazorCode = new("TestComponent.razor", """
        <TestComponent Param="1" />

        @code {
            [Parameter] public int Param { get; set; }
        }

        """);

    public static readonly InitialCode InitialCSharpCode = new("Class.cs", """
        class Class
        {
            public void M()
            {
            }
        }

        """);

    public static readonly InitialCode InitialCshtmlCode = new("TestPage.cshtml", """
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

    public static async Task<CompiledAssembly> CompileAsync(IEnumerable<InputCode> inputs)
    {
        var directory = "/TestProject/";
        var fileSystem = new VirtualRazorProjectFileSystemProxy();
        var cSharp = new Dictionary<string, CSharpSyntaxTree>();
        foreach (var input in inputs)
        {
            var filePath = directory + input.FileName;
            switch (input.FileExtension)
            {
                case ".razor":
                case ".cshtml":
                    {
                        var item = RazorAccessors.CreateSourceGeneratorProjectItem(
                            basePath: "/",
                            filePath: filePath,
                            relativePhysicalPath: input.FileName,
                            fileKind: null!, // will be automatically determined from file path
                            additionalText: new TestAdditionalText(input.Text, encoding: Encoding.UTF8, path: filePath),
                            cssScope: null);
                        fileSystem.Add(item);
                        break;
                    }
                case ".cs":
                    {
                        cSharp[input.FileName] = (CSharpSyntaxTree)CSharpSyntaxTree.ParseText(input.Text, path: filePath);
                        break;
                    }
            }
        }

        var config = RazorConfiguration.Default;

        // Phase 1: Declaration only (to be used as a reference from which tag helpers will be discovered).
        RazorProjectEngine declarationProjectEngine = createProjectEngine([]);
        var declarationCompilation = CSharpCompilation.Create("TestAssembly",
            syntaxTrees: [
                ..fileSystem.Inner.EnumerateItems("/").Select((item) =>
                {
                    RazorCodeDocument declarationCodeDocument = declarationProjectEngine.ProcessDeclarationOnly(item);
                    string declarationCSharp = declarationCodeDocument.GetCSharpDocument().GeneratedCode;
                    return CSharpSyntaxTree.ParseText(declarationCSharp);
                }),
                ..cSharp.Values,
            ],
            Basic.Reference.Assemblies.AspNet80.References.All,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Phase 2: Full generation.
        RazorProjectEngine projectEngine = createProjectEngine([
            ..Basic.Reference.Assemblies.AspNet80.References.All,
            declarationCompilation.ToMetadataReference()]);
        var compiledRazorFiles = fileSystem.Inner.EnumerateItems("/")
            .ToImmutableDictionary(
                keySelector: (item) => item.RelativePhysicalPath,
                elementSelector: (item) =>
                {
                    RazorCodeDocument codeDocument = projectEngine.Process(item);

                    string syntax = codeDocument.GetSyntaxTree().Serialize();

                    string ir = codeDocument.GetDocumentIntermediateNode().Serialize();

                    string cSharp = codeDocument.GetCSharpDocument().GeneratedCode;

                    return new CompiledFile([
                        new("Syntax", syntax),
                        new("IR", ir),
                        new("C#", cSharp) { Priority = 1 },
                    ]);
                });

        var finalCompilation = CSharpCompilation.Create("TestAssembly",
            [
                ..compiledRazorFiles.Values.Select(static (file) =>
                {
                    var success = file.GetOutput("C#")!.TryGetEagerText(out var cSharpText);
                    Debug.Assert(success);
                    return CSharpSyntaxTree.ParseText(cSharpText!);
                }),
                ..cSharp.Values,
            ],
            Basic.Reference.Assemblies.AspNet80.References.All,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var peFile = getPeFile(finalCompilation);
        string il = getIl(peFile);
        string decompiledCSharp = await getCSharpAsync(peFile);

        var compiledFiles = compiledRazorFiles.AddRange(
            cSharp.Select((pair) => new KeyValuePair<string, CompiledFile>(
                pair.Key,
                new([
                    new("Syntax", pair.Value.GetRoot().Dump()),
                    new("IL", il),
                    new("C#", decompiledCSharp) { Priority = 1 },
                    new("Run", async () =>
                    {
                        await Task.Delay(5_000);
                        return "TBD";
                    }),
                ]))));

        var diagnostics = finalCompilation
            .GetDiagnostics()
            .Where(d => d.Severity != DiagnosticSeverity.Hidden);
        string diagnosticsText = getActualDiagnosticsText(diagnostics);

        return new CompiledAssembly(
            Files: compiledFiles,
            Diagnostics: diagnosticsText,
            NumWarnings: diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning),
            NumErrors: diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));

        RazorProjectEngine createProjectEngine(IReadOnlyList<MetadataReference> references)
        {
            return RazorProjectEngine.Create(config, fileSystem.Inner, b =>
            {
                b.SetRootNamespace("TestNamespace");

                b.Features.Add(RazorAccessors.CreateDefaultTypeNameFeature());
                b.Features.Add(new CompilationTagHelperFeature());
                b.Features.Add(new DefaultMetadataReferenceFeature
                {
                    References = references,
                });

                CompilerFeatures.Register(b);
                RazorExtensions.Register(b);

                b.SetCSharpLanguageVersion(LanguageVersion.Preview);
            });
        }

        static string getActualDiagnosticsText(IEnumerable<Diagnostic> diagnostics)
        {
            var assertText = DiagnosticDescription.GetAssertText(
            expected: [],
            actual: diagnostics,
            unmatchedExpected: [],
            unmatchedActual: diagnostics);
            var startAnchor = "Actual:" + Environment.NewLine;
            var endAnchor = "Diff:" + Environment.NewLine;
            var start = assertText.IndexOf(startAnchor, StringComparison.Ordinal) + startAnchor.Length;
            var end = assertText.IndexOf(endAnchor, start, StringComparison.Ordinal);
            var result = assertText[start..end];
            return removeIndentation(result);
        }

        static string removeIndentation(string text)
        {
            var spaces = new string(' ', 16);
            return text.Trim().Replace(Environment.NewLine + spaces, Environment.NewLine);
        }

        static ICSharpCode.Decompiler.Metadata.PEFile? getPeFile(CSharpCompilation compilation)
        {
            var stream = new MemoryStream();
            var emitResult = compilation.Emit(stream);
            if (!emitResult.Success)
            {
                return null;
            }

            stream.Position = 0;
            return new(compilation.AssemblyName ?? "", stream);
        }

        static string getIl(ICSharpCode.Decompiler.Metadata.PEFile? peFile)
        {
            if (peFile is null)
            {
                return "";
            }

            var output = new ICSharpCode.Decompiler.PlainTextOutput();
            var disassembler = new ICSharpCode.Decompiler.Disassembler.ReflectionDisassembler(output, cancellationToken: default);
            disassembler.WriteModuleContents(peFile);
            return output.ToString();
        }

        static async Task<string> getCSharpAsync(ICSharpCode.Decompiler.Metadata.PEFile? peFile)
        {
            if (peFile is null)
            {
                return "";
            }

            var typeSystem = await ICSharpCode.Decompiler.TypeSystem.DecompilerTypeSystem.CreateAsync(
                peFile,
                new ICSharpCode.Decompiler.Metadata.UniversalAssemblyResolver(
                    mainAssemblyFileName: null,
                    throwOnError: false,
                    targetFramework: ".NETCoreApp,Version=9.0"));
            var decompiler = new ICSharpCode.Decompiler.CSharp.CSharpDecompiler(
                typeSystem,
                new ICSharpCode.Decompiler.DecompilerSettings(ICSharpCode.Decompiler.CSharp.LanguageVersion.CSharp1));
            return decompiler.DecompileWholeModuleAsString();
        }
    }
}

public record InitialCode(string SuggestedFileName, string TextTemplate)
{
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

public sealed record CompiledAssembly(ImmutableDictionary<string, CompiledFile> Files, string Diagnostics, int NumWarnings, int NumErrors);

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

        throw new InvalidOperationException();
    }
}
