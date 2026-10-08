using PaperStager.Core;
using Xunit;

namespace PaperStager.Core.Tests;

public sealed class PlanningTests
{
    [Fact]
    public async Task SeparateRulesExtractFirstPageFieldsAndCreateNames()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        var plan = new PlanningService().CreatePlan(project);
        Assert.False(plan.HasErrors, string.Join("; ", plan.Documents.SelectMany(d => d.Errors)));
        Assert.Equal(["Acme_A-100.pdf", "Beta_B-200.pdf"], plan.Documents.Select(d => d.FileName));
        Assert.Equal("Acme", plan.Documents[0].Fields["customer"]);
        Assert.Equal("B-200", plan.Documents[1].Fields["reference"]);
    }

    [Fact]
    public void MissingRequiredValueBlocksReviewWithoutInventingAValue()
    {
        var project = ProjectWithText("No reference was supplied");
        var plan = new PlanningService().CreatePlan(project);
        Assert.True(plan.HasErrors);
        Assert.NotEmpty(Assert.Single(plan.Documents).Errors);
    }

    [Fact]
    public void ManualKoreanAndUnicodeCorrectionsOverrideExtractedValue()
    {
        var project = ProjectWithText("Reference: old");
        project.Documents[0].ManualFields["reference"] = "한글_거래처_é_2026";
        var plan = new PlanningService().CreatePlan(project);
        Assert.False(plan.HasErrors, string.Join("; ", plan.Documents.SelectMany(d => d.Errors)));
        Assert.Equal("한글_거래처_é_2026.pdf", Assert.Single(plan.Documents).FileName);
    }

    [Fact]
    public void MultilineManualValueBecomesOneVisibleFilenameLine()
    {
        var project = ProjectWithText("Reference: old");
        project.Documents[0].ManualFields["reference"] = "한글\r\n거래처\t2026";
        var plan = new PlanningService().CreatePlan(project);
        Assert.False(plan.HasErrors);
        Assert.Equal("한글 거래처 2026.pdf", Assert.Single(plan.Documents).FileName);
    }

    [Theory]
    [InlineData(@"Reference:\s*(?<value>[A-Z]-\d+)")]
    [InlineData(@"Reference:\s*([A-Z]-\d+)")]
    [InlineData(@"[A-Z]-\d+")]
    public void RegexSupportsNamedFirstAndWholeMatchCaptures(string expression)
    {
        var project = ProjectWithText("Reference: A-102");
        project.Template.Fields[0].Kind = FieldRuleKind.Regex;
        project.Template.Fields[0].Expression = expression;
        var plan = new PlanningService().CreatePlan(project);
        Assert.False(plan.HasErrors);
        Assert.Equal("A-102.pdf", Assert.Single(plan.Documents).FileName);
    }

    [Fact]
    public void InvalidRegexProducesReviewErrorInsteadOfCrashing()
    {
        var project = ProjectWithText("Reference: A-102");
        project.Template.Fields[0].Kind = FieldRuleKind.Regex;
        project.Template.Fields[0].Expression = "(?<unclosed";
        Assert.True(new PlanningService().CreatePlan(project).HasErrors);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("/absolute")]
    [InlineData("C:\\absolute")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("NUL")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    [InlineData("name.")]
    [InlineData("bad:name")]
    [InlineData("bad?name")]
    public void UnsafePortableNamesAreBlocked(string unsafeName)
    {
        var project = ProjectWithText("Reference: valid");
        project.Documents[0].ManualFields["reference"] = unsafeName;
        Assert.True(new PlanningService().CreatePlan(project).HasErrors);
    }

    [Theory]
    [InlineData("../{reference}")]
    [InlineData("folder/{reference}")]
    [InlineData("{unknown}")]
    public void UnsafeOrUnknownTemplateTokensAreBlocked(string pattern)
    {
        var project = ProjectWithText("Reference: A-100");
        project.Template.Pattern = pattern;
        Assert.True(new PlanningService().CreatePlan(project).HasErrors);
    }

    [Fact]
    public void CaseInsensitiveFilenameCollisionsAreVisibleOnEveryPlatform()
    {
        var project = ProjectWithText("Reference: invoice");
        project.Sources[0].Pages.Add(new PdfPageInfo { Number = 2, Text = "Reference: INVOICE", WidthPoints = 612, HeightPoints = 792 });
        project.Documents = DocumentService.Split(project.Sources[0], [1, 2]);
        var plan = new PlanningService().CreatePlan(project);
        Assert.True(plan.HasErrors);
        Assert.All(plan.Documents, d => Assert.NotEmpty(d.Errors));
    }

    [Fact]
    public void LaterPageTextCannotSupplyMissingFirstPageFields()
    {
        var project = ProjectWithText("No reference");
        project.Sources[0].Pages.Add(new PdfPageInfo { Number = 2, Text = "Reference: second-page", WidthPoints = 612, HeightPoints = 792 });
        project.Documents[0].EndPage = 2;
        Assert.True(new PlanningService().CreatePlan(project).HasErrors);
    }

    [Fact]
    public void SkippedDocumentDoesNotBlockExportForMissingFields()
    {
        var project = ProjectWithText("No reference");
        project.Sources[0].Pages.Add(new PdfPageInfo { Number = 2, Text = "Reference: valid", WidthPoints = 612, HeightPoints = 792 });
        project.Documents = DocumentService.Split(project.Sources[0], [1, 2]);
        project.Documents[0].Skip = true;
        project.Documents[0].SkipReason = "Unreadable synthetic page; rescan needed";
        var plan = new PlanningService().CreatePlan(project);
        Assert.False(plan.HasErrors);
        var document = plan.Documents[0];
        Assert.True(document.Skip);
        Assert.Equal(project.Documents[0].SkipReason, document.SkipReason);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 2)]
    public void OutOfRangeOrInvertedPagesAreBlocked(int start, int end)
    {
        var project = ProjectWithText("Reference: valid");
        project.Documents[0].StartPage = start;
        project.Documents[0].EndPage = end;
        Assert.True(new PlanningService().CreatePlan(project).HasErrors);
    }

    [Theory]
    [InlineData("Reference: A-100\nReference: B-200")]
    [InlineData("Reference: A-100\nReference: A-100")]
    public void RepeatedLabelsRequireManualReviewEvenIfTheValuesMatch(string text)
    {
        var project = ProjectWithText(text);
        Assert.True(new PlanningService().CreatePlan(project).HasErrors);
        project.Documents[0].ManualFields["reference"] = "confirmed";
        Assert.False(new PlanningService().CreatePlan(project).HasErrors);
    }

    [Fact]
    public void CanonicallyEquivalentUnicodeFilenamesCollide()
    {
        var project = ProjectWithText("Reference: placeholder");
        project.Sources[0].Pages.Add(new PdfPageInfo { Number = 2, Text = "Reference: placeholder", WidthPoints = 612, HeightPoints = 792 });
        project.Documents = DocumentService.Split(project.Sources[0], [1, 2]);
        project.Documents[0].ManualFields["reference"] = "café";
        project.Documents[1].ManualFields["reference"] = "cafe\u0301";
        var plan = new PlanningService().CreatePlan(project);
        Assert.True(plan.HasErrors);
        Assert.All(plan.Documents, d => Assert.NotEmpty(d.Errors));
    }

    [Fact]
    public void EverySourcePageMustBeAssignedOrExplicitlySkipped()
    {
        var project = ProjectWithText("Reference: A-100");
        project.Sources[0].Pages.Add(new PdfPageInfo { Number = 2, Text = "Continuation", WidthPoints = 612, HeightPoints = 792 });
        Assert.True(new PlanningService().CreatePlan(project).HasErrors);
    }

    [Fact]
    public void OverlappingPageRangesRequireReview()
    {
        var project = ProjectWithText("Reference: first");
        project.Sources[0].Pages.Add(new PdfPageInfo { Number = 2, Text = "Reference: second", WidthPoints = 612, HeightPoints = 792 });
        project.Documents = DocumentService.Split(project.Sources[0], [1, 2]);
        Assert.False(new PlanningService().CreatePlan(project).HasErrors);
        project.Documents[0].EndPage = 2;
        var plan = new PlanningService().CreatePlan(project);
        Assert.True(plan.HasErrors);
        Assert.NotEmpty(plan.Errors);
    }

    private static ReviewProject ProjectWithText(string text)
    {
        var source = new SourcePdf
        {
            Id = "synthetic-source", Path = Path.Combine(Path.GetTempPath(), "unused-synthetic.pdf"), Sha256 = new string('0', 64),
            Pages = [new PdfPageInfo { Number = 1, Text = text, WidthPoints = 612, HeightPoints = 792 }]
        };
        return new ReviewProject
        {
            Sources = [source], Documents = DocumentService.Split(source, [1]),
            Template = new NamingTemplate
            {
                Name = "Reference", Pattern = "{reference}",
                Fields = [new FieldRule { Name = "reference", Kind = FieldRuleKind.AfterLabel, Expression = "Reference:", Required = true }]
            }
        };
    }
}
