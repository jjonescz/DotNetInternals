using ProtoBuf;
using System.Collections.Immutable;

namespace DotNetInternals.Lab;

[ProtoContract]
internal sealed class SavedState
{
    [ProtoMember(1)]
    public ImmutableArray<InputCode> Inputs { get; init; }
}
