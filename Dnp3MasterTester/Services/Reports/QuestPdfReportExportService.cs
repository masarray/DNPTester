using System.IO;
using Dnp3MasterTester.Models.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Dnp3MasterTester.Services.Reports;

public sealed class QuestPdfReportExportService
{
    private const string Ink = "#142432";
    private const string Muted = "#5F6E7C";
    private const string Accent = "#0F6B7A";
    private const string Line = "#DDE4EC";
    private const string Soft = "#F1F6F7";

    public QuestPdfReportExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

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
        Document.Create(container => ComposeDocument(container, snapshot)).GeneratePdf(path);
    }

    private static void ComposeDocument(IDocumentContainer container, FatTestSessionSnapshot snapshot)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(34);
            page.DefaultTextStyle(text => text.FontFamily("Segoe UI").FontSize(9).FontColor(Ink));

            page.Header().Element(content => ComposeHeader(content, snapshot));
            page.Content().PaddingTop(18).Column(column =>
            {
                column.Spacing(14);
                ComposeCover(column, snapshot);
                column.Item().PageBreak();
                ComposeExecutiveSummary(column, snapshot);
                ComposeFatMatrix(column, snapshot);
                ComposeFatItemDetails(column, snapshot);
                column.Item().PageBreak();
                ComposeCommunicationAudit(column, snapshot);
                ComposePointAudit(column, snapshot);
                column.Item().PageBreak();
                ComposeEventAudit(column, snapshot);
                ComposeCommandAudit(column, snapshot);
                column.Item().PageBreak();
                ComposeAppendix(column, snapshot);
            });
            page.Footer().Element(content => ComposeFooter(content, snapshot));
        });
    }

    private static void ComposeHeader(IContainer container, FatTestSessionSnapshot snapshot)
    {
        container.BorderBottom(1).BorderColor(Line).PaddingBottom(7).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text(snapshot.ReportId).FontSize(10).SemiBold().FontColor(Accent);
                column.Item().Text(snapshot.Branding.ProjectName).FontSize(8).FontColor(Muted);
            });
            row.ConstantItem(150).AlignRight().Text(snapshot.EvidenceStateText).FontSize(7.5f).FontColor(Muted);
        });
    }

    private static void ComposeFooter(IContainer container, FatTestSessionSnapshot snapshot)
    {
        container.BorderTop(1).BorderColor(Line).PaddingTop(7).Row(row =>
        {
            row.RelativeItem().Text(snapshot.Branding.FooterText).FontSize(8).FontColor(Muted);
            row.ConstantItem(90).AlignRight().Text(text =>
            {
                text.Span("Page ").FontSize(8).FontColor(Muted);
                text.CurrentPageNumber().FontSize(8).FontColor(Muted);
                text.Span(" / ").FontSize(8).FontColor(Muted);
                text.TotalPages().FontSize(8).FontColor(Muted);
            });
        });
    }

    private static void ComposeCover(ColumnDescriptor column, FatTestSessionSnapshot snapshot)
    {
        column.Item().BorderBottom(1).BorderColor(Line).PaddingBottom(16).Row(row =>
        {
            row.RelativeItem().Column(identity =>
            {
                identity.Item().Text(snapshot.ReportId).FontSize(15).SemiBold().FontColor(Accent);
                identity.Item().Text(snapshot.Branding.ProjectName).FontSize(9).FontColor(Muted);
            });

            if (HasLogo(snapshot.Branding.CompanyLogoPath))
            {
                row.ConstantItem(86).Height(34).Element(content => ComposeLogo(content, snapshot.Branding.CompanyLogoPath));
            }

            if (HasLogo(snapshot.Branding.CustomerLogoPath))
            {
                row.ConstantItem(86).Height(34).PaddingLeft(10).Element(content => ComposeLogo(content, snapshot.Branding.CustomerLogoPath));
            }
        });
        column.Item().PaddingTop(52).Text("DNP3 Interoperability Test Report").FontSize(26).SemiBold().FontColor(Ink);
        column.Item().PaddingTop(8).Text($"{snapshot.Branding.CompanyName} / {snapshot.Branding.CustomerName}").FontSize(11).FontColor(Muted);
        column.Item().PaddingTop(18).Element(content => SummaryBand(content, snapshot));

        column.Item().PaddingTop(24).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
            });
            InfoCard(table, "Customer", snapshot.Branding.CustomerName);
            InfoCard(table, "Project", snapshot.Branding.ProjectName);
            InfoCard(table, "DUT / Target", snapshot.ConnectionTarget);
            InfoCard(table, "Connection", snapshot.ConnectionProfile);
            InfoCard(table, "Point Profile", snapshot.PointCatalogProfileName);
            InfoCard(table, "Generated", snapshot.GeneratedAtText);
            InfoCard(table, "Finalized", snapshot.FinalizedAtText);
            InfoCard(table, "Prepared By", snapshot.Branding.PreparedBy);
            InfoCard(table, "Reviewed / Approved", BuildReviewApproval(snapshot));
        });
    }

    private static void ComposeExecutiveSummary(ColumnDescriptor column, FatTestSessionSnapshot snapshot)
    {
        SectionTitle(column, "Executive Summary");
        column.Item().Element(content => SummaryBand(content, snapshot));
        column.Item().Text("FAT completion and technical result are intentionally separated. Open or not-tested items reduce completion, but they do not imply technical failure unless a failed evidence result exists.")
            .FontColor(Muted);
        if (snapshot.Observations.Count > 0)
        {
            column.Item().Border(1).BorderColor(Line).Padding(12).Column(observations =>
            {
                observations.Spacing(5);
                observations.Item().Text("Engineering Observations").FontSize(11).SemiBold().FontColor(Ink);
                foreach (var observation in snapshot.Observations.Take(6))
                {
                    observations.Item().Text($"- {observation}").FontSize(8.2f).FontColor(Muted);
                }
            });
        }
    }

    private static void ComposeFatMatrix(ColumnDescriptor column, FatTestSessionSnapshot snapshot)
    {
        SectionTitle(column, "FAT Result Matrix");
        column.Item().Text("Each row is derived from explicit evidence recognition rules. Items remain NOT TESTED when the required evidence is absent or the workflow is not yet supported by the application.")
            .FontSize(8.5f)
            .FontColor(Muted);
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(52);
                columns.RelativeColumn(2.2f);
                columns.RelativeColumn(3.4f);
                columns.ConstantColumn(112);
                columns.ConstantColumn(50);
            });

            HeaderCell(table, "Item");
            HeaderCell(table, "Test");
            HeaderCell(table, "Evidence Summary");
            HeaderCell(table, "Verdict");
            HeaderCell(table, "Ev.");

            foreach (var item in snapshot.FatItems)
            {
                BodyCell(table, item.ItemCode);
                BodyCell(table, item.Title);
                BodyCell(table, item.EvidenceSummary);
                BodyCell(table, item.VerdictText);
                BodyCell(table, item.EvidenceCount.ToString(), alignRight: true);
            }
        });
    }

    private static void ComposeFatItemDetails(ColumnDescriptor column, FatTestSessionSnapshot snapshot)
    {
        SectionTitle(column, "FAT Test Method & Recognition");
        column.Item().Text("This section explains how each FAT item is tested and how the app recognizes whether evidence exists. Items that require a future dedicated workflow are explicitly identified instead of being silently inferred.")
            .FontSize(8.5f)
            .FontColor(Muted);

        foreach (var item in snapshot.FatItems)
        {
            column.Item().Border(1).BorderColor(Line).Padding(10).Column(card =>
            {
                card.Spacing(5);
                card.Item().Row(row =>
                {
                    row.ConstantItem(58).Text(item.ItemCode).FontSize(10).SemiBold().FontColor(Accent);
                    row.RelativeItem().Text(item.Title).FontSize(10).SemiBold().FontColor(Ink);
                    row.ConstantItem(118).AlignRight().Text(item.VerdictText).FontSize(8).SemiBold().FontColor(ResolveVerdictColor(item.Verdict));
                });
                card.Item().Text(item.Objective).FontSize(8.2f).FontColor(Muted);
                KeyValueLine(card, "How to test", item.TestMethod);
                KeyValueLine(card, "Required evidence", item.RequiredEvidence);
                KeyValueLine(card, "Recognition rule", item.RecognitionRule);
                KeyValueLine(card, "Current rationale", item.Rationale);
            });
        }
    }

    private static void ComposeCommunicationAudit(ColumnDescriptor column, FatTestSessionSnapshot snapshot)
    {
        SectionTitle(column, "Communication Audit");
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
            });
            InfoCard(table, "State", snapshot.ConnectionState);
            InfoCard(table, "Detail", snapshot.ConnectionDetail);
            InfoCard(table, "Transport / Address", snapshot.ConnectionProfile);
            InfoCard(table, "Polling", snapshot.PollingProfile);
        });

        EvidenceTable(column, "Protocol Trace Extract", ["Time", "Level", "Direction", "Summary"], snapshot.TraceEvidence.Take(32).Select(x => new[]
        {
            x.TimestampLocal.ToString("HH:mm:ss.fff"),
            x.Level,
            x.Direction,
            x.Summary
        }));
    }

    private static void ComposePointAudit(ColumnDescriptor column, FatTestSessionSnapshot snapshot)
    {
        SectionTitle(column, "Point Read / Value Audit");
        EvidenceTable(column, "Point Evidence Extract", ["Type", "Index", "Point", "Value", "Quality", "Timestamp"], snapshot.PointEvidence.Take(42).Select(x => new[]
        {
            x.PointType,
            x.Index.ToString(),
            x.PointLabel,
            x.Value,
            x.Quality,
            x.SourceTimestamp
        }));
    }

    private static void ComposeEventAudit(ColumnDescriptor column, FatTestSessionSnapshot snapshot)
    {
        SectionTitle(column, "Event / SOE Audit");
        EvidenceTable(column, "SCADA Event Evidence", ["Time", "Event", "Point", "Value", "Status"], snapshot.EventEvidence.Take(34).Select(x => new[]
        {
            x.TimestampLocal.ToString("HH:mm:ss.fff"),
            x.EvidenceType,
            x.PointLabel,
            x.Value,
            x.Status
        }));

        EvidenceTable(column, "SOE Timestamp Evidence", ["Received", "Type", "Point", "Value", "Timestamp"], snapshot.SoeEvidence.Take(34).Select(x => new[]
        {
            x.TimestampLocal.ToString("HH:mm:ss.fff"),
            x.EvidenceType,
            x.PointLabel,
            x.Value,
            x.Status
        }));
    }

    private static void ComposeCommandAudit(ColumnDescriptor column, FatTestSessionSnapshot snapshot)
    {
        SectionTitle(column, "Command Audit");
        EvidenceTable(column, "Command Lifecycle Evidence", ["Transaction", "Point", "Mode", "Operation", "Acceptance", "Feedback", "Verdict"], snapshot.CommandEvidence.Select(x => new[]
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

    private static void ComposeAppendix(ColumnDescriptor column, FatTestSessionSnapshot snapshot)
    {
        SectionTitle(column, "Appendix");
        column.Item().Text("Appendix evidence is limited in the printable report to keep the document readable. The snapshot model retains structured evidence for later full evidence export.")
            .FontColor(Muted);
        EvidenceTable(column, "FAT Item Rationale", ["Item", "Objective", "Criteria", "Rationale"], snapshot.FatItems.Select(x => new[]
        {
            x.ItemCode,
            x.Objective,
            x.AcceptanceCriteria,
            x.Rationale
        }));
    }

    private static void SummaryBand(IContainer container, FatTestSessionSnapshot snapshot)
    {
        container.Background(Soft).Border(1).BorderColor(Line).Padding(16).Row(row =>
        {
            SummaryMetric(row, "FAT Status", snapshot.FatExecutionStatus);
            SummaryMetric(row, "Technical Result", snapshot.TechnicalResult);
            SummaryMetric(row, "Executed", $"{snapshot.ExecutedItemCount}/{snapshot.FatItems.Count}");
            SummaryMetric(row, "Failed", snapshot.FailedItemCount.ToString());
            SummaryMetric(row, "Open", snapshot.OpenItemCount.ToString());
        });
    }

    private static void SummaryMetric(RowDescriptor row, string label, string value)
    {
        row.RelativeItem().Column(column =>
        {
            column.Item().Text(label.ToUpperInvariant()).FontSize(7).FontColor(Muted);
            column.Item().Text(value).FontSize(10).SemiBold().FontColor(Ink);
        });
    }

    private static void InfoCard(TableDescriptor table, string label, string value)
    {
        table.Cell().Padding(5).Border(1).BorderColor(Line).Padding(10).Column(column =>
        {
            column.Item().Text(label.ToUpperInvariant()).FontSize(7).FontColor(Muted);
            column.Item().Text(string.IsNullOrWhiteSpace(value) ? "-" : value).FontSize(9).SemiBold().FontColor(Ink);
        });
    }

    private static void SectionTitle(ColumnDescriptor column, string title)
    {
        column.Item().PaddingTop(8).Text(title).FontSize(16).SemiBold().FontColor(Accent);
    }

    private static void EvidenceTable(ColumnDescriptor column, string title, string[] headers, IEnumerable<string[]> rows)
    {
        column.Item().PaddingTop(8).Text(title).FontSize(11).SemiBold().FontColor(Ink);
        var materialized = rows.ToArray();

        if (materialized.Length == 0)
        {
            column.Item().Border(1).BorderColor(Line).Padding(10).Text("No evidence captured for this section yet.").FontColor(Muted);
            return;
        }

        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns => ConfigureEvidenceColumns(columns, headers));

            foreach (var header in headers)
            {
                HeaderCell(table, header);
            }

            for (var rowIndex = 0; rowIndex < materialized.Length; rowIndex++)
            {
                foreach (var cell in materialized[rowIndex].Take(headers.Length))
                {
                    BodyCell(table, cell, rowIndex: rowIndex);
                }
            }
        });
    }

    private static void HeaderCell(TableDescriptor table, string value)
    {
        table.Cell().Element(HeaderCellStyle).Text(value).FontSize(7).SemiBold().FontColor(Ink);
    }

    private static void BodyCell(TableDescriptor table, string value, bool alignRight = false, int rowIndex = 0)
    {
        var text = table.Cell().Element(container => BodyCellStyle(container, rowIndex)).Text(string.IsNullOrWhiteSpace(value) ? "-" : value).FontSize(7.4f).FontColor(Ink);
        if (alignRight)
        {
            text.AlignRight();
        }
    }

    private static IContainer HeaderCellStyle(IContainer container) =>
        container.Background("#EEF4F6").BorderBottom(1).BorderColor(Line).PaddingVertical(6).PaddingHorizontal(5);

    private static IContainer BodyCellStyle(IContainer container, int rowIndex) =>
        container.Background(rowIndex % 2 == 0 ? Colors.White : "#FAFCFD").BorderBottom(0.5f).BorderColor(Line).PaddingVertical(5).PaddingHorizontal(5);

    private static void ConfigureEvidenceColumns(TableColumnsDefinitionDescriptor columns, string[] headers)
    {
        var signature = string.Join("|", headers);
        switch (signature)
        {
            case "Time|Level|Direction|Summary":
                columns.ConstantColumn(66);
                columns.ConstantColumn(48);
                columns.ConstantColumn(58);
                columns.RelativeColumn(1);
                break;
            case "Type|Index|Point|Value|Quality|Timestamp":
                columns.ConstantColumn(76);
                columns.ConstantColumn(34);
                columns.RelativeColumn(1.7f);
                columns.ConstantColumn(64);
                columns.ConstantColumn(72);
                columns.ConstantColumn(102);
                break;
            case "Time|Event|Point|Value|Status":
            case "Received|Type|Point|Value|Timestamp":
                columns.ConstantColumn(66);
                columns.ConstantColumn(84);
                columns.RelativeColumn(1.6f);
                columns.ConstantColumn(64);
                columns.ConstantColumn(92);
                break;
            case "Transaction|Point|Mode|Operation|Acceptance|Feedback|Verdict":
                columns.ConstantColumn(70);
                columns.RelativeColumn(1.4f);
                columns.ConstantColumn(64);
                columns.ConstantColumn(62);
                columns.ConstantColumn(76);
                columns.ConstantColumn(76);
                columns.ConstantColumn(76);
                break;
            case "Item|Objective|Criteria|Rationale":
                columns.ConstantColumn(45);
                columns.RelativeColumn(1.2f);
                columns.RelativeColumn(1.2f);
                columns.RelativeColumn(1.4f);
                break;
            default:
                foreach (var _ in headers)
                {
                    columns.RelativeColumn();
                }
                break;
        }
    }

    private static void KeyValueLine(ColumnDescriptor column, string label, string value)
    {
        column.Item().Text(text =>
        {
            text.Span($"{label}: ").FontSize(7.8f).SemiBold().FontColor(Ink);
            text.Span(string.IsNullOrWhiteSpace(value) ? "-" : value).FontSize(7.8f).FontColor(Muted);
        });
    }

    private static string ResolveVerdictColor(ReportVerdict verdict) => verdict switch
    {
        ReportVerdict.Pass => "#157347",
        ReportVerdict.PassWithWarning => "#8A5A00",
        ReportVerdict.Fail => "#B42318",
        ReportVerdict.Inconclusive => "#4A5568",
        _ => "#6B7280"
    };

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '-' : ch));
    }

    private static bool HasLogo(string path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static void ComposeLogo(IContainer container, string path)
    {
        container.AlignMiddle().AlignCenter().Image(path).FitArea();
    }

    private static string BuildReviewApproval(FatTestSessionSnapshot snapshot)
    {
        var reviewed = string.IsNullOrWhiteSpace(snapshot.Branding.ReviewedBy) ? "-" : snapshot.Branding.ReviewedBy;
        var approved = string.IsNullOrWhiteSpace(snapshot.Branding.ApprovedBy) ? "-" : snapshot.Branding.ApprovedBy;
        return $"Reviewed: {reviewed} / Approved: {approved}";
    }
}
