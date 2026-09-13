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

                    var intra = model.TaxSummary.IsIntraState;

                    // Place of supply is stated explicitly: it is what justifies the tax heads
                    // below, and a reader (or an auditor) should not have to infer it.
                    if (!string.IsNullOrWhiteSpace(model.TaxSummary.PlaceOfSupplyStateName))
                    {
                        col.Item().PaddingTop(6).Text(
                            $"Place of Supply: {model.TaxSummary.PlaceOfSupplyStateName} ({model.TaxSummary.PlaceOfSupplyStateCode})" +
                            $" — {(intra ? "Intra-state (CGST + SGST)" : "Inter-state (IGST)")}")
                            .FontSize(9);
                    }

                    col.Item().PaddingTop(10).Table(table =>
                    {
                        // Only the heads that apply get columns — see InvoicePdfTaxSummary's
                        // doc on why zero-filled CGST/SGST columns on an IGST invoice are worse
                        // than absent ones.
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(4);   // Description
                            c.RelativeColumn(1.2f); // HSN/SAC
                            c.RelativeColumn(1);   // Qty
                            c.RelativeColumn(1.4f); // Rate
                            c.RelativeColumn(1.6f); // Taxable
                            if (intra)
                            {
                                c.RelativeColumn(1.4f); // CGST
                                c.RelativeColumn(1.4f); // SGST
                            }
                            else
                            {
                                c.RelativeColumn(1.6f); // IGST
                            }
                            c.RelativeColumn(1.8f); // Total
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Description").Bold().FontSize(9);
                            header.Cell().Text("HSN/SAC").Bold().FontSize(9);
                            header.Cell().AlignRight().Text("Qty").Bold().FontSize(9);
                            header.Cell().AlignRight().Text("Rate").Bold().FontSize(9);
                            header.Cell().AlignRight().Text("Taxable").Bold().FontSize(9);
                            if (intra)
                            {
                                header.Cell().AlignRight().Text("CGST").Bold().FontSize(9);
                                header.Cell().AlignRight().Text("SGST").Bold().FontSize(9);
                            }
                            else
                            {
                                header.Cell().AlignRight().Text("IGST").Bold().FontSize(9);
                            }
                            header.Cell().AlignRight().Text("Total").Bold().FontSize(9);
                        });

                        foreach (var line in model.Lines)
                        {
                            table.Cell().PaddingVertical(2).Text(line.Description).FontSize(9);
                            table.Cell().PaddingVertical(2).Text(line.HsnCode ?? "—").FontSize(9);
                            table.Cell().PaddingVertical(2).AlignRight().Text(QuantityFormatter.Format(line.Quantity)).FontSize(9);
                            table.Cell().PaddingVertical(2).AlignRight().Text($"{line.GstRate:0.##}%").FontSize(9);
                            table.Cell().PaddingVertical(2).AlignRight().Text(MoneyFormatter.Format(model.Currency, line.TaxableValue)).FontSize(9);
                            if (intra)
                            {
                                table.Cell().PaddingVertical(2).AlignRight().Text(MoneyFormatter.Format(model.Currency, line.CgstAmount)).FontSize(9);
                                table.Cell().PaddingVertical(2).AlignRight().Text(MoneyFormatter.Format(model.Currency, line.SgstAmount)).FontSize(9);
                            }
                            else
                            {
                                table.Cell().PaddingVertical(2).AlignRight().Text(MoneyFormatter.Format(model.Currency, line.IgstAmount)).FontSize(9);
                            }
                            table.Cell().PaddingVertical(2).AlignRight().Text(MoneyFormatter.Format(model.Currency, line.LineTotal)).FontSize(9);
                        }
                    });

                    col.Item().PaddingTop(10).LineHorizontal(0.5f);

                    col.Item().PaddingTop(6).AlignRight().Table(totals =>
                    {
                        totals.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(150);
                            c.ConstantColumn(120);
                        });

                        void Row(string label, decimal value, bool bold = false)
                        {
                            var l = totals.Cell().PaddingVertical(1).Text(label).FontSize(9);
                            var v = totals.Cell().PaddingVertical(1).AlignRight().Text(MoneyFormatter.Format(model.Currency, value)).FontSize(9);
                            if (bold)
                            {
                                l.Bold();
                                v.Bold();
                            }
                        }

                        Row("Taxable Value", model.TaxSummary.TaxableValue);
                        if (intra)
                        {
                            Row("CGST", model.TaxSummary.CgstAmount);
                            Row("SGST", model.TaxSummary.SgstAmount);
                        }
                        else
                        {
                            Row("IGST", model.TaxSummary.IgstAmount);
                        }

                        Row("Total", model.TotalAmount, bold: true);
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
