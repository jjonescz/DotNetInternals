using Microsoft.AspNetCore.Mvc.Razor.Extensions;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.NET.Sdk.Razor.SourceGenerators;
using Roslyn.Test.Utilities;
using System.Text;

namespace DotNetInternals.RazorAccess;

public static class RazorCompiler
{
    public static readonly string InitialCode = """
        <TestComponent Param="1" />

        @code {
            [Parameter] public int Param { get; set; }
        }
        """;

    public static CompiledRazor Compile(string input)
    {
        var filePath = "/TestProject/TestComponent.razor";
        var item = new SourceGeneratorProjectItem(
            basePath: "/",
            filePath: filePath,
            relativePhysicalPath: "TestComponent.razor",
            fileKind: FileKinds.Component,
            additionalText: new TestAdditionalText(input, encoding: Encoding.UTF8, path: filePath),
            cssScope: null);

        var fileSystem = new VirtualRazorProjectFileSystem();
        fileSystem.Add(item);

        var config = RazorConfiguration.Default;

        // Phase 1: Declaration only (to be used as a reference from which tag helpers will be discovered).
        RazorProjectEngine declarationProjectEngine = createProjectEngine([]);
        RazorCodeDocument declarationCodeDocument = declarationProjectEngine.ProcessDeclarationOnly(item);
        string declarationCSharp = declarationCodeDocument.GetCSharpDocument().GeneratedCode;
        var declarationCompilation = CSharpCompilation.Create("TestAssembly", [CSharpSyntaxTree.ParseText(declarationCSharp)]);

        // Phase 2: Full generation.
        RazorProjectEngine projectEngine = createProjectEngine([declarationCompilation.ToMetadataReference()]);
        RazorCodeDocument codeDocument = projectEngine.Process(item);

        string syntax = codeDocument.GetSyntaxTree().Root.SerializedValue;

        string ir = formatDocumentTree(codeDocument.GetDocumentIntermediateNode());

        string cSharp = codeDocument.GetCSharpDocument().GeneratedCode;

        return new CompiledRazor(Syntax: syntax, Ir: ir, CSharp: cSharp);

        RazorProjectEngine createProjectEngine(IReadOnlyList<MetadataReference> references)
        {
            return RazorProjectEngine.Create(config, fileSystem, b =>
            {
                b.SetRootNamespace("TestNamespace");

                b.Features.Add(new DefaultTypeNameFeature());
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

        static string formatDocumentTree(DocumentIntermediateNode node)
        {
            var formatter = new DebuggerDisplayFormatter();
            formatter.FormatTree(node);
            return formatter.ToString();
        }
    }
}

public record CompiledRazor(string Syntax, string Ir, string CSharp);
