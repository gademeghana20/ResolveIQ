using System.Globalization;
using System.Text;

namespace ResolveIQ.Application.DTOs.Incidents
{
    /// <summary>The rows of a CSV export, and whether the list was cut at the row limit.</summary>
    public record IncidentExport(IReadOnlyList<IncidentSummaryResponse> Rows, bool Truncated)
    {
        /// <summary>An export never has more rows than this, so one request cannot load the whole database.</summary>
        public const int MaxRows = 5000;
    }

    /// <summary>Turns incident rows into CSV text that opens correctly in Excel.</summary>
    public static class IncidentCsv
    {
        private static readonly string[] Header =
        {
            "Number", "Title", "Priority", "Status", "Category", "Reported by", "Assigned to",
            "Created (UTC)", "SLA", "SLA due (UTC)", "SLA minutes left"
        };

        public static string Build(IEnumerable<IncidentSummaryResponse> rows)
        {
            var text = new StringBuilder();
            text.Append(string.Join(',', Header)).Append("\r\n");

            foreach (var r in rows)
            {
                text.Append(string.Join(',', new[]
                {
                    Cell(r.IncidentNumber),
                    Cell(r.Title),
                    Cell(r.Priority?.ToString()),
                    Cell(r.Status.ToString()),
                    Cell(r.CategoryName),
                    Cell(r.ReportedByEmail),
                    Cell(r.AssignedToEmail),
                    Cell(r.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    Cell(r.SlaState.ToString()),
                    Cell(r.SlaDueAtUtc?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    // A real number, written as is: it can be negative, which is not an attack.
                    r.SlaMinutesLeft?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
                })).Append("\r\n");
            }

            return text.ToString();
        }

        /// <summary>
        /// One safe cell. Text starting with = + - @ (or a tab or line break) would be run as a formula by
        /// Excel, so a harmless apostrophe is put in front. Quotes, commas and line breaks are escaped.
        /// </summary>
        public static string Cell(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n')
            {
                value = "'" + value;
            }

            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }
    }
}
