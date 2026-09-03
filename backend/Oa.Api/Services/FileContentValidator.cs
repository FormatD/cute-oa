using System.IO.Compression;

namespace Oa.Api.Services;

public static class FileContentValidator
{
    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0xff];
    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private static readonly byte[] Ole = [0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1];

    public static bool MatchesExtension(string filePath, string extension)
    {
        var normalized = extension.ToLowerInvariant();
        if (normalized is ".docx" or ".xlsx") return MatchesOpenXml(filePath, normalized);
        var expected = normalized switch
        {
            ".pdf" => Pdf,
            ".jpg" or ".jpeg" => Jpeg,
            ".png" => Png,
            ".doc" or ".xls" => Ole,
            _ => []
        };
        if (expected.Length == 0) return false;
        Span<byte> header = stackalloc byte[8];
        using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var read = input.Read(header);
        return read >= expected.Length && header[..expected.Length].SequenceEqual(expected);
    }

    private static bool MatchesOpenXml(string filePath, string extension)
    {
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            var hasContentTypes = archive.Entries.Any(entry => entry.FullName.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase));
            var requiredRoot = extension == ".docx" ? "word/" : "xl/";
            return hasContentTypes && archive.Entries.Any(entry => entry.FullName.StartsWith(requiredRoot, StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidDataException) { return false; }
    }
}
