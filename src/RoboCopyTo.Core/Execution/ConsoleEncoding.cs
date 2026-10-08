using System.Runtime.InteropServices;
using System.Text;

namespace RoboCopyTo.Core.Execution;

/// <summary>Robocopy writes redirected stdout in the console OEM code page, whatever the console code page is set to.</summary>
public static partial class ConsoleEncoding
{
    private static readonly Lazy<Encoding> OemLazy = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding((int)GetOEMCP());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.GetEncoding(437);
        }
    });

    public static Encoding Oem => OemLazy.Value;

    [LibraryImport("kernel32.dll")]
    private static partial uint GetOEMCP();
}
