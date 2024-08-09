using System.Collections.Immutable;
using ProtoBuf;

namespace DotNetInternals.Lab;

[ProtoContract]
internal sealed class SavedState
{
    [ProtoMember(1)]
    public ImmutableArray<InputCode> Inputs { get; init; }
}
