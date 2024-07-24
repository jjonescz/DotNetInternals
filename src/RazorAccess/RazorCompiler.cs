using System.Text;
using Microsoft.AspNetCore.Mvc.Razor.Extensions;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.NET.Sdk.Razor.SourceGenerators;
using Roslyn.Test.Utilities;

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
        var filePath = "/TestNamespace/TestComponent.razor";
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

        var projectEngine = RazorProjectEngine.Create(config, fileSystem, static b =>
        {
            b.Features.Add(new DefaultTypeNameFeature());
            b.SetRootNamespace("TestNamespace");

            b.Features.Add(new ConfigureRazorCodeGenerationOptions(options =>
            {
            }));

            CompilerFeatures.Register(b);
            RazorExtensions.Register(b);

            b.SetCSharpLanguageVersion(LanguageVersion.Preview);
        });

        var razorCodeDocument = projectEngine.Process(item);

        var syntax = razorCodeDocument.GetSyntaxTree().Root.SerializedValue;

        var ir = formatDocumentTree(razorCodeDocument.GetDocumentIntermediateNode());

        var cSharp = razorCodeDocument.GetCSharpDocument().GeneratedCode;

        return new CompiledRazor(Syntax: syntax, Ir: ir, CSharp: cSharp);

        static string formatDocumentTree(DocumentIntermediateNode node)
        {
            var formatter = new DebuggerDisplayFormatter();
            formatter.FormatTree(node);
            return formatter.ToString();
        }
    }
}

public record CompiledRazor(string Syntax, string Ir, string CSharp);
