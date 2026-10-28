using System.Runtime.InteropServices;
using System.Text;

namespace Ember.Core;

/// <summary>
/// Console tools such as powercfg, gpupdate or whoami write their output in the OEM code page
/// (CP850 on a German Windows), not in UTF-8. Decoding it as UTF-8 turns "Höchstleistung" into
/// "H?chstleistung" (with a replacement character). This class provides the right decoder.
/// </summary>
public static class ConsoleEncoding
{
    static ConsoleEncoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    static Encoding? _oem;

    /// <summary>The OEM code page of this machine (GetOEMCP), which new consoles start with.</summary>
    public static Encoding Oem => _oem ??= ForCodePage(OperatingSystem.IsWindows() ? (int)GetOEMCP() : 850);

    public static Encoding ForCodePage(int codePage) => Encoding.GetEncoding(codePage);

    public static string Decode(byte[] bytes, int codePage) => ForCodePage(codePage).GetString(bytes);

    [DllImport("kernel32.dll")]
    static extern uint GetOEMCP();
}
