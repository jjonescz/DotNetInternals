namespace DotNetInternals.Lab;

internal sealed record InitialCode(string SuggestedFileName, string TextTemplate)
{
    public static readonly InitialCode Razor = new("TestComponent.razor", """
        <TestComponent Param="1" />

        @code {
            [Parameter] public int Param { get; set; }
        }

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
