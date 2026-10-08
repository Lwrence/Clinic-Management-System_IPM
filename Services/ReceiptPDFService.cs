using CruzNeryClinic.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Diagnostics;
using System.IO;

namespace CruzNeryClinic.Services
{
    public static class ReceiptPDFService
    {
        private const string ClinicName = "CRUZ-NERY DENTAL CLINIC";
        private const string ClinicOwner = "CRISTINA C. NERY - Prop.";
        private const string ClinicTin = "VAT Reg. TIN: 226-234-039-00000";
        private const string ClinicAddress = "33 B Rodriguez Highway, San Jose, Rodriguez, Rizal";
        private const string AuthorityToPrint = "BIR AUTHORITY TO PRINT NO. OCN: 045AU20260000011135";
        private const string AuthorityDateIssued = "DATE ISSUED: January 21, 2026";
        private const string ApprovedSeries = "APPROVED SERIES: 0001-1000 * 20Bkts. (50x2)";

        public static string GenerateReceiptPdf(BillingReceiptDetail receipt)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            string receiptsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CruzNeryClinic",
                "Receipts"
            );

            Directory.CreateDirectory(receiptsFolder);

            string safeReceiptNumber = MakeSafeFileName(receipt.ReceiptNumber);
            string filePath = Path.Combine(receiptsFolder, $"{safeReceiptNumber}.pdf");

