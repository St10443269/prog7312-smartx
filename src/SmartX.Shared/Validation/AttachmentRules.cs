namespace SmartX.Shared.Validation;

/// <summary>
/// What files can be attached to a sensor. The Sensors page checks a file
/// against these rules before uploading it, and the API checks them again
/// (following the upload-validation advice in OWASP Foundation, n.d.: allow only
/// known file types and limit the size).
/// </summary>
public static class AttachmentRules
{
    public const long MaxBytes = 25 * 1024 * 1024; // 25 MB

    public static readonly string[] AllowedExtensions =
        [".json", ".yaml", ".yml", ".txt", ".log", ".cfg", ".conf", ".png", ".jpg", ".jpeg", ".pdf"];

    public static string AllowedText => string.Join(", ", AllowedExtensions);

    /// <summary>Returns a message if the file can't be attached, or null if it is fine.</summary>
    public static string? Check(string? fileName, long length)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "The file has no name.";
        }

        if (length <= 0)
        {
            return "The file is empty.";
        }

        if (length > MaxBytes)
        {
            return $"The file is {FormatSize(length)}, which is over the {FormatSize(MaxBytes)} limit.";
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            var shown = extension.Length == 0 ? "no extension" : $"'{extension}'";
            return $"File type {shown} is not allowed. Allowed types: {AllowedText}.";
        }

        return null;
    }

    // InvariantCulture so the size reads "1.5 KB" on every computer, not "1,5 KB"
    // on one set to a language that uses a decimal comma
    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{(bytes / 1024.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} KB",
        _ => $"{(bytes / (1024.0 * 1024.0)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} MB"
    };
}
