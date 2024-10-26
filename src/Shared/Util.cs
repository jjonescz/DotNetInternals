namespace DotNetInternals;

public static class Util
{
    public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    public static void AddRange<T>(this ICollection<T> collection, ReadOnlySpan<T> items)
    {
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    public static void CaptureConsoleOutput(Action action, out string stdout, out string stderr)
    {
        using var stdoutWriter = new StringWriter();
        using var stderrWriter = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        Console.SetOut(stdoutWriter);
        Console.SetError(stderrWriter);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
        stdout = stdoutWriter.ToString();
        stderr = stderrWriter.ToString();
    }

    public static async IAsyncEnumerable<T> Concat<T>(this IAsyncEnumerable<T> a, IEnumerable<T> b)
    {
        await foreach (var item in a)
        {
            yield return item;
        }
        foreach (var item in b)
        {
            yield return item;
        }
    }

    public static async IAsyncEnumerable<T> Concat<T>(this IAsyncEnumerable<T> a, IEnumerable<Task<T>> b)
    {
        await foreach (var item in a)
        {
            yield return item;
        }
        foreach (var item in b)
        {
            yield return await item;
        }
    }

    public static string JoinToString<T>(this IEnumerable<T> source, string separator)
    {
        return string.Join(separator, source);
    }

    public static string JoinToString<T>(this IEnumerable<T> source, string separator, string quote)
    {
        return string.Join(separator, source.Select(x => $"{quote}{x}{quote}"));
    }

    public static async Task<IEnumerable<TResult>> SelectAsync<T, TResult>(this IEnumerable<T> source, Func<T, Task<TResult>> selector)
    {
        var results = new List<TResult>(source.TryGetNonEnumeratedCount(out var count) ? count : 0);
        foreach (var item in source)
        {
            results.Add(await selector(item));
        }
        return results;
    }

    public static IEnumerable<TResult> SelectNonNull<T, TResult>(this IEnumerable<T> source, Func<T, TResult?> selector)
    {
        foreach (var item in source)
        {
            if (selector(item) is TResult result)
            {
                yield return result;
            }
        }
    }

    public static async Task<Dictionary<TKey, TValue>> ToDictionaryAsync<T, TKey, TValue>(
        this IAsyncEnumerable<T> source,
        Func<T, TKey> keySelector,
        Func<T, TValue> valueSelector)
        where TKey : notnull
    {
        var dictionary = new Dictionary<TKey, TValue>();
        await foreach (var item in source)
        {
            dictionary.Add(keySelector(item), valueSelector(item));
        }
        return dictionary;
    }

    public static async Task<ImmutableArray<T>> ToImmutableArrayAsync<T>(this IAsyncEnumerable<T> source)
    {
        var builder = ImmutableArray.CreateBuilder<T>();
        await foreach (var item in source)
        {
            builder.Add(item);
        }
        return builder.ToImmutable();
    }

    public static async Task<ImmutableDictionary<TKey, TValue>> ToImmutableDictionaryAsync<T, TKey, TValue>(
        this IEnumerable<T> source,
        Func<T, TKey> keySelector,
        Func<T, Task<TValue>> valueSelector)
        where TKey : notnull
    {
        var builder = ImmutableDictionary.CreateBuilder<TKey, TValue>();

        foreach (var item in source)
        {
            builder.Add(keySelector(item), await valueSelector(item));
        }

        return builder.ToImmutable();
    }

    public static Task<ImmutableDictionary<TKey, TValue>> ToImmutableDictionaryAsync<T, TKey, TValue>(
        this IAsyncEnumerable<T> source,
        Func<T, TKey> keySelector,
        Func<T, TValue> valueSelector)
        where TKey : notnull
    {
        return ToImmutableDictionaryAsync(source, keySelector, valueSelector, _ => Unreachable<Task<TValue>>(), []);
    }

    public static async Task<ImmutableDictionary<TKey, TValue>> ToImmutableDictionaryAsync<T, TKey, TValue>(
        this IAsyncEnumerable<T> source,
        Func<T, TKey> keySelector,
        Func<T, TValue> valueSelector,
        Func<TKey, Task<TValue>> fallbackSelector,
        IEnumerable<TKey> fallbacks)
        where TKey : notnull
    {
        var builder = ImmutableDictionary.CreateBuilder<TKey, TValue>();

        await foreach (var item in source)
        {
            builder.Add(keySelector(item), valueSelector(item));
        }

        foreach (var key in fallbacks)
        {
            if (!builder.ContainsKey(key))
            {
                builder.Add(key, await fallbackSelector(key));
            }
        }

        return builder.ToImmutable();
    }

    public static IEnumerable<T> TryConcat<T>(this ImmutableArray<T>? a, ImmutableArray<T>? b)
    {
        return [.. (a ?? []), .. (b ?? [])];
    }

    public static T Unreachable<T>()
    {
        throw new InvalidOperationException($"Unreachable '{typeof(T)}'.");
    }

    public static string WithoutSuffix(this string s, string suffix)
    {
        return s.EndsWith(suffix, StringComparison.Ordinal) ? s[..^suffix.Length] : s;
    }
}
