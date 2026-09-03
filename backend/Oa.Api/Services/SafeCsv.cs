using System.Text;

namespace Oa.Api.Services;

public static class SafeCsv
{
    public static void AppendRow(StringBuilder builder, IEnumerable<string?> values) =>
        builder.AppendLine(string.Join(',', values.Select(EscapeCell)));

    public static byte[] ToUtf8Bom(string value) =>
        Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(value)).ToArray();

    private static string EscapeCell(string? raw)
    {
        var value = raw ?? string.Empty;
        var firstNonWhitespace = value.FirstOrDefault(character => !char.IsWhiteSpace(character));
        var leadingControl = value.TakeWhile(char.IsWhiteSpace).Any(character => character is '\t' or '\r' or '\n');
        if (leadingControl || firstNonWhitespace is '=' or '+' or '-' or '@') value = $"'{value}";
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
