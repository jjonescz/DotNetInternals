// https://github.com/dotnet/roslyn/blob/7d940863e07ccd447f6ef5f066018003a29e13bd/src/Compilers/Test/Core/Diagnostics/DiagnosticDescription.cs

#nullable disable

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Test.Utilities
{
    public sealed class DiagnosticDescription
    {
        public static readonly DiagnosticDescription[] None = { };
        public static readonly DiagnosticDescription[] Any = null;

        // common fields for all DiagnosticDescriptions
        private readonly object _code;
        private readonly bool _isWarningAsError;
        private readonly bool _isSuppressed;
        private readonly string _squiggledText;
        private readonly object[] _arguments;
        private readonly LinePosition? _startPosition; // May not have a value only in the case that we're constructed via factories
        private readonly bool _argumentOrderDoesNotMatter;
        private readonly Type _errorCodeType;
        private readonly bool _ignoreArgumentsWhenComparing;
        private readonly DiagnosticSeverity? _defaultSeverityOpt;
        private readonly DiagnosticSeverity? _effectiveSeverityOpt;
        private readonly ImmutableArray<string> _originalFormatSpecifiers = ImmutableArray<string>.Empty;

        // fields for DiagnosticDescriptions constructed via factories
        private readonly Func<SyntaxNode, bool> _syntaxPredicate;
        private bool _showPredicate; // show predicate in ToString if comparison fails

        // fields for DiagnosticDescriptions constructed from Diagnostics
        private readonly Location _location;

        private IEnumerable<string> _argumentsAsStrings;
        private IEnumerable<string> GetArgumentsAsStrings()
        {
            if (_argumentsAsStrings == null)
            {
                // We'll use IFormattable here, because it is more explicit than just calling .ToString()
                // (and is closer to what the compiler actually does when displaying error messages)
                _argumentsAsStrings = _arguments.Select((o, i) =>
                {
                    if (o is DiagnosticInfo embedded)
                    {
                        return embedded.GetMessage(CultureInfo.InvariantCulture);
                    }

                    return i < _originalFormatSpecifiers.Length ?
                        string.Format(CultureInfo.InvariantCulture, _originalFormatSpecifiers[i], o) :
                        string.Format(CultureInfo.InvariantCulture, "{0}", o);
                });
            }
            return _argumentsAsStrings;
        }

        public DiagnosticDescription(
            object code,
            bool isWarningAsError,
            string squiggledText,
            object[] arguments,
            LinePosition? startLocation,
            Func<SyntaxNode, bool> syntaxNodePredicate,
            bool argumentOrderDoesNotMatter,
            Type errorCodeType = null,
            DiagnosticSeverity? defaultSeverityOpt = null,
            DiagnosticSeverity? effectiveSeverityOpt = null,
            bool isSuppressed = false)
        {
            _code = code;
            _isWarningAsError = isWarningAsError;
            _squiggledText = squiggledText;
            _arguments = arguments;
            _startPosition = startLocation;
            _syntaxPredicate = syntaxNodePredicate;
            _argumentOrderDoesNotMatter = argumentOrderDoesNotMatter;
            _errorCodeType = errorCodeType ?? code.GetType();
            _defaultSeverityOpt = defaultSeverityOpt;
            _effectiveSeverityOpt = effectiveSeverityOpt;
            _isSuppressed = isSuppressed;
        }

        public DiagnosticDescription(
            object code,
            string squiggledText,
            object[] arguments,
            LinePosition? startLocation,
            Func<SyntaxNode, bool> syntaxNodePredicate,
            bool argumentOrderDoesNotMatter,
            Type errorCodeType = null,
            DiagnosticSeverity? defaultSeverityOpt = null,
            DiagnosticSeverity? effectiveSeverityOpt = null,
            bool isSuppressed = false)
        {
            _code = code;
            _isWarningAsError = false;
            _squiggledText = squiggledText;
            _arguments = arguments;
            _startPosition = startLocation;
            _syntaxPredicate = syntaxNodePredicate;
            _argumentOrderDoesNotMatter = argumentOrderDoesNotMatter;
            _errorCodeType = errorCodeType ?? code.GetType();
            _defaultSeverityOpt = defaultSeverityOpt;
            _effectiveSeverityOpt = effectiveSeverityOpt;
            _isSuppressed = isSuppressed;
        }

        public DiagnosticDescription(Diagnostic d, bool errorCodeOnly, bool includeDefaultSeverity = false, bool includeEffectiveSeverity = false)
        {
            _code = d.Code;
            _isWarningAsError = d.IsWarningAsError;
            _isSuppressed = d.IsSuppressed;
            _location = d.Location;
            _defaultSeverityOpt = includeDefaultSeverity ? d.DefaultSeverity : (DiagnosticSeverity?)null;
            _effectiveSeverityOpt = includeEffectiveSeverity ? d.Severity : (DiagnosticSeverity?)null;
            _originalFormatSpecifiers = GetFormatSpecifiers(d.Descriptor.MessageFormat.ToString());

            DiagnosticWithInfo dinfo = null;
            if (d.Code == 0 || d.Descriptor.ImmutableCustomTags.Contains(WellKnownDiagnosticTags.CustomObsolete))
            {
                _code = d.Id;
                _errorCodeType = typeof(string);
            }
            else
            {
                dinfo = d as DiagnosticWithInfo;
                if (dinfo == null)
                {
                    _code = d.Code;
                    _errorCodeType = typeof(int);
                }
                else
                {
                    _errorCodeType = dinfo.Info.MessageProvider.ErrorCodeType;
                    _code = d.Code;
                }
            }

            _ignoreArgumentsWhenComparing = errorCodeOnly;

            if (!_ignoreArgumentsWhenComparing)
            {
                if (_location.IsInSource)
                {
                    // we don't just want to do SyntaxNode.GetText(), because getting the text via the SourceTree validates the public API
                    _squiggledText = _location.SourceTree.GetText().ToString(_location.SourceSpan);
                }

                if (dinfo != null)
                {
                    _arguments = dinfo.Info.Arguments;
                }
                else
                {
                    var args = d.Arguments;
                    if (args == null || args.Count == 0)
                    {
                        _arguments = null;
                    }
                    else
                    {
                        _arguments = d.Arguments.ToArray();
                    }
                }

                if (_arguments != null && _arguments.Length == 0)
                {
                    _arguments = null;
                }
            }

            _startPosition = _location.GetMappedLineSpan().StartLinePosition;
        }

        public DiagnosticDescription WithSquiggledText(string squiggledText)
        {
            return new DiagnosticDescription(_code, _isWarningAsError, squiggledText, _arguments, _startPosition, _syntaxPredicate, false, _errorCodeType, _defaultSeverityOpt, _effectiveSeverityOpt, _isSuppressed);
        }

        public DiagnosticDescription WithArguments(params object[] arguments)
        {
            return new DiagnosticDescription(_code, _isWarningAsError, _squiggledText, arguments, _startPosition, _syntaxPredicate, false, _errorCodeType, _defaultSeverityOpt, _effectiveSeverityOpt, _isSuppressed);
        }

        public DiagnosticDescription WithArgumentsAnyOrder(params string[] arguments)
        {
            return new DiagnosticDescription(_code, _isWarningAsError, _squiggledText, arguments, _startPosition, _syntaxPredicate, true, _errorCodeType, _defaultSeverityOpt, _effectiveSeverityOpt, _isSuppressed);
        }

        public DiagnosticDescription WithWarningAsError(bool isWarningAsError)
        {
            return new DiagnosticDescription(_code, isWarningAsError, _squiggledText, _arguments, _startPosition, _syntaxPredicate, true, _errorCodeType, _defaultSeverityOpt, _effectiveSeverityOpt, _isSuppressed);
        }

        public DiagnosticDescription WithDefaultSeverity(DiagnosticSeverity defaultSeverity)
        {
            return new DiagnosticDescription(_code, _isWarningAsError, _squiggledText, _arguments, _startPosition, _syntaxPredicate, true, _errorCodeType, defaultSeverity, _effectiveSeverityOpt, _isSuppressed);
        }

        public DiagnosticDescription WithEffectiveSeverity(DiagnosticSeverity effectiveSeverity)
        {
            return new DiagnosticDescription(_code, _isWarningAsError, _squiggledText, _arguments, _startPosition, _syntaxPredicate, true, _errorCodeType, _defaultSeverityOpt, effectiveSeverity, _isSuppressed);
        }

        /// <summary>
        /// Specialized syntaxPredicate that can be used to verify the start of the squiggled Span
        /// </summary>
        public DiagnosticDescription WithLocation(int line, int column)
        {
            return new DiagnosticDescription(_code, _isWarningAsError, _squiggledText, _arguments, new LinePosition(line - 1, column - 1), _syntaxPredicate, _argumentOrderDoesNotMatter, _errorCodeType, _defaultSeverityOpt, _effectiveSeverityOpt, _isSuppressed);
        }

        /// <summary>
        /// Can be used to unambiguously identify Diagnostics that can not be uniquely identified by code, squiggledText and arguments
        /// </summary>
        /// <param name="syntaxPredicate">The argument to syntaxPredicate will be the nearest SyntaxNode whose Span contains first squiggled character.</param>
        public DiagnosticDescription WhereSyntax(Func<SyntaxNode, bool> syntaxPredicate)
        {
            return new DiagnosticDescription(_code, _isWarningAsError, _squiggledText, _arguments, _startPosition, syntaxPredicate, _argumentOrderDoesNotMatter, _errorCodeType, _defaultSeverityOpt, _effectiveSeverityOpt, _isSuppressed);
        }

        public object Code => _code;
        public string SquiggledText => _squiggledText;
        public bool HasLocation => _startPosition != null;
        public int LocationLine => _startPosition.Value.Line + 1;
        public int LocationCharacter => _startPosition.Value.Character + 1;
        public bool IsWarningAsError => _isWarningAsError;
        public bool IsSuppressed => _isSuppressed;
        public DiagnosticSeverity? DefaultSeverity => _defaultSeverityOpt;
        public DiagnosticSeverity? EffectiveSeverity => _effectiveSeverityOpt;

        public override bool Equals(object obj)
        {
            var d = obj as DiagnosticDescription;

            if (d == null)
                return false;

            if (!_code.Equals(d._code))
                return false;

            if (_isWarningAsError != d._isWarningAsError)
                return false;

            if (_isSuppressed != d._isSuppressed)
                return false;

            if (!_ignoreArgumentsWhenComparing)
            {
                if (_squiggledText != d._squiggledText)
                    return false;
            }

            if (_startPosition != null)
            {
                if (d._startPosition != null)
                {
                    if (_startPosition.Value != d._startPosition.Value)
                    {
                        return false;
                    }
                }
            }

            if (_syntaxPredicate != null)
            {
                if (d._location == null)
                    return false;

                if (!_syntaxPredicate(d._location.SourceTree.GetRoot().FindToken(_location.SourceSpan.Start, true).Parent))
                {
                    _showPredicate = true;
                    return false;
                }

                _showPredicate = false;
            }
            if (d._syntaxPredicate != null)
            {
                if (_location == null)
                    return false;

                if (!d._syntaxPredicate(_location.SourceTree.GetRoot().FindToken(_location.SourceSpan.Start, true).Parent))
                {
                    d._showPredicate = true;
                    return false;
                }

                d._showPredicate = false;
            }

            // If ignoring arguments, we can skip the rest of this method.
            if (_ignoreArgumentsWhenComparing || d._ignoreArgumentsWhenComparing)
                return true;

            // Only validation of arguments should happen between here and the end of this method.
            if (_arguments == null)
            {
                if (d._arguments != null)
                    return false;
            }
            else // _arguments != null
            {
                if (d._arguments == null)
                    return false;

                // we'll compare the arguments as strings
                var args1 = GetArgumentsAsStrings();
                var args2 = d.GetArgumentsAsStrings();
                if (_argumentOrderDoesNotMatter || d._argumentOrderDoesNotMatter)
                {
                    if (args1.Count() != args2.Count() || !args1.SetEquals(args2))
                        return false;
                }
                else
                {
                    if (!args1.SequenceEqual(args2))
                        return false;
                }
            }

            if (_defaultSeverityOpt != d._defaultSeverityOpt ||
                _effectiveSeverityOpt != d._effectiveSeverityOpt)
            {
                return false;
            }

            return true;
        }

        public override int GetHashCode()
        {
            int hashCode;
            hashCode = _code.GetHashCode();
            hashCode = Hash.Combine(_isWarningAsError.GetHashCode(), hashCode);
            hashCode = Hash.Combine(_isSuppressed.GetHashCode(), hashCode);

            // TODO: !!! This implementation isn't consistent with Equals, which might ignore inequality of some members based on ignoreArgumentsWhenComparing flag, etc.
            hashCode = Hash.Combine(_squiggledText, hashCode);
            hashCode = Hash.Combine(_arguments, hashCode);
            if (_startPosition != null)
                hashCode = Hash.Combine(hashCode, _startPosition.Value.GetHashCode());
            if (_defaultSeverityOpt != null)
                hashCode = Hash.Combine(hashCode, ((int)_defaultSeverityOpt.Value).GetHashCode());
            if (_effectiveSeverityOpt != null)
                hashCode = Hash.Combine(hashCode, ((int)_effectiveSeverityOpt.Value).GetHashCode());
            return hashCode;
        }

        private static void AppendArgumentString(StringBuilder sb, string argumentString)
        {
            var beginQuote = "\"";
            var endQuote = "\"";
            if (argumentString.Contains("\""))
            {
                argumentString = argumentString.Replace("\"", "\"\"");
                beginQuote = "@\"";
            }
            sb.Append(beginQuote);
            sb.Append(argumentString);
            sb.Append(endQuote);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.Append("Diagnostic(");
            if (_errorCodeType == typeof(string))
            {
                sb.Append('"').Append(_code).Append('"');
            }
            else
            {
                sb.Append(_errorCodeType.Name);
                sb.Append('.');
                sb.Append(Enum.GetName(_errorCodeType, _code));
            }

            if (_squiggledText != null)
            {
                if (_squiggledText.Contains("\n") || _squiggledText.Contains("\\") || _squiggledText.Contains("\""))
                {
                    sb.Append(", @\"");
                    sb.Append(_squiggledText.Replace("\"", "\"\""));
                }
                else
                {
                    sb.Append(", \"");
                    sb.Append(_squiggledText);
                }

                sb.Append('"');
            }

            if (_isSuppressed)
            {
                sb.Append(", isSuppressed: true");
            }

            sb.Append(')');

            if (_arguments != null)
            {
                sb.Append(".WithArguments(");
                var argumentStrings = GetArgumentsAsStrings().GetEnumerator();
                for (int i = 0; argumentStrings.MoveNext(); i++)
                {
                    AppendArgumentString(sb, argumentStrings.Current);
                    if (i < _arguments.Length - 1)
                    {
                        sb.Append(", ");
                    }
                }
                sb.Append(')');
            }

            if (_startPosition != null)
            {
                sb.Append(".WithLocation(");
                sb.Append(_startPosition.Value.Line + 1);
                sb.Append(", ");
                sb.Append(_startPosition.Value.Character + 1);
                sb.Append(')');
            }

            if (_isWarningAsError)
            {
                sb.Append(".WithWarningAsError(true)");
            }

            if (_defaultSeverityOpt != null)
            {
                sb.Append($".WithDefaultSeverity(DiagnosticSeverity.{_defaultSeverityOpt.Value.ToString()})");
            }

            if (_effectiveSeverityOpt != null)
            {
                sb.Append($".WithEffectiveSeverity(DiagnosticSeverity.{_effectiveSeverityOpt.Value.ToString()})");
            }

            if (_syntaxPredicate != null && _showPredicate)
            {
                sb.Append(".WhereSyntax(...)");
            }

            return sb.ToString();
        }

        private static ImmutableArray<string> GetFormatSpecifiers(string messageFormat)
        {
            var specifiers = ImmutableArray<string>.Empty;
            if (Regex.Matches(messageFormat, @"\{\d+(:\d+)?\}") is { Count: > 0 } matches)
            {
                var builder = ArrayBuilder<string>.GetInstance();
                foreach (Match match in matches)
                {
                    // We use 0 as the position specifier, regardless of what it was in the original format string,
                    // because we format diagnostic arguments one at a time so we cannot have a position specifier greater than 0
                    const string posSpecifier = "0";
                    var fmtSpecifier = match.Groups.Count > 1 && match.Groups[1].Success ? match.Groups[1].Value : "";

                    builder.Add(
                            $@"{{{posSpecifier}{fmtSpecifier}}}");
                }
                specifiers = builder.ToImmutableArray();
            }

            return specifiers;
        }

        private class LinePositionComparer : IComparer<LinePosition?>
        {
            internal static LinePositionComparer Instance = new LinePositionComparer();

            public int Compare(LinePosition? x, LinePosition? y)
            {
                if (x == null)
                {
                    if (y == null)
                    {
                        return 0;
                    }
                    return -1;
                }

                if (y == null)
                {
                    return 1;
                }

                int lineDiff = x.Value.Line.CompareTo(y.Value.Line);
                if (lineDiff != 0)
                {
                    return lineDiff;
                }

                return x.Value.Character.CompareTo(y.Value.Character);
            }
        }
    }
}
