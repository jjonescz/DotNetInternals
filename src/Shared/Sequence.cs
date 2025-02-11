using System.Text.Json.Serialization;

namespace DotNetLab;

/// <summary>
/// An equatable <see cref="ImmutableArray{T}"/>.
/// </summary>
[method: JsonConstructor]
public readonly struct Sequence<T>(ImmutableArray<T> value) : IEquatable<Sequence<T>>
{
    public ImmutableArray<T> Value { get; } = value;

    public bool Equals(Sequence<T> other)
    {
        if (Value.IsDefault)
        {
            return other.Value.IsDefault;
        }

        if (other.Value.IsDefault)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        var enu1 = Value.GetEnumerator();
        var enu2 = other.Value.GetEnumerator();
        while (true)
        {
            bool has1 = enu1.MoveNext();
            bool has2 = enu2.MoveNext();

            if (has1 != has2)
            {
                return false;
            }

            if (!has1 && !has2)
            {
                return true;
            }

            if (!comparer.Equals(enu1.Current, enu2.Current))
            {
                return false;
            }
        }
    }

    public override bool Equals(object? obj)
    {
        return obj is Sequence<T> sequence && Equals(sequence);
    }

    public static bool operator ==(Sequence<T> left, Sequence<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Sequence<T> left, Sequence<T> right)
    {
        return !(left == right);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Value)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }

    public override string ToString()
    {
        return Value.IsDefault ? string.Empty : Value.JoinToString(", ");
    }

    public static implicit operator ImmutableArray<T>(Sequence<T> sequence)
    {
        return sequence.Value;
    }

    public static implicit operator Sequence<T>(ImmutableArray<T> value)
    {
        return new(value);
    }
}
