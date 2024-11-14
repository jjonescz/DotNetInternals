using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Test.Utilities;

namespace DotNetInternals;

public static class RoslynAccessors
{
    public static string Dump(this CSharpSyntaxNode node)
    {
        return node.Dump();
    }

    public static string DumpExtended(this CSharpSyntaxNode node)
    {
        return TreeDumper.DumpCompact(nodeOrTokenToTree(node));

        static TreeDumperNode nodeOrTokenToTree(SyntaxNodeOrToken nodeOrToken)
        {
            string text = nodeOrToken.Kind().ToString();

            if (nodeOrToken.AsNode(out var node))
            {
                return new TreeDumperNode(text, null,
                    [
                        ..node.GetLeadingTrivia().Select(triviaToTree),
                        ..node.ChildNodesAndTokens().Select(nodeOrTokenToTree),
                        ..node.GetTrailingTrivia().Select(triviaToTree),
                    ]);
            }

            return new TreeDumperNode(text + " " + stringOrMissing(nodeOrToken), null,
                [
                    ..nodeOrToken.GetLeadingTrivia().Select(triviaToTree),
                    ..nodeOrToken.GetTrailingTrivia().Select(triviaToTree),
                ]);
        }

        static TreeDumperNode triviaToTree(SyntaxTrivia trivia)
        {
            return new TreeDumperNode($"""
                {trivia.Kind()} "{withoutNewLines(trivia.ToString())}"
                """, null,
                trivia.GetStructure() is { } structure ? [nodeOrTokenToTree(structure)] : []);
        }

        static string stringOrMissing(SyntaxNodeOrToken nodeOrToken)
        {
            if (!nodeOrToken.IsMissing)
            {
                return $"""
                    "{withoutNewLines(nodeOrToken.ToString())}"
                    """;
            }

            return "<missing>";
        }

        static string withoutNewLines(string s)
        {
            return s.ReplaceLineEndings("⏎");
        }
    }

    public static string GetDiagnosticsText(this IEnumerable<Diagnostic> actual)
    {
        var sb = new StringBuilder();
        var e = actual.GetEnumerator();
        for (int i = 0; e.MoveNext(); i++)
        {
            Diagnostic d = e.Current;
            string message = ((IFormattable)d).ToString(null, CultureInfo.InvariantCulture);

            if (i > 0)
            {
                sb.AppendLine(",");
            }

            sb.Append("// ");
            sb.AppendLine(message);
            var l = d.Location;
            if (l.IsInSource)
            {
                sb.Append("// ");
                sb.AppendLine(l.SourceTree.GetText().Lines.GetLineFromPosition(l.SourceSpan.Start).ToString());
            }

            var description = new DiagnosticDescription(d, errorCodeOnly: false);
            sb.Append(description.ToString());
        }
        return sb.ToString();
    }
}