            File.WriteAllBytes(filePath, GenerateReceiptBytes(receipt));
            return filePath;
        }

        public static byte[] GenerateReceiptBytes(BillingReceiptDetail receipt)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    // Keep a normal portrait A4 sheet, but the (compact) content only
                    // fills the top half so the page can be cut/torn in half.
                    page.Size(PageSizes.A4);
                    page.Margin(18);
                    page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Arial"));

                    page.Header().Element(header =>
                    {
                        header.BorderBottom(1).BorderColor("#222222").PaddingBottom(5).Column(col =>
                        {
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Column(left =>
                                {
                                    left.Item().Text(ClinicName)
                                        .FontSize(13)
                                        .Bold()
                                        .FontColor("#111111");

                                    left.Item().PaddingTop(1).Text(ClinicOwner)
                                        .FontSize(7)
                                        .FontColor("#333333");

                                    left.Item().Text(ClinicTin)
                                        .FontSize(7)
                                        .FontColor("#333333");

                                    left.Item().Text(ClinicAddress)
                                        .FontSize(7)
                                        .FontColor("#333333");
                                });

                                row.ConstantItem(150).AlignRight().Column(right =>
                                {
                                    right.Item().AlignRight().Text("SERVICE INVOICE")
                                        .FontSize(14)
                                        .Bold()
                                        .FontColor("#111111");

                                    right.Item().PaddingTop(2).Text($"Invoice No.: {receipt.ReceiptNumber}")
                                        .FontSize(8)
                                        .Bold()
                                        .FontColor("#111111");

                                    right.Item().Text($"Date: {receipt.TransactionDateDisplay}")
                                        .FontSize(8)
                                        .FontColor("#333333");

                                    right.Item().PaddingTop(2).Text("[x] Cash Sales    [ ] Charge Sales")
                                        .FontSize(7)
                                        .FontColor("#333333");
                                });
                            });
                        });
                    });
                    page.Content().PaddingTop(8).Column(col =>
                    {
                        col.Spacing(6);

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Element(box =>
                            {
                                InfoBox(
                                    box,
                                    "SOLD TO",
                                    $"Customer / Patient Name: {receipt.PatientName}\n" +
                                    $"Patient ID: {receipt.PatientCode}\n" +
                                    $"TIN: N/A    Business Address: N/A"
                                );
                            });

                            row.ConstantItem(12);

                            row.RelativeItem().Element(box =>
                            {
                                InfoBox(
                                    box,
                                    "INVOICE DETAILS",
                                    $"Category: {receipt.PatientCategory}\n" +
                                    $"Payment Status: {receipt.PaymentStatus}\n" +
                                    $"Billing Source: {receipt.BillingSource}"
                                );
                            });
                        });
                        col.Item().Text("Invoice Items")
                            .FontSize(10)
                            .Bold()
                            .FontColor("#333333");

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2.6f); // Description / Nature of Service
                                columns.RelativeColumn(0.7f); // Quantity
                                columns.RelativeColumn(1.1f); // Unit Cost
                                columns.RelativeColumn(1.1f); // Amount
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(TableHeaderCell).Text("Description / Nature of Service");
                                header.Cell().Element(TableHeaderCell).AlignCenter().Text("Qty");
                                header.Cell().Element(TableHeaderCell).AlignRight().Text("Unit Cost");
                                header.Cell().Element(TableHeaderCell).AlignRight().Text("Amount");
                            });

                            if (receipt.InvoiceItems != null && receipt.InvoiceItems.Count > 0)
                            {
                                foreach (BillingTransactionItem item in receipt.InvoiceItems)
                                {
                                    string description = string.IsNullOrWhiteSpace(item.ItemDescription)
                                        ? item.ServiceName
                                        : $"{item.ServiceName} - {item.ItemDescription}";

                                    if (item.TreatmentDate.HasValue)
                                        description = $"{description}\nDate: {item.TreatmentDateDisplay}";

                                    string unitCost = item.IsIncluded
                                        ? "Included"
                                        : $"₱{item.Amount:N2}";

                                    string amount = item.IsIncluded
                                        ? "₱0.00"
                                        : $"₱{item.Amount:N2}";

                                    table.Cell().Element(TableBodyCell).Text(description);
                                    table.Cell().Element(TableBodyCell).AlignCenter().Text("1");
                                    table.Cell().Element(TableBodyCell).AlignRight().Text(unitCost);
                                    table.Cell().Element(TableBodyCell).AlignRight().Text(amount);
                                }
                            }
                            else
                            {
                                // Fallback for older billing records that do not have BillingTransactionItems yet.
                                table.Cell().Element(TableBodyCell).Text(
                                    string.IsNullOrWhiteSpace(receipt.Description)
                                        ? receipt.ServiceName
                                        : receipt.Description
                                );

                                table.Cell().Element(TableBodyCell).AlignCenter().Text("1");
                                table.Cell().Element(TableBodyCell).AlignRight().Text(receipt.TotalAmountDisplay);
                                table.Cell().Element(TableBodyCell).AlignRight().Text(receipt.TotalAmountDisplay);
                            }
                        });

                        col.Item().PaddingTop(2).Text("Payment History")
                            .FontSize(10)
                            .Bold()
                            .FontColor("#333333");

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.2f); // Payment Date
                                columns.RelativeColumn(1.0f); // Method
                                columns.RelativeColumn(2.0f); // Notes
                                columns.RelativeColumn(1.1f); // Amount
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(TableHeaderCell).Text("Payment Date");
                                header.Cell().Element(TableHeaderCell).Text("Method");
                                header.Cell().Element(TableHeaderCell).Text("Notes");
                                header.Cell().Element(TableHeaderCell).AlignRight().Text("Amount");
                            });

                            if (receipt.PaymentHistory != null && receipt.PaymentHistory.Count > 0)
                            {
                                foreach (PaymentRecord payment in receipt.PaymentHistory)
                                {
                                    table.Cell().Element(TableBodyCell).Text(payment.PaymentDate.ToString("MM/dd/yyyy"));

                                    table.Cell().Element(TableBodyCell).Text(
                                        string.IsNullOrWhiteSpace(payment.PaymentMethod)
                                            ? "Cash"
                                            : payment.PaymentMethod
                                    );

                                    table.Cell().Element(TableBodyCell).Text(
                                        string.IsNullOrWhiteSpace(payment.Notes)
                                            ? "-"
                                            : payment.Notes
                                    );

                                    table.Cell().Element(TableBodyCell).AlignRight().Text($"₱{payment.AmountPaid:N2}");
                                }
                            }
                            else
                            {
                                table.Cell().Element(TableBodyCell).Text("-");
                                table.Cell().Element(TableBodyCell).Text("-");
                                table.Cell().Element(TableBodyCell).Text("No payment recorded yet.");
                                table.Cell().Element(TableBodyCell).AlignRight().Text("₱0.00");
                            }
                        });

                        col.Item().AlignRight().Width(200).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            SummaryRow(table, "Gross Amount", receipt.TotalAmountDisplay);
                            
                            if (receipt.HasVatExemption)
                            {
                                SummaryRow(table, "VAT Exempt Sales", receipt.VatExemptSalesDisplay);
                            }

                            SummaryRow(table, $"Discount ({receipt.DiscountType})", receipt.DiscountAmountDisplay);
                            SummaryRow(table, "Billable Amount", receipt.SubtotalAfterDiscountDisplay);
                            SummaryRow(table, "Amount Paid", receipt.AmountPaidDisplay);
                            SummaryRow(table, "Change", receipt.ChangeAmountDisplay);
                            SummaryRow(table, "Remaining Balance", receipt.RemainingBalanceDisplay, true);
                        });

                        if (!string.IsNullOrWhiteSpace(receipt.Notes))
                        {
                            col.Item().PaddingTop(2).Column(notes =>
                            {
                                notes.Item().Text("Notes").Bold().FontSize(8);
                                notes.Item().Text(receipt.Notes).FontSize(7).FontColor("#444444");
                            });
                        }

                        // Authority / BIR details kept inside the content (not page.Footer)
                        // so the whole receipt stays within the top half of the sheet.
                        col.Item().PaddingTop(4).BorderTop(1).BorderColor("#CCCCCC").PaddingTop(3).Row(row =>
                        {
                            row.RelativeItem().Column(left =>
                            {
                                left.Item().Text("PERMIT / AUTHORITY DETAILS")
                                    .FontSize(6)
                                    .Bold()
                                    .FontColor("#333333");

                                left.Item().Text(AuthorityToPrint)
                                    .FontSize(6)
                                    .FontColor("#555555");

                                left.Item().Text(AuthorityDateIssued)
                                    .FontSize(6)
                                    .FontColor("#555555");

                                left.Item().Text(ApprovedSeries)
                                    .FontSize(6)
                                    .FontColor("#555555");
                            });

                            row.RelativeItem().AlignRight().Column(right =>
                            {
                                right.Item().Text("This invoice was generated by Dental Clinic Management System.")
                                    .FontSize(6)
                                    .FontColor("#777777");

                                right.Item().PaddingTop(2).Text("Verify ATP and BIR details before production use.")
                                    .FontSize(6)
                                    .FontColor("#777777");
                            });
                        });

                        // Cut/tear indicator marking the bottom of the receipt.
                        col.Item().PaddingTop(10)
                            .BorderBottom(1).BorderColor("#999999")
                            .DefaultTextStyle(x => x.FontSize(7).FontColor("#999999"))
                            .Text("✂  - - - - - - - - - - - - - - - - cut here - - - - - - - - - - - - - - - -");
                    });
                });
            }).GeneratePdf();
        }

        public static void OpenPdf(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }

        private static void InfoBox(IContainer container, string title, string body)
        {
            container.Column(col =>
            {
                col.Item().Background("#F3F7FA").Padding(4).Text(title).Bold().FontSize(9);
                col.Item().Border(1).BorderColor("#DDDDDD").Padding(5).Text(body).FontSize(8);
            });
        }

        private static IContainer TableHeaderCell(IContainer container)
        {
            return container
                .Background("#EEF3FA")
                .Border(1)
                .BorderColor("#DDDDDD")
                .Padding(3);
        }

        private static IContainer TableBodyCell(IContainer container)
        {
            return container
                .Border(1)
                .BorderColor("#E0E0E0")
                .Padding(3);
        }

        private static void SummaryRow(TableDescriptor table, string label, string value, bool isBold = false)
        {
            table.Cell().Element(c => SummaryCell(c, isBold)).Text(label);
            table.Cell().Element(c => SummaryCell(c, isBold)).AlignRight().Text(value);
        }

        private static IContainer SummaryCell(IContainer container, bool isBold)
        {
            IContainer styled = container
                .BorderBottom(1)
                .BorderColor("#DDDDDD")
                .PaddingVertical(3);

            return styled.DefaultTextStyle(x =>
                isBold ? x.Bold().FontSize(9) : x.FontSize(8)
            );
        }

        private static string MakeSafeFileName(string fileName)
        {
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(invalidChar, '-');

            return fileName;
        }
    }
}
