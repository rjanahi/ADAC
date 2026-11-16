
using DocumentSystemApi.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using System.Text.Json;

namespace DocumentSystemApi.Services;

//Information required to generate a PDF report
public class AnalysisPdfData
{
    public string DocumentTitle { get; set; } = "";
    public string AnalysisType { get; set; } = "";
    public int AnalysisVersion { get; set; }
    public DateTime GeneratedAt { get; set; }

    public string OverallAssessment { get; set; } = "";

    public int? TotalTests { get; set; }
    public int? PassedTests { get; set; }
    public int? FailedTests { get; set; }
    public int? BlockedTests { get; set; }
    public int? SkippedTests { get; set; }

    public List<string> KeyRisks { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public List<AnalysisPdfFinding> Findings { get; set; } = new();
}

public class AnalysisPdfFinding
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Severity { get; set; }
    public string? Category { get; set; }
    public string? Impact { get; set; }
    public string? Recommendation { get; set; }
    public string? SourceLocation { get; set; }
}

public class AnalysisPdfService
{

    private readonly DocumentDbContext _context;

    //Access the existing DocumentDB database
    public AnalysisPdfService(DocumentDbContext context)
    {
        _context = context;
    }


    //Retrieve the exact saved analysis selected by the user
    public async Task<AnalysisPdfData> GetAnalysisPdfDataAsync(
        Guid analysisId,
        Guid userId)
    {
        if (analysisId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException(
                "Invalid analysis or user identifier.");
        }

        //Retrieve the analysis only when its document
        //belongs to the authenticated user
        var analysis = await _context.DocumentAnalyses
            .Include(a => a.Document)
            .FirstOrDefaultAsync(a =>
                a.Id == analysisId &&
                a.Document.UserId == userId &&
                !a.Document.IsArchived);

        if (analysis == null)
        {
            throw new KeyNotFoundException(
                "Analysis not found.");
        }

        //Only completed analyses can be exported
        if (analysis.Status != "Completed")
        {
            throw new InvalidOperationException(
                "The selected analysis has not completed.");
        }

        //Retrieve findings belonging to this exact analysis
        var findings = await _context.DocumentFindings
            .AsNoTracking()
            .Where(f => f.AnalysisId == analysisId)
            .OrderBy(f => f.Title)
            .ToListAsync();

        //Convert database records into the PDF report model
        return new AnalysisPdfData
        {
            DocumentTitle = analysis.Document.Title,
            AnalysisType = analysis.AnalysisType,
            AnalysisVersion = analysis.Version,

            //This is the PDF export date, not the analysis date
            GeneratedAt = DateTime.UtcNow,

            OverallAssessment =
                analysis.OverallAssessment ?? "",

            TotalTests = analysis.TotalTests,
            PassedTests = analysis.PassedTests,
            FailedTests = analysis.FailedTests,
            BlockedTests = analysis.BlockedTests,
            SkippedTests = analysis.SkippedTests,

            KeyRisks = ReadJsonStringList(analysis.KeyRisks),
            Recommendations =
                ReadJsonStringList(analysis.Recommendations),

            Findings = findings.Select(f => new AnalysisPdfFinding
            {
                Title = f.Title,
                Description = f.Description,
                Severity = f.Severity,
                Category = f.Category,
                Impact = f.Impact,
                Recommendation = f.Recommendation,
                SourceLocation = f.SourceLocation
            }).ToList()
        };
    }


