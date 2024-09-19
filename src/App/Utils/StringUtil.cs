namespace DotNetInternals;

internal static class StringUtil
{
    public static string SeparateThousands(this int number)
    {
        return number.ToString("N0");
    }
}
