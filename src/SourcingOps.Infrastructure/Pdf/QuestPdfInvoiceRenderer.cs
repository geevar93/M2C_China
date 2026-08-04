using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;

namespace SourcingOps.Infrastructure.Pdf;

/// <summary>
/// QuestPDF implementation of <see cref="IInvoicePdfRenderer"/> (M6 contract DR-3 — settled,
/// not re-opened here). Pure managed C#, no native binary and no headless browser, which is
/// the deciding factor against a single 4 vCPU / 8 GB VPS with a minimal container count
/// (TECH_SPEC §7.2/§7.3, constraint C1). The Community licence line lives ONCE, in
/// <c>DependencyInjection.AddInfrastructure</c> — never scattered across call sites.
///
/// A plain single-page A4 layout: header (issuer block + invoice number/date), bill-to,
/// one line item, computed total, and an optional bank-details footer. There is no line-item
/// table because a lightweight invoice (FSD §6.8) carries exactly one line by design
/// (<c>InvoiceDetailDto.LineDescription</c> is a single string, not a collection).
/// </summary>
public sealed class QuestPdfInvoiceRenderer : IInvoicePdfRenderer
{
    public byte[] Render(InvoicePdfModel model)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text(model.LegalEntityName).FontSize(16).Bold();
                    col.Item().Text(model.RegisteredAddress);
                    if (!string.IsNullOrWhiteSpace(model.Gstin))
                    {
                        col.Item().Text($"GSTIN: {model.Gstin}");
                    }
                    col.Item().PaddingTop(10).LineHorizontal(1);
                });

                page.Content().PaddingTop(15).Column(col =>
                {
                    col.Spacing(8);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Invoice No: {model.InvoiceNumber}").Bold();
                        row.RelativeItem().AlignRight().Text($"Date: {model.InvoiceDate:yyyy-MM-dd}");
                    });

                    if (!string.IsNullOrWhiteSpace(model.ShipmentReference))
                    {
                        col.Item().Text($"Shipment Ref: {model.ShipmentReference}");
                    }

                    col.Item().PaddingTop(10).Text("Bill To").Bold();
                    col.Item().Text(model.CustomerName);
                    var location = string.Join(", ", new[] { model.CustomerCity, model.CustomerRegion }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (!string.IsNullOrWhiteSpace(location))
                    {
                        col.Item().Text(location);
                    }
                    if (!string.IsNullOrWhiteSpace(model.CustomerGstin))
                    {
                        col.Item().Text($"GSTIN: {model.CustomerGstin}");
                    }

                    col.Item().PaddingTop(15).LineHorizontal(0.5f);

                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(1);
                        });

                        table.Cell().Text("Description");
                        table.Cell().AlignRight().Text("Amount");

                        table.Cell().Text(string.IsNullOrWhiteSpace(model.LineDescription) ? "Services rendered" : model.LineDescription);
                        table.Cell().AlignRight().Text(MoneyFormatter.Format(model.Currency, model.Amount));

                        table.Cell().Text("Tax");
                        table.Cell().AlignRight().Text(MoneyFormatter.Format(model.Currency, model.TaxAmount));

                        table.Cell().PaddingTop(4).Text("Total").Bold();
                        table.Cell().PaddingTop(4).AlignRight().Text(MoneyFormatter.Format(model.Currency, model.TotalAmount)).Bold();
                    });

                    if (model.BankDetails is not null)
                    {
                        col.Item().PaddingTop(20).Text("Bank Details").Bold();
                        col.Item().Text($"Account Name: {model.BankDetails.AccountName}");
                        col.Item().Text($"Account Number: {model.BankDetails.AccountNumber}");
                        col.Item().Text($"IFSC: {model.BankDetails.Ifsc}");
                        if (!string.IsNullOrWhiteSpace(model.BankDetails.Branch))
                        {
                            col.Item().Text($"Branch: {model.BankDetails.Branch}");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(model.DeclarationText))
                    {
                        col.Item().PaddingTop(20).Text(model.DeclarationText).FontSize(8).Italic();
                    }
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" of ");
                    x.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }
}
