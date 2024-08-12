using Microsoft.AspNetCore.Mvc.Razor.Extensions;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace DotNetInternals;

public class Compiler : ICompiler
{
    public Task<CompiledAssembly> CompileAsync(IEnumerable<InputCode> inputs)
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

        ICSharpCode.Decompiler.Metadata.PEFile? peFile = null;

        var compiledFiles = compiledRazorFiles.AddRange(
            cSharp.Select((pair) => new KeyValuePair<string, CompiledFile>(
                pair.Key,
                new([
                    new("Syntax", pair.Value.GetRoot().Dump()),
                    new("IL", () =>
                    {
                        peFile ??= getPeFile(finalCompilation);
                        return getIl(peFile);
                    }),
                    new("C#", async () =>
                    {
                        peFile ??= getPeFile(finalCompilation);
                        return await getCSharpAsync(peFile);
                    })
                    {
                        Priority = 1
                    },
                    new("Run", () =>
                    {
                        var executableCompilation = finalCompilation
                            .WithOptions(finalCompilation.Options.WithOutputKind(OutputKind.ConsoleApplication));
                        var emitStream = getEmitStream(executableCompilation);
                        return emitStream is null ? "" : Executor.Execute(emitStream);
                    }),
                ]))));

        IEnumerable<Diagnostic> diagnostics = finalCompilation
            .GetDiagnostics()
            .Where(d => d.Severity != DiagnosticSeverity.Hidden);
        string diagnosticsText = getActualDiagnosticsText(diagnostics);
        int numWarnings = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
        int numErrors = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        ImmutableArray<DiagnosticData> diagnosticData = diagnostics
            .Select(d =>
            {
                string? filePath = d.Location.SourceTree?.FilePath;
                FileLinePositionSpan lineSpan;

                if (string.IsNullOrEmpty(filePath) &&
                    d.Location.GetMappedLineSpan() is { IsValid: true, HasMappedPath: true } mappedLineSpan)
                {
                    filePath = mappedLineSpan.Path;
                    lineSpan = mappedLineSpan;
                }
                else
                {
                    lineSpan = d.Location.GetLineSpan();
                }

                return new DiagnosticData(
                    FilePath: filePath,
                    Severity: d.Severity switch
                    {
                        DiagnosticSeverity.Error => DiagnosticDataSeverity.Error,
                        DiagnosticSeverity.Warning => DiagnosticDataSeverity.Warning,
                        _ => DiagnosticDataSeverity.Info,
                    },
                    Id: d.Id,
                    HelpLinkUri: d.Descriptor.HelpLinkUri,
                    Message: d.GetMessage(),
                    StartLineNumber: lineSpan.StartLinePosition.Line + 1,
                    StartColumn: lineSpan.StartLinePosition.Character + 1,
                    EndLineNumber: lineSpan.EndLinePosition.Line + 1,
                    EndColumn: lineSpan.EndLinePosition.Character + 1
                );
            })
            .ToImmutableArray();

        var result = new CompiledAssembly(
            BaseDirectory: directory,
            Files: compiledFiles,
            NumWarnings: numWarnings,
            NumErrors: numErrors,
            Diagnostics: diagnosticData,
            GlobalOutputs:
            [
                new(CompiledAssembly.DiagnosticsOutputType, diagnosticsText)
                {
                    Priority = numErrors > 0 ? 2 : 0,
                },
            ]);

        return Task.FromResult(result);

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

        static MemoryStream? getEmitStream(CSharpCompilation compilation)
        {
            var stream = new MemoryStream();
            var emitResult = compilation.Emit(stream);
            if (!emitResult.Success)
            {
                return null;
            }

            stream.Position = 0;
            return stream;
        }

        static ICSharpCode.Decompiler.Metadata.PEFile? getPeFile(CSharpCompilation compilation)
        {
            return getEmitStream(compilation) is { } stream
                ? new(compilation.AssemblyName ?? "", stream)
                : null;
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
