﻿namespace DotNetLab.Lab;

internal sealed record InitialCode(string SuggestedFileName, string TextTemplate)
{
    public static readonly InitialCode Razor = new("TestComponent.razor", """
        <TestComponent Param="1" />

        @code {
            [Parameter] public int Param { get; set; }
        }

        """);

    // https://github.com/dotnet/aspnetcore/blob/036ec9ec2ffbfe927f9eb7622dfff122c634ccbb/src/ProjectTemplates/Web.ProjectTemplates/content/BlazorWeb-CSharp/BlazorWeb-CSharp/Components/_Imports.razor
    public static readonly InitialCode RazorImports = new("_Imports.razor", """
        ﻿@using System.Net.Http
        @using System.Net.Http.Json
        @using Microsoft.AspNetCore.Components.Authorization
        @using Microsoft.AspNetCore.Components.Forms
        @using Microsoft.AspNetCore.Components.Routing
        @using Microsoft.AspNetCore.Components.Web
        @using static Microsoft.AspNetCore.Components.Web.RenderMode
        @using Microsoft.AspNetCore.Components.Web.Virtualization
        @using Microsoft.JSInterop
        """);

    public static readonly InitialCode CSharp = new("Program.cs", """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Diagnostics;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;

        class Program
        {
            static void Main()
            {
                Console.WriteLine("Hello.");
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
                public Customer Customer { get; set; } = new();
            }

            public class Customer
            {
                public int Id { get; set; }

                [Required, StringLength(10)]
                public string Name { get; set; } = "";
            }
        }

        """);

    public static readonly InitialCode Configuration = new("Configuration.cs", """
        Config.CSharpParseOptions(options => options
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([new("use-roslyn-tokenizer", "true")]));

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
