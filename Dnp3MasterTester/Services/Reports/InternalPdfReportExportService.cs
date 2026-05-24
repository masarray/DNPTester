using System.Globalization;
using System.IO;
using System.Text;
using Dnp3MasterTester.Models.Reports;

namespace Dnp3MasterTester.Services.Reports;

public sealed class InternalPdfReportExportService
{
    public string RenderPreview(FatTestSessionSnapshot snapshot)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Dnp3MasterTester", "ReportPreview");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{SanitizeFileName(snapshot.ReportId)}-{DateTime.Now:yyyyMMdd-HHmmssfff}-preview.pdf");
        Export(snapshot, path);
        return path;
    }

    public void Export(FatTestSessionSnapshot snapshot, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var document = new FatReportPdfDocument(snapshot);
        File.WriteAllBytes(path, document.Render());
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '-' : ch));
    }

    private sealed class FatReportPdfDocument
    {
        private const double PageWidth = 595.28;
        private const double PageHeight = 841.89;
        private const double Margin = 34;
        private const double FooterY = 30;
        private const double ContentBottom = 58;
        private const double LineHeight = 12;
        private const string Ink = "0.078 0.141 0.196";
        private const string Muted = "0.373 0.431 0.486";
        private const string Accent = "0.059 0.420 0.478";
        private const string Line = "0.867 0.894 0.925";
        private const string Soft = "0.945 0.965 0.969";

        private readonly FatTestSessionSnapshot _snapshot;
        private readonly List<PdfPage> _pages = [];
        private PdfPage _page = new();
        private double _y;

        public FatReportPdfDocument(FatTestSessionSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public byte[] Render()
        {
            NewPage();
            ComposeCover();
            NewPage();
            ComposeExecutiveSummary();
            ComposeFatMatrix();
            ComposeFatItemDetails();
            NewPage();
            ComposeCommunicationAudit();
            ComposePointAudit();
            NewPage();
            ComposeEventAudit();
            ComposeCommandAudit();
            NewPage();
            ComposeAppendix();
            FinalizeCurrentPage();
            return PdfWriter.Write(_pages);
        }

        private void ComposeCover()
        {
            DrawText(_snapshot.ReportId, Margin, _y, 11, Accent, bold: true);
            DrawText(_snapshot.Branding.ProjectName, Margin, _y - 14, 9, Muted);
            _y -= 88;

            DrawText("DNP3 Interoperability Test Report", Margin, _y, 26, Ink, bold: true);
            _y -= 22;
            DrawText($"{_snapshot.Branding.CompanyName} / {_snapshot.Branding.CustomerName}", Margin, _y, 11, Muted);
            _y -= 34;
            SummaryBand();
            _y -= 38;

            TwoColumnInfo([
                ("Customer", _snapshot.Branding.CustomerName),
                ("Project", _snapshot.Branding.ProjectName),
                ("DUT / Target", _snapshot.ConnectionTarget),
                ("Connection", _snapshot.ConnectionProfile),
                ("Point Profile", _snapshot.PointCatalogProfileName),
                ("Generated", _snapshot.GeneratedAtText),
                ("Finalized", _snapshot.FinalizedAtText),
                ("Prepared By", _snapshot.Branding.PreparedBy),
                ("Reviewed / Approved", BuildReviewApproval(_snapshot))
            ]);
        }

        private void ComposeExecutiveSummary()
        {
            Section("Executive Summary");
            SummaryBand();
            Paragraph("FAT completion and technical result are intentionally separated. Open or not-tested items reduce completion, but they do not imply technical failure unless a failed evidence result exists.", Muted);

            if (_snapshot.Observations.Count > 0)
            {
                Section("Engineering Observations", 12);
                foreach (var observation in _snapshot.Observations.Take(6))
                {
                    Bullet(observation);
                }
            }
        }

        private void ComposeFatMatrix()
        {
            Section("FAT Result Matrix");
            Paragraph("Each row is derived from explicit evidence recognition rules. Items remain NOT TESTED when the required evidence is absent or the workflow is not yet supported by the application.", Muted);
            Table(
                ["Item", "Test", "Evidence Summary", "Verdict", "Ev."],
                [52, 115, 210, 94, 40],
                _snapshot.FatItems.Select(x => new[]
                {
                    x.ItemCode,
                    x.Title,
                    x.EvidenceSummary,
                    x.VerdictText,
                    x.EvidenceCount.ToString(CultureInfo.InvariantCulture)
                }));
        }

        private void ComposeFatItemDetails()
        {
            Section("FAT Test Method & Recognition");
            foreach (var item in _snapshot.FatItems)
            {
                EnsureSpace(84);
                DrawRoundedBox(Margin, _y - 74, PageWidth - (Margin * 2), 78, "1 1 1");
                DrawText(item.ItemCode, Margin + 10, _y - 16, 10, Accent, bold: true);
                DrawText(item.Title, Margin + 66, _y - 16, 10, Ink, bold: true);
                DrawText(item.VerdictText, PageWidth - Margin - 118, _y - 16, 8, VerdictColor(item.Verdict), bold: true);
                _y -= 30;
                KeyValue("Objective", item.Objective);
                KeyValue("How to test", item.TestMethod);
                KeyValue("Required evidence", item.RequiredEvidence);
                KeyValue("Recognition rule", item.RecognitionRule);
                KeyValue("Current rationale", item.Rationale);
                _y -= 16;
            }
        }

        private void ComposeCommunicationAudit()
        {
            Section("Communication Audit");
            TwoColumnInfo([
                ("State", _snapshot.ConnectionState),
                ("Detail", _snapshot.ConnectionDetail),
                ("Transport / Address", _snapshot.ConnectionProfile),
                ("Polling", _snapshot.PollingProfile)
            ]);
            EvidenceTable("Protocol Trace Extract", ["Time", "Level", "Direction", "Summary"], [66, 48, 58, 339], _snapshot.TraceEvidence.Take(32).Select(x => new[]
            {
                x.TimestampLocal.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                x.Level,
                x.Direction,
                x.Summary
            }));
        }

        private void ComposePointAudit()
        {
            Section("Point Read / Value Audit");
            EvidenceTable("Point Evidence Extract", ["Type", "Index", "Point", "Value", "Quality", "Timestamp"], [76, 34, 142, 64, 72, 123], _snapshot.PointEvidence.Take(42).Select(x => new[]
            {
                x.PointType,
                x.Index.ToString(CultureInfo.InvariantCulture),
                x.PointLabel,
                x.Value,
                x.Quality,
                x.SourceTimestamp
            }));
        }

        private void ComposeEventAudit()
        {
            Section("Event / SOE Audit");
            EvidenceTable("SCADA Event Evidence", ["Time", "Event", "Point", "Value", "Status"], [66, 84, 205, 64, 92], _snapshot.EventEvidence.Take(34).Select(x => new[]
            {
                x.TimestampLocal.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                x.EvidenceType,
                x.PointLabel,
                x.Value,
                x.Status
            }));
            EvidenceTable("SOE Timestamp Evidence", ["Received", "Type", "Point", "Value", "Timestamp"], [66, 84, 205, 64, 92], _snapshot.SoeEvidence.Take(34).Select(x => new[]
            {
                x.TimestampLocal.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                x.EvidenceType,
                x.PointLabel,
                x.Value,
                x.Status
            }));
        }

        private void ComposeCommandAudit()
        {
            Section("Command Audit");
            EvidenceTable("Command Lifecycle Evidence", ["Transaction", "Point", "Mode", "Operation", "Acceptance", "Feedback", "Verdict"], [70, 92, 64, 62, 76, 76, 71], _snapshot.CommandEvidence.Select(x => new[]
            {
                x.TransactionId,
                x.PointLabel,
                x.CommandMode,
                x.Operation,
                x.AcceptanceResult,
                x.FeedbackResult,
                x.FinalVerdict
            }));
        }

        private void ComposeAppendix()
        {
            Section("Appendix");
            Paragraph("Appendix evidence is limited in the printable report to keep the document readable. The snapshot model retains structured evidence for later full evidence export.", Muted);
            EvidenceTable("FAT Item Rationale", ["Item", "Objective", "Criteria", "Rationale"], [45, 150, 150, 166], _snapshot.FatItems.Select(x => new[]
            {
                x.ItemCode,
                x.Objective,
                x.AcceptanceCriteria,
                x.Rationale
            }));
        }

        private void NewPage()
        {
            FinalizeCurrentPage();
            _page = new PdfPage();
            _pages.Add(_page);
            _y = PageHeight - 62;
            Header();
        }

        private void FinalizeCurrentPage()
        {
            if (!_pages.Contains(_page))
            {
                return;
            }

            Footer();
        }

        private void Header()
        {
            DrawText(_snapshot.ReportId, Margin, PageHeight - 34, 10, Accent, bold: true);
            DrawText(_snapshot.Branding.ProjectName, Margin, PageHeight - 47, 8, Muted);
            DrawText(_snapshot.EvidenceStateText, PageWidth - Margin - 150, PageHeight - 40, 8, Muted);
            LineAt(Margin, PageHeight - 55, PageWidth - Margin, PageHeight - 55, Line);
        }

        private void Footer()
        {
            LineAt(Margin, FooterY + 13, PageWidth - Margin, FooterY + 13, Line);
            DrawText(_snapshot.Branding.FooterText, Margin, FooterY, 8, Muted);
            DrawText($"Page {_pages.IndexOf(_page) + 1}", PageWidth - Margin - 60, FooterY, 8, Muted);
        }

        private void Section(string title, double fontSize = 16)
        {
            EnsureSpace(30);
            DrawText(title, Margin, _y, fontSize, Accent, bold: true);
            _y -= 20;
        }

        private void Paragraph(string text, string color)
        {
            foreach (var line in Wrap(text, 96))
            {
                EnsureSpace(LineHeight);
                DrawText(line, Margin, _y, 8.5, color);
                _y -= LineHeight;
            }

            _y -= 8;
        }

        private void Bullet(string text)
        {
            foreach (var line in Wrap(text, 92))
            {
                EnsureSpace(LineHeight);
                DrawText("- " + line, Margin + 6, _y, 8.2, Muted);
                _y -= LineHeight;
            }
        }

        private void KeyValue(string label, string value)
        {
            var text = $"{label}: {(string.IsNullOrWhiteSpace(value) ? "-" : value)}";
            foreach (var line in Wrap(text, 105).Take(1))
            {
                DrawText(line, Margin + 10, _y, 7.7, Muted);
                _y -= 10;
            }
        }

        private void SummaryBand()
        {
            EnsureSpace(56);
            DrawRoundedBox(Margin, _y - 44, PageWidth - (Margin * 2), 52, Soft);
            var metrics = new[]
            {
                ("FAT STATUS", _snapshot.FatExecutionStatus),
                ("TECHNICAL RESULT", _snapshot.TechnicalResult),
                ("EXECUTED", $"{_snapshot.ExecutedItemCount}/{_snapshot.FatItems.Count}"),
                ("FAILED", _snapshot.FailedItemCount.ToString(CultureInfo.InvariantCulture)),
                ("OPEN", _snapshot.OpenItemCount.ToString(CultureInfo.InvariantCulture))
            };

            var width = (PageWidth - (Margin * 2) - 24) / metrics.Length;
            for (var i = 0; i < metrics.Length; i++)
            {
                var x = Margin + 12 + (width * i);
                DrawText(metrics[i].Item1, x, _y - 13, 7, Muted);
                DrawText(metrics[i].Item2, x, _y - 29, 10, Ink, bold: true);
            }

            _y -= 62;
        }

        private void TwoColumnInfo(IReadOnlyList<(string Label, string Value)> items)
        {
            var colWidth = (PageWidth - (Margin * 2) - 10) / 2;
            for (var i = 0; i < items.Count; i += 2)
            {
                EnsureSpace(44);
                InfoBox(Margin, _y - 34, colWidth, items[i].Label, items[i].Value);
                if (i + 1 < items.Count)
                {
                    InfoBox(Margin + colWidth + 10, _y - 34, colWidth, items[i + 1].Label, items[i + 1].Value);
                }

                _y -= 44;
            }

            _y -= 8;
        }

        private void InfoBox(double x, double y, double width, string label, string value)
        {
            DrawRoundedBox(x, y, width, 36, "1 1 1");
            DrawText(label.ToUpperInvariant(), x + 8, y + 22, 7, Muted);
            DrawText(Trim(value, 48), x + 8, y + 9, 8.5, Ink, bold: true);
        }

        private void EvidenceTable(string title, string[] headers, double[] widths, IEnumerable<string[]> rows)
        {
            Section(title, 11);
            var materialized = rows.ToArray();
            if (materialized.Length == 0)
            {
                Paragraph("No evidence captured for this section yet.", Muted);
                return;
            }

            Table(headers, widths, materialized);
        }

        private void Table(string[] headers, double[] widths, IEnumerable<string[]> rows)
        {
            EnsureSpace(34);
            DrawTableRow(headers, widths, header: true);
            foreach (var row in rows)
            {
                DrawTableRow(row.Take(headers.Length).ToArray(), widths, header: false);
            }

            _y -= 8;
        }

        private void DrawTableRow(string[] cells, double[] widths, bool header)
        {
            EnsureSpace(header ? 18 : 20);
            var rowHeight = header ? 18 : 20;
            var x = Margin;
            DrawRect(Margin, _y - rowHeight + 4, widths.Sum(), rowHeight, header ? "0.933 0.957 0.965" : "1 1 1");
            for (var i = 0; i < cells.Length && i < widths.Length; i++)
            {
                DrawText(Trim(cells[i], Math.Max(6, (int)(widths[i] / 4.2))), x + 4, _y - 8, header ? 7 : 7.2, Ink, bold: header);
                x += widths[i];
            }

            LineAt(Margin, _y - rowHeight + 4, Margin + widths.Sum(), _y - rowHeight + 4, Line);
            _y -= rowHeight;
        }

        private void EnsureSpace(double height)
        {
            if (_y - height < ContentBottom)
            {
                NewPage();
            }
        }

        private void DrawText(string text, double x, double y, double size, string color, bool bold = false)
        {
            _page.Commands.AppendFormat(CultureInfo.InvariantCulture, "BT /{0} {1:0.##} Tf {2} rg {3:0.##} {4:0.##} Td ({5}) Tj ET\n",
                bold ? "F2" : "F1",
                size,
                color,
                x,
                y,
                PdfWriter.Escape(text));
        }

        private void DrawRoundedBox(double x, double y, double width, double height, string fill)
        {
            DrawRect(x, y, width, height, fill);
            LineAt(x, y, x + width, y, Line);
            LineAt(x, y + height, x + width, y + height, Line);
            LineAt(x, y, x, y + height, Line);
            LineAt(x + width, y, x + width, y + height, Line);
        }

        private void DrawRect(double x, double y, double width, double height, string fill)
        {
            _page.Commands.AppendFormat(CultureInfo.InvariantCulture, "{0} rg {1:0.##} {2:0.##} {3:0.##} {4:0.##} re f\n", fill, x, y, width, height);
        }

        private void LineAt(double x1, double y1, double x2, double y2, string color)
        {
            _page.Commands.AppendFormat(CultureInfo.InvariantCulture, "{0} RG 0.6 w {1:0.##} {2:0.##} m {3:0.##} {4:0.##} l S\n", color, x1, y1, x2, y2);
        }

        private static IEnumerable<string> Wrap(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                yield return "-";
                yield break;
            }

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = new StringBuilder();
            foreach (var word in words)
            {
                if (line.Length > 0 && line.Length + word.Length + 1 > maxChars)
                {
                    yield return line.ToString();
                    line.Clear();
                }

                if (line.Length > 0)
                {
                    line.Append(' ');
                }

                line.Append(word);
            }

            if (line.Length > 0)
            {
                yield return line.ToString();
            }
        }

        private static string Trim(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            return value.Length <= max ? value : value[..Math.Max(1, max - 1)] + "...";
        }

        private static string VerdictColor(ReportVerdict verdict) => verdict switch
        {
            ReportVerdict.Pass => "0.082 0.451 0.278",
            ReportVerdict.PassWithWarning => "0.541 0.353 0",
            ReportVerdict.Fail => "0.706 0.137 0.094",
            _ => Muted
        };

        private static string BuildReviewApproval(FatTestSessionSnapshot snapshot)
        {
            var reviewed = string.IsNullOrWhiteSpace(snapshot.Branding.ReviewedBy) ? "-" : snapshot.Branding.ReviewedBy;
            var approved = string.IsNullOrWhiteSpace(snapshot.Branding.ApprovedBy) ? "-" : snapshot.Branding.ApprovedBy;
            return $"Reviewed: {reviewed} / Approved: {approved}";
        }
    }

    private sealed class PdfPage
    {
        public StringBuilder Commands { get; } = new();
    }

    private static class PdfWriter
    {
        public static byte[] Write(IReadOnlyList<PdfPage> pages)
        {
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>"
            };

            var kids = Enumerable.Range(0, pages.Count).Select(i => $"{3 + (i * 2)} 0 R");
            objects.Add($"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {pages.Count} >>");

            for (var i = 0; i < pages.Count; i++)
            {
                var pageObject = 3 + (i * 2);
                var contentObject = pageObject + 1;
                objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> /F2 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >> >> >> /Contents {contentObject} 0 R >>");
                var stream = pages[i].Commands.ToString();
                objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}endstream");
            }

            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms, Encoding.ASCII, leaveOpen: true) { NewLine = "\n" };
            writer.Write("%PDF-1.4\n");
            var offsets = new List<long> { 0 };
            for (var i = 0; i < objects.Count; i++)
            {
                writer.Flush();
                offsets.Add(ms.Position);
                writer.Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }

            writer.Flush();
            var xref = ms.Position;
            writer.Write($"xref\n0 {objects.Count + 1}\n");
            writer.Write("0000000000 65535 f \n");
            foreach (var offset in offsets.Skip(1))
            {
                writer.Write($"{offset:0000000000} 00000 n \n");
            }

            writer.Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            writer.Flush();
            return ms.ToArray();
        }

        public static string Escape(string? value)
        {
            var normalized = NormalizeAscii(value ?? "-");
            return normalized
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("(", "\\(", StringComparison.Ordinal)
                .Replace(")", "\\)", StringComparison.Ordinal);
        }

        private static string NormalizeAscii(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                builder.Append(ch is >= ' ' and <= '~' ? ch : '?');
            }

            return builder.ToString();
        }
    }
}
