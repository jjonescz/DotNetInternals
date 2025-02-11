using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace DotNetLab.Lab;

[SupportedOSPlatform("browser")]
internal static partial class UpdateInfo
{
    public static bool UpdateIsAvailable { get; private set; }

    public static event Action? UpdateBecameAvailable;

    [JSExport]
    public static void UpdateAvailable()
    {
        Console.WriteLine("Update is available");
        UpdateIsAvailable = true;
        UpdateBecameAvailable?.Invoke();
    }
}