    //Convert stored JSON arrays into lists for the PDF
    private static List<string> ReadJsonStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            //Expect a JSON array of strings
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            var results = new List<string>();

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string? value = item.GetString();

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        results.Add(value);
                    }
                }
            }

            return results;
        }
        catch (JsonException)
        {
            //Malformed JSON cannot be rendered as a list
            return new List<string>();
        }
    }


    //Generate a PDF in memory
    public byte[] GeneratePdf(AnalysisPdfData analysis)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                //Page configuration
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.PageColor(Colors.White);

                page.DefaultTextStyle(style =>
                    style.FontSize(10)
                         .FontColor(Colors.Grey.Darken3));

                //Repeated page header
                page.Header()
                    .PaddingBottom(15)
                    .Column(column =>
                    {
                        column.Item()
                            .Text("DOCUMENT ANALYSIS REPORT")
                            .Bold()
                            .FontSize(16)
                            .FontColor(Colors.Blue.Darken3);

                        column.Item()
                            .Text(analysis.DocumentTitle);
                    });

                //Main report content
                page.Content()
                    .PaddingVertical(15)
                    .Column(column =>
                    {
                        column.Spacing(18);

                        //Report information
                        column.Item().Column(section =>
                        {
                            section.Spacing(5);

                            section.Item().Text(
                                $"Analysis Type: {analysis.AnalysisType}");

                            section.Item().Text(
                                $"Analysis Version: {analysis.AnalysisVersion}");

                            section.Item().Text(
                                $"Exported: {analysis.GeneratedAt:dd MMM yyyy}");
                        });

                        //Overall assessment
                        AddSection(
                            column,
                            "Overall Assessment",
                            analysis.OverallAssessment);

                        //QA-specific statistics
                        if (analysis.AnalysisType.Equals(
                            "QA",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            column.Item().Column(section =>
                            {
                                section.Spacing(6);

                                section.Item()
                                    .Text("Test Statistics")
                                    .Bold()
                                    .FontSize(13);

                                section.Item().Text(
                                    $"Total Tests: {FormatCount(analysis.TotalTests)}");

                                section.Item().Text(
                                    $"Passed: {FormatCount(analysis.PassedTests)}");

                                section.Item().Text(
                                    $"Failed: {FormatCount(analysis.FailedTests)}");

                                section.Item().Text(
                                    $"Blocked: {FormatCount(analysis.BlockedTests)}");

                                section.Item().Text(
                                    $"Skipped: {FormatCount(analysis.SkippedTests)}");
                            });
                        }

                        //Key risks
                        AddListSection(
                            column,
                            "Key Risks",
                            analysis.KeyRisks);

                        //Recommendations
                        AddListSection(
                            column,
                            "Recommendations",
                            analysis.Recommendations);

                        //Detailed findings
                        column.Item()
                            .Text("Detailed Findings")
                            .Bold()
                            .FontSize(13);

                        if (analysis.Findings.Count == 0)
                        {
                            column.Item().Text(
                                "No findings recorded.");
                        }

                        foreach (var finding in analysis.Findings)
                        {
                            column.Item()
                                .Border(1)
                                .BorderColor(Colors.Grey.Lighten2)
                                .Padding(12)
                                .Column(section =>
                                {
                                    section.Spacing(6);

                                    section.Item()
                                        .Text(finding.Title)
                                        .Bold()
                                        .FontSize(11);

                                    section.Item()
                                        .Text(finding.Description);

                                    AddOptionalField(
                                        section,
                                        "Severity",
                                        finding.Severity);

                                    AddOptionalField(
                                        section,
                                        "Category",
                                        finding.Category);

                                    AddOptionalField(
                                        section,
                                        "Impact",
                                        finding.Impact);

                                    AddOptionalField(
                                        section,
                                        "Recommendation",
                                        finding.Recommendation);

                                    AddOptionalField(
                                        section,
                                        "Source",
                                        finding.SourceLocation);
                                });
                        }
                    });

                //Repeated page footer
                page.Footer()
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
            });
        });

        //Return PDF bytes without saving a file
        return document.GeneratePdf();
    }

    private static string FormatCount(int? count)
    {
        return count?.ToString() ?? "Not recorded";
    }

    //Add a titled text section
    private static void AddSection(
        ColumnDescriptor column,
        string title,
        string content)
    {
        column.Item().Column(section =>
        {
            section.Spacing(6);

            section.Item()
                .Text(title)
                .Bold()
                .FontSize(13);

            section.Item()
                .Text(string.IsNullOrWhiteSpace(content)
                    ? "Not recorded."
                    : content);
        });
    }

    //Add a list of risks or recommendations
    private static void AddListSection(
        ColumnDescriptor column,
        string title,
        List<string> items)
    {
        column.Item().Column(section =>
        {
            section.Spacing(6);

            section.Item()
                .Text(title)
                .Bold()
                .FontSize(13);

            if (items.Count == 0)
            {
                section.Item().Text("None recorded.");
                return;
            }

            foreach (string item in items)
            {
                section.Item().Text($"• {item}");
            }
        });
    }

    //Add a finding field only when information exists
    private static void AddOptionalField(
        ColumnDescriptor column,
        string label,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            column.Item().Text(text =>
            {
                text.Span($"{label}: ").Bold();
                text.Span(value);
            });
        }
    }


    //Generate a PDF for a specific saved analysis
    public async Task<byte[]> ExportAnalysisAsync(
        Guid analysisId,
        Guid userId)
    {
        //Retrieve the selected analysis and verify ownership
        AnalysisPdfData analysis =
            await GetAnalysisPdfDataAsync(
                analysisId,
                userId);

        //Generate the PDF using the saved analysis data
        byte[] pdfBytes = GeneratePdf(analysis);

        return pdfBytes;
    }

}
