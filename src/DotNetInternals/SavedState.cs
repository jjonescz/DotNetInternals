using System.Collections.Immutable;
using DotNetInternals.RazorAccess;
using ProtoBuf;

namespace DotNetInternals;

[ProtoContract]
internal sealed class SavedState
{
    [ProtoMember(1)]
    public ImmutableArray<InputCode> Inputs { get; init; }
}
