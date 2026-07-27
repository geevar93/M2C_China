namespace SourcingOps.Application.Common;

/// <summary>Strips path-traversal and separator characters from a caller-supplied filename (TECH_SPEC §4.6, §8).</summary>
public static class FileNameSanitizer
{
    public static string Sanitize(string originalFilename)
    {
        var name = originalFilename.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..]; // drop any directory component
        name = name.Replace("..", string.Empty);

        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "file" : cleaned;
    }
}
