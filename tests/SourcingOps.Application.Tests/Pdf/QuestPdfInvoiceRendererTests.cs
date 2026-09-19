using FluentAssertions;
using SourcingOps.Application.Interfaces;
using SourcingOps.Infrastructure.Pdf;

namespace SourcingOps.Application.Tests.Pdf;

/// <summary>
/// N-37 (buyer GSTIN on the invoice PDF's "Bill To" block). QuestPDF embeds a subsetted font
/// and encodes text via CID glyph indices in a compressed content stream — there is no plain
/// ASCII "GSTIN: ..." substring to grep for in the output bytes, and adding a PDF text-extraction
/// library is out of scope (no new NuGet packages). These tests instead verify the renderer's
/// observable behaviour structurally: QuestPDF's output is deterministic for identical input
/// (no embedded timestamps), so (a) adding a GSTIN line measurably changes the rendered byte
/// stream relative to an otherwise-identical model, and (b) a null GSTIN and a
/// whitespace-only GSTIN render BYTE-IDENTICAL output — proving the
/// <c>!string.IsNullOrWhiteSpace</c> guard in <see cref="QuestPdfInvoiceRenderer"/> really does
/// suppress the line entirely (no stray empty "GSTIN:" text item) rather than merely rendering
/// it blank.
/// </summary>
public class QuestPdfInvoiceRendererTests
{
    // The Community licence line is normally set once, in DependencyInjection.AddInfrastructure
    // (QuestPdfInvoiceRenderer's own doc comment). This suite instantiates the renderer directly,
    // bypassing that composition root, so the static ctor sets it here instead — QuestPDF throws
    // at render time otherwise.
    static QuestPdfInvoiceRendererTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    private static InvoicePdfModel BaseModel(string? customerGstin) => new(
        InvoiceNumber: "INV-2608-001",
        InvoiceDate: new DateOnly(2026, 8, 1),
        CustomerName: "Meena Traders",
        CustomerCity: "Surat",
        CustomerRegion: "Gujarat",
        CustomerGstin: customerGstin,
        LineDescription: "Consulting services",
        Amount: 1000m,
        TaxAmount: 180m,
        TotalAmount: 1180m,
        Currency: "INR",
        ShipmentReference: null,
        LegalEntityName: "M2C Sourcing Pvt Ltd",
        RegisteredAddress: "123 Industrial Estate, Surat, Gujarat",
        Gstin: null, // seller GSTIN — deliberately not set here, this suite is about the buyer's
        BankDetails: null,
        DeclarationText: null,
        Lines: [new InvoicePdfLine("Consulting services", "998311", 1m, 1000m, 18m, 1000m, 0m, 0m, 180m, 1180m)],
        TaxSummary: new InvoicePdfTaxSummary("27", "Maharashtra", IsIntraState: false, 1000m, 0m, 0m, 180m, 180m));

    [Fact]
    public void Render_CustomerWithGstin_ProducesDifferentOutputThanWithout()
    {
        var sut = new QuestPdfInvoiceRenderer();

        var withGstin = sut.Render(BaseModel("27ABCDE1234F1Z5"));
        var withoutGstin = sut.Render(BaseModel(null));

        withGstin.Should().NotBeEquivalentTo(withoutGstin,
            "adding the buyer's GSTIN line to the Bill To block must change the rendered document");
    }

    [Fact]
    public void Render_CustomerGstinNullVsBlank_ProducesByteIdenticalOutput()
    {
        var sut = new QuestPdfInvoiceRenderer();

        var withNull = sut.Render(BaseModel(null));
        var withBlank = sut.Render(BaseModel("   "));

        withBlank.Should().BeEquivalentTo(withNull,
            "a blank GSTIN must be treated exactly like an absent one — no stray empty \"GSTIN:\" line");
    }

    [Fact]
    public void Render_WithLineDiscount_AddsTheDiscountColumnAndTotals()
    {
        var sut = new QuestPdfInvoiceRenderer();
        var discounted = BaseModel(null) with
        {
            Amount = 900m,
            TaxAmount = 162m,
            TotalAmount = 1062m,
            Lines = [new InvoicePdfLine("Consulting services", "998311", 1m, 1000m, 18m, 900m, 0m, 0m, 162m, 1062m, DiscountAmount: 100m)],
            TaxSummary = new InvoicePdfTaxSummary("27", "Maharashtra", IsIntraState: false, 900m, 0m, 0m, 162m, 162m, TotalDiscount: 100m)
        };

        var bytes = sut.Render(discounted);

        bytes.Should().NotBeEmpty();
        bytes.Should().NotBeEquivalentTo(sut.Render(BaseModel(null)));
    }

    [Fact]
    public void Render_RupeeSymbol_IsAvailableInTheBundledFont_WithoutSystemFonts()
    {
        // The API container (Alpine) has no system fonts, so ₹ must come from QuestPDF's
        // bundled Lato. On a dev box a missing glyph would silently fall back to a Windows
        // font; switching environment fonts off reproduces the container.
        var previousEnvironmentFonts = QuestPDF.Settings.UseEnvironmentFonts;
        var previousGlyphCheck = QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable;
        try
        {
            QuestPDF.Settings.UseEnvironmentFonts = false;
            QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = true;

            var act = () => new QuestPdfInvoiceRenderer().Render(BaseModel(null));

            act.Should().NotThrow("every amount on the invoice is printed with the ₹ symbol");
        }
        finally
        {
            QuestPDF.Settings.UseEnvironmentFonts = previousEnvironmentFonts;
            QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = previousGlyphCheck;
        }
    }
}
