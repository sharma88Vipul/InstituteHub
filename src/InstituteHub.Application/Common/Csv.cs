using System.Globalization;
using System.Text;

namespace InstituteHub.Application.Common;

/// <summary>
/// Minimal RFC 4180 CSV reading and writing (comma separated, double-quoted fields, quotes doubled inside quotes,
/// line breaks allowed inside quotes). Files open in Excel: <see cref="Write"/> adds a UTF-8 BOM.
/// </summary>
public static class Csv
{
    /// <summary>Rows of fields. Blank lines are skipped; a leading BOM is ignored.</summary>
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
        }

        void EndRow()
        {
            EndField();
            if (fields.Count > 1 || fields[0].Trim().Length > 0) rows.Add(fields.ToArray());
            fields.Clear();
        }

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    break;
                case ',':
                    EndField();
                    break;
                case '\r':
                    break; // \r\n or a lone \r: the row ends at \n (or at the end of the text)
                case '\n':
                    EndRow();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0) EndRow();
        return rows;
    }

    /// <summary>A CSV file (UTF-8 with BOM, CRLF line ends) from a header row and data rows.</summary>
    public static byte[] Write(IEnumerable<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', header.Select(Escape))).Append("\r\n");
        foreach (var row in rows)
        {
            sb.Append(string.Join(',', row.Select(v => Escape(Format(v))))).Append("\r\n");
        }

        var bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        return [.. bom, .. body];
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset t => t.ToOffset(TimeSpan.FromHours(5.5)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.##", CultureInfo.InvariantCulture),
        double x => x.ToString("0.#", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string Escape(string value)
    {
        // Cells starting with = + - @ are formulas in Excel; prefix them so data cannot run as a formula.
        if (value.Length > 0 && value[0] is '=' or '+' or '@' && !decimal.TryParse(value, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        if (value.Length > 0 && value[0] == '-' && !decimal.TryParse(value, CultureInfo.InvariantCulture, out _))
            value = "'" + value;

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
