using System.Text;
using Microsoft.AspNetCore.Mvc.Razor.Extensions;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.NET.Sdk.Razor.SourceGenerators;
using Roslyn.Test.Utilities;

namespace RazorInternals;

public static class RazorCompiler
{
    public static string Compile(string input)
    {
        var filePath = "/folder/file.razor";
        var item = new SourceGeneratorProjectItem(
            basePath: "/",
            filePath: filePath,
            relativePhysicalPath: "/folder",
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

        return razorCodeDocument.GetCSharpDocument().GeneratedCode;
    }
}
