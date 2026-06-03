using System.Text.RegularExpressions;

namespace BiddingBuddy.Bff.Core.Helpers;

/// <summary>Sanitizes file names before building R2 object keys.</summary>
public static class FileNameSanitizer
{
    private static readonly Regex _unsafeChars =
        new(@"[^\w.\-\s]", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Strip path separators, null bytes, and control characters.
    /// Returns only the file name portion; preserves the extension.
    /// </summary>
    public static string Sanitize(string raw)
    {
        // Take only the last segment if a path was supplied
        var name = Path.GetFileName(raw);

        // Remove control characters and null bytes (below 0x20 and DEL 0x7F)
        name = new string(name.Where(c => c >= 0x20 && c != '\x7F').ToArray());

        // Replace anything that isn't alphanumeric, dash, underscore, dot, or space
        name = _unsafeChars.Replace(name, "_");

        return name.Trim();
    }
}
