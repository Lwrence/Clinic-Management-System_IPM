using CommunityToolkit.Mvvm.Input;
using CruzNeryClinic.Models;
using CruzNeryClinic.Repositories;
using CruzNeryClinic.Services;
using CruzNeryClinic.Views.Charts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Input;

namespace CruzNeryClinic.ViewModels
{
    public class ReportsViewModel : BaseViewModel
    {
        private readonly ReportsRepository _repository = new();

        public event Action? ChartDataRefreshed;

        // ── Tab selection ──────────────────────────────────────────────────────

        private bool _isPatientVisitsSelected = true;
        private bool _isTransactionReportsSelected;
        private bool _isInventoryReportsSelected;
        private bool _isUserActivityLogSelected;

        public bool IsPatientVisitsSelected
        {
            get => _isPatientVisitsSelected;
            set => SetProperty(ref _isPatientVisitsSelected, value);
        }
        public bool IsTransactionReportsSelected
        {
            get => _isTransactionReportsSelected;
            set => SetProperty(ref _isTransactionReportsSelected, value);
        }
        public bool IsInventoryReportsSelected
        {
            get => _isInventoryReportsSelected;
            set => SetProperty(ref _isInventoryReportsSelected, value);
        }
        public bool IsUserActivityLogSelected
        {
            get => _isUserActivityLogSelected;
            set => SetProperty(ref _isUserActivityLogSelected, value);
        }

        // ── Filter state ───────────────────────────────────────────────────────

        private bool _isThisMonthSelected = true;
        private bool _isThisYearSelected;
        private bool _isCustomRangeSelected;
        private DateTime? _filterFromDate;
        private DateTime? _filterToDate;
        private string _showingText = "Showing: Monthly Report";
        private bool _isDateRangeVisible;
        private int _selectedFilterMonthIndex = DateTime.Now.Month - 1;
        private int _selectedFilterYear = DateTime.Now.Year;

        public List<string> MonthNames { get; } = new List<string>
        {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        };

        public List<int> AvailableYears { get; } = new List<int>(
            System.Linq.Enumerable.Range(2020, DateTime.Now.Year - 2020 + 2));

        public int SelectedFilterMonthIndex
        {
            get => _selectedFilterMonthIndex;
            set
            {
                SetProperty(ref _selectedFilterMonthIndex, value);
                if (IsThisMonthSelected) UpdateMonthRange();
            }
        }

        public int SelectedFilterYear
        {
            get => _selectedFilterYear;
            set
            {
                SetProperty(ref _selectedFilterYear, value);
                if (IsThisMonthSelected) UpdateMonthRange();
                else if (IsThisYearSelected) UpdateYearRange();
            }
        }

        public bool IsThisMonthSelected
        {
            get => _isThisMonthSelected;
            set => SetProperty(ref _isThisMonthSelected, value);
        }
        public bool IsThisYearSelected
        {
            get => _isThisYearSelected;
            set => SetProperty(ref _isThisYearSelected, value);
        }
        public bool IsCustomRangeSelected
        {
            get => _isCustomRangeSelected;
            set => SetProperty(ref _isCustomRangeSelected, value);
        }
        public DateTime? FilterFromDate
        {
            get => _filterFromDate;
            set
            {
                SetProperty(ref _filterFromDate, value);
                if (IsCustomRangeSelected && _filterFromDate.HasValue && _filterToDate.HasValue)
                {
                    ShowingText = $"Showing: {_filterFromDate:MMM dd} – {_filterToDate:MMM dd, yyyy}";
                    LoadData();
                }
            }
        }
        public DateTime? FilterToDate
        {
            get => _filterToDate;
            set
            {
                SetProperty(ref _filterToDate, value);
                if (IsCustomRangeSelected && _filterFromDate.HasValue && _filterToDate.HasValue)
                {
                    ShowingText = $"Showing: {_filterFromDate:MMM dd} – {_filterToDate:MMM dd, yyyy}";
                    LoadData();
                }
            }
        }
        public bool IsDateRangeVisible
        {
            get => _isDateRangeVisible;
            set => SetProperty(ref _isDateRangeVisible, value);
        }
        public string ShowingText
        {
            get => _showingText;
            set => SetProperty(ref _showingText, value);
        }

        // ── Print preview state ────────────────────────────────────────────────

        private bool _isReportPrintPreviewOpen;
        public bool IsReportPrintPreviewOpen
        {
            get => _isReportPrintPreviewOpen;
            set => SetProperty(ref _isReportPrintPreviewOpen, value);
        }

        // File URI of the generated report HTML, navigated to in the preview WebView2.
        // Each print writes a uniquely-named temp file so the preview always reloads
        // and the printout footer shows a short path instead of a giant data URI.
        private Uri? _reportPrintPreviewUri;
        public Uri? ReportPrintPreviewUri
        {
            get => _reportPrintPreviewUri;
            private set => SetProperty(ref _reportPrintPreviewUri, value);
        }

        // Title shown on the preview overlay, reflecting the active report tab.
        public string ReportPrintPreviewTitle => $"{GetActiveReportTitle()} — Print Preview";

        // ── Print scope (chosen in the Print options dialog) ────────────────────
        private bool _isPrintOptionsOpen;
        public bool IsPrintOptionsOpen
        {
            get => _isPrintOptionsOpen;
            set => SetProperty(ref _isPrintOptionsOpen, value);
        }

        public ObservableCollection<string> PrintRowOptions { get; } =
            new() { "25", "50", "100", "All", "Custom" };

        private string _selectedPrintRowLimit = "50";
        public string SelectedPrintRowLimit
        {
            get => _selectedPrintRowLimit;
            set
            {
                if (SetProperty(ref _selectedPrintRowLimit, value))
                    OnPropertyChanged(nameof(IsCustomRowLimit));
            }
        }

        // True when the user picks "Custom" in the rows dropdown; reveals the number box.
        public bool IsCustomRowLimit => SelectedPrintRowLimit == "Custom";

        private string _customRowLimit = "200";
        public string CustomRowLimit
        {
            get => _customRowLimit;
            set => SetProperty(ref _customRowLimit, value);
        }

        private bool _printIncludeCharts = true;
        public bool PrintIncludeCharts
        {
            get => _printIncludeCharts;
            set => SetProperty(ref _printIncludeCharts, value);
        }

        private bool _printIncludeSummary = true;
        public bool PrintIncludeSummary
        {
            get => _printIncludeSummary;
            set => SetProperty(ref _printIncludeSummary, value);
        }

        private bool _printIncludeDetailedRecords = true;
        public bool PrintIncludeDetailedRecords
        {
            get => _printIncludeDetailedRecords;
            set => SetProperty(ref _printIncludeDetailedRecords, value);
        }

        // Resolves SelectedPrintRowLimit to a row cap (int.MaxValue = no cap).
        // "Custom" uses CustomRowLimit; an invalid/blank custom value means no cap.
        private int PrintRowLimit
        {
            get
            {
                if (IsCustomRowLimit)
                    return int.TryParse(CustomRowLimit, out int custom) && custom > 0
                        ? custom
                        : int.MaxValue;

                return int.TryParse(SelectedPrintRowLimit, out int n) ? n : int.MaxValue;
            }
        }

        // ── Report period (chosen in the Print options dialog) ──────────────────
        // Lets a report be generated for the whole month, quarter, or year that
        // contains the report's currently selected month/year, regardless of the
        // filter-bar mode. Applies to both the PDF preview and the Excel export.
        public List<string> PrintPeriodOptions { get; } = new() { "Monthly", "Quarterly", "Annual" };

        private string _selectedReportPeriod = "Monthly";
        public string SelectedReportPeriod
        {
            get => _selectedReportPeriod;
            set => SetProperty(ref _selectedReportPeriod, value);
        }

        // ── Export format (PDF preview vs. Excel workbook) ──────────────────────
        // Mutually exclusive: choosing one clears the other so the radio buttons
        // stay in sync with the backing state.
        private bool _isPdfExport = true;
        public bool IsPdfExport
        {
            get => _isPdfExport;
            set
            {
                if (SetProperty(ref _isPdfExport, value) && value) IsExcelExport = false;
                OnPropertyChanged(nameof(GenerateButtonText));
            }
        }

        private bool _isExcelExport;
        public bool IsExcelExport
        {
            get => _isExcelExport;
            set
            {
                if (SetProperty(ref _isExcelExport, value) && value) IsPdfExport = false;
                OnPropertyChanged(nameof(GenerateButtonText));
            }
        }

        // Primary action label in the Print Options dialog, reflecting the format.
        public string GenerateButtonText => IsExcelExport ? "Export to Excel" : "Generate Preview";

        // ── Chart & table data ─────────────────────────────────────────────────

        public ObservableCollection<PatientVisitReportItem> PatientVisitsItems { get; } = new();
        public List<DualChartDataPoint> PatientVisitTrend { get; private set; } = new();

        public ObservableCollection<TransactionReportItem> TransactionItems { get; } = new();
        public List<ChartDataPoint> RevenueTrend { get; private set; } = new();
        public List<ChartDataPoint> DailyTransactionCounts { get; private set; } = new();

        public ObservableCollection<InventoryReportItem> InventoryItems { get; } = new();
        public List<ChartDataPoint> InventoryChartData { get; private set; } = new();

        public ObservableCollection<ActivityLogReportItem> ActivityLogItems { get; } = new();
        public List<PieChartSlice> ActivityByType { get; private set; } = new();
        public List<ChartDataPoint> ActivityByModule { get; private set; } = new();

        // Plain-language interpretation shown under each chart.
        private string _patientVisitInsight = "";
        public string PatientVisitInsight
        {
            get => _patientVisitInsight;
            private set => SetProperty(ref _patientVisitInsight, value);
        }

        private string _revenueInsight = "";
        public string RevenueInsight
        {
            get => _revenueInsight;
            private set => SetProperty(ref _revenueInsight, value);
        }

        private string _dailyTransactionsInsight = "";
        public string DailyTransactionsInsight
        {
            get => _dailyTransactionsInsight;
            private set => SetProperty(ref _dailyTransactionsInsight, value);
        }

        private string _inventoryInsight = "";
        public string InventoryInsight
        {
            get => _inventoryInsight;
            private set => SetProperty(ref _inventoryInsight, value);
        }

        private string _activityByTypeInsight = "";
        public string ActivityByTypeInsight
        {
            get => _activityByTypeInsight;
            private set => SetProperty(ref _activityByTypeInsight, value);
        }

        private string _activityByModuleInsight = "";
        public string ActivityByModuleInsight
        {
            get => _activityByModuleInsight;
            private set => SetProperty(ref _activityByModuleInsight, value);
        }

        // ── Chart detail panel (opens when a chart is clicked) ──────────────────
        public event Action? ChartDetailRequested;

        public string SelectedChartKey { get; private set; } = "";

        private bool _isChartDetailOpen;
        public bool IsChartDetailOpen
        {
            get => _isChartDetailOpen;
            set => SetProperty(ref _isChartDetailOpen, value);
        }

        private string _chartDetailTitle = "";
        public string ChartDetailTitle
        {
            get => _chartDetailTitle;
            private set => SetProperty(ref _chartDetailTitle, value);
        }

        private string _chartDetailDescription = "";
        public string ChartDetailDescription
        {
            get => _chartDetailDescription;
            private set => SetProperty(ref _chartDetailDescription, value);
        }

        private string _chartDetailInsight = "";
        public string ChartDetailInsight
        {
            get => _chartDetailInsight;
            private set => SetProperty(ref _chartDetailInsight, value);
        }

        public ObservableCollection<KeyFigure> ChartDetailFigures { get; } = new();

        // ── Commands ───────────────────────────────────────────────────────────

        public ICommand SelectPatientVisitsCommand { get; }
        public ICommand SelectTransactionReportsCommand { get; }
        public ICommand SelectInventoryReportsCommand { get; }
        public ICommand SelectUserActivityLogCommand { get; }
        public ICommand ApplyThisMonthCommand { get; }
        public ICommand ApplyThisYearCommand { get; }
        public ICommand ApplyCustomRangeCommand { get; }
        public ICommand OpenPrintOptionsCommand { get; }
        public ICommand ClosePrintOptionsCommand { get; }
        public ICommand PrintReportCommand { get; }
        public ICommand CloseReportPrintPreviewCommand { get; }
        public ICommand OpenChartDetailCommand { get; }
        public ICommand CloseChartDetailCommand { get; }

        public ReportsViewModel()
        {
            SelectPatientVisitsCommand = new RelayCommand(SelectPatientVisits);
            SelectTransactionReportsCommand = new RelayCommand(SelectTransactionReports);
            SelectInventoryReportsCommand = new RelayCommand(SelectInventoryReports);
            SelectUserActivityLogCommand = new RelayCommand(SelectUserActivityLog);
            ApplyThisMonthCommand = new RelayCommand(ApplyThisMonth);
            ApplyThisYearCommand = new RelayCommand(ApplyThisYear);
            ApplyCustomRangeCommand = new RelayCommand(ApplyCustomRange);
            OpenPrintOptionsCommand = new RelayCommand(() => IsPrintOptionsOpen = true);
            ClosePrintOptionsCommand = new RelayCommand(() => IsPrintOptionsOpen = false);
            PrintReportCommand = new RelayCommand(PrintReport);
            CloseReportPrintPreviewCommand = new RelayCommand(() => IsReportPrintPreviewOpen = false);
            OpenChartDetailCommand = new RelayCommand<string>(OpenChartDetail);
            CloseChartDetailCommand = new RelayCommand(() => IsChartDetailOpen = false);

            ApplyThisMonth();
        }

        // Opens the right-side chart detail panel for the clicked chart.
        private void OpenChartDetail(string? key)
        {
            if (string.IsNullOrEmpty(key)) return;

            ChartDetailFigures.Clear();

            switch (key)
            {
                case "patientVisit":
                    ChartDetailTitle = "Patient Visits Trend";
                    ChartDetailInsight = PatientVisitInsight;
                    foreach (var f in ChartInsight.KeyFigures(PatientVisitTrend, "scheduled", "walk-in"))
                        ChartDetailFigures.Add(f);
                    break;
                case "revenue":
                    ChartDetailTitle = "Revenue Trend";
                    ChartDetailInsight = RevenueInsight;
                    foreach (var f in ChartInsight.KeyFigures(RevenueTrend, "₱"))
                        ChartDetailFigures.Add(f);
                    break;
                case "dailyTx":
                    ChartDetailTitle = "Daily Transactions";
                    ChartDetailInsight = DailyTransactionsInsight;
                    foreach (var f in ChartInsight.KeyFigures(DailyTransactionCounts))
                        ChartDetailFigures.Add(f);
                    break;
                case "inventory":
                    ChartDetailTitle = "Stock Levels vs Threshold";
                    ChartDetailInsight = InventoryInsight;
                    foreach (var f in ChartInsight.KeyFigures(InventoryChartData))
                        ChartDetailFigures.Add(f);
                    break;
                case "activityType":
                    ChartDetailTitle = "Activity Distribution by Type";
                    ChartDetailInsight = ActivityByTypeInsight;
                    foreach (var f in ChartInsight.KeyFigures(ActivityByType))
                        ChartDetailFigures.Add(f);
                    break;
                case "activityModule":
                    ChartDetailTitle = "Activity by Module";
                    ChartDetailInsight = ActivityByModuleInsight;
                    foreach (var f in ChartInsight.KeyFigures(ActivityByModule))
                        ChartDetailFigures.Add(f);
                    break;
                default:
                    return;
            }

            ChartDetailDescription = ChartInsight.Description(key);
            SelectedChartKey = key;
            IsChartDetailOpen = true;
            ChartDetailRequested?.Invoke();
        }

        // ── Tab switching ──────────────────────────────────────────────────────

        // Opens a specific report tab when navigated to from the Dashboard "View All".
        public void ShowReport(string reportKey)
        {
            switch (reportKey)
            {
                case "Transaction": SelectTransactionReports(); break;
                case "Inventory": SelectInventoryReports(); break;
                case "UserActivity": SelectUserActivityLog(); break;
                default: SelectPatientVisits(); break;
            }
        }

        private void SelectPatientVisits()
        {
            IsPatientVisitsSelected = true;
            IsTransactionReportsSelected = false;
            IsInventoryReportsSelected = false;
            IsUserActivityLogSelected = false;
            LoadData();
        }

        private void SelectTransactionReports()
        {
            IsPatientVisitsSelected = false;
            IsTransactionReportsSelected = true;
            IsInventoryReportsSelected = false;
            IsUserActivityLogSelected = false;
            LoadData();
        }

        private void SelectInventoryReports()
        {
            IsPatientVisitsSelected = false;
            IsTransactionReportsSelected = false;
            IsInventoryReportsSelected = true;
            IsUserActivityLogSelected = false;
            LoadData();
        }

        private void SelectUserActivityLog()
        {
            IsPatientVisitsSelected = false;
            IsTransactionReportsSelected = false;
            IsInventoryReportsSelected = false;
            IsUserActivityLogSelected = true;
            LoadData();
        }

        // ── Filter switching ───────────────────────────────────────────────────

        private void ApplyThisMonth()
        {
            IsThisMonthSelected = true;
            IsThisYearSelected = false;
            IsCustomRangeSelected = false;
            _selectedFilterMonthIndex = DateTime.Now.Month - 1;
            _selectedFilterYear = DateTime.Now.Year;
            OnPropertyChanged(nameof(SelectedFilterMonthIndex));
            OnPropertyChanged(nameof(SelectedFilterYear));
            UpdateMonthRange();
        }

        private void ApplyThisYear()
        {
            IsThisMonthSelected = false;
            IsThisYearSelected = true;
            IsCustomRangeSelected = false;
            _selectedFilterYear = DateTime.Now.Year;
            OnPropertyChanged(nameof(SelectedFilterYear));
            UpdateYearRange();
        }

        private void ApplyCustomRange()
        {
            IsThisMonthSelected = false;
            IsThisYearSelected = false;
            IsCustomRangeSelected = true;
            ShowingText = "Showing: Custom Range";
        }

        private void UpdateMonthRange()
        {
            int month = _selectedFilterMonthIndex + 1;
            var first = new DateTime(_selectedFilterYear, month, 1);
            var last = first.AddMonths(1).AddDays(-1);
            _filterFromDate = first;
            _filterToDate = last;
            OnPropertyChanged(nameof(FilterFromDate));
            OnPropertyChanged(nameof(FilterToDate));
            ShowingText = $"Showing: {first:MMMM yyyy}";
            LoadData();
        }

        private void UpdateYearRange()
        {
            _filterFromDate = new DateTime(_selectedFilterYear, 1, 1);
            _filterToDate = new DateTime(_selectedFilterYear, 12, 31);
            OnPropertyChanged(nameof(FilterFromDate));
            OnPropertyChanged(nameof(FilterToDate));
            ShowingText = $"Showing: {_selectedFilterYear}";
            LoadData();
        }

        // ── Data loading ───────────────────────────────────────────────────────

        private (string from, string to) GetDateRange()
        {
            if (_filterFromDate.HasValue && _filterToDate.HasValue)
                return (_filterFromDate.Value.ToString("yyyy-MM-dd"), _filterToDate.Value.ToString("yyyy-MM-dd"));

            var now = DateTime.Now;
            var first = new DateTime(now.Year, now.Month, 1);
            var last = first.AddMonths(1).AddDays(-1);
            return (first.ToString("yyyy-MM-dd"), last.ToString("yyyy-MM-dd"));
        }

        // Computes the date range (and a display label) for the report period
        // chosen in the Print options dialog, anchored on the report's currently
        // selected month/year so the printout matches the on-screen selection.
        private (DateTime from, DateTime to, string label) GetPrintPeriodRange()
        {
            int year = _selectedFilterYear;
            int monthIndex = _selectedFilterMonthIndex; // 0-based

            switch (SelectedReportPeriod)
            {
                case "Quarterly":
                {
                    int quarter = monthIndex / 3;                 // 0..3
                    int startMonth = quarter * 3 + 1;             // 1,4,7,10
                    var from = new DateTime(year, startMonth, 1);
                    var to = from.AddMonths(3).AddDays(-1);
                    string label = $"Q{quarter + 1} {year} ({from:MMMM} – {to:MMMM})";
                    return (from, to, label);
                }
                case "Annual":
                {
                    var from = new DateTime(year, 1, 1);
                    var to = new DateTime(year, 12, 31);
                    return (from, to, $"Annual {year}");
                }
                default: // Monthly
                {
                    var from = new DateTime(year, monthIndex + 1, 1);
                    var to = from.AddMonths(1).AddDays(-1);
                    return (from, to, $"{from:MMMM yyyy}");
                }
            }
        }

        // Applies the chosen report period to the active date range and refreshes
        // the on-screen data so the preview/export reflects the printed period.
        private void ApplyPrintPeriod()
        {
            var (from, to, label) = GetPrintPeriodRange();
            _filterFromDate = from;
            _filterToDate = to;
            OnPropertyChanged(nameof(FilterFromDate));
            OnPropertyChanged(nameof(FilterToDate));
            ShowingText = $"Showing: {label}";
            LoadData();
        }

        // ── Print (WebView2 preview) ─────────────────────────────────────────────

        private string GetActiveReportTitle()
        {
            if (IsTransactionReportsSelected) return "Transaction Report";
            if (IsInventoryReportsSelected) return "Inventory Report";
            if (IsUserActivityLogSelected) return "User Activity Log";
            return "Patient Visits Report";
        }

        // Builds an HTML version of the active report (charts + table) and opens
        // it in the WebView2 print-preview overlay.
        private void PrintReport()
        {
            // Scope the data to the chosen report period (Monthly/Quarterly/Annual)
            // before generating, so both the preview and the export match it.
            ApplyPrintPeriod();

            // Excel export takes a different path: it writes every record (no row
            // cap) straight to an .xlsx file instead of the HTML print preview.
            if (IsExcelExport)
            {
                ExportToExcel();
                return;
            }

            string html = BuildReportHtml();

            // Unique file name per print: forces a reload and keeps the printed
            // footer URL short instead of a long base64 data URI.
            string filePath = Path.Combine(
                Path.GetTempPath(),
                $"ClinicReport_{DateTime.Now:yyyyMMddHHmmssfff}.html");

            File.WriteAllText(filePath, html, Encoding.UTF8);

            ReportPrintPreviewUri = new Uri(filePath);
            OnPropertyChanged(nameof(ReportPrintPreviewTitle));
            IsPrintOptionsOpen = false;
            IsReportPrintPreviewOpen = true;
        }

        // Exports the full record set of the active report to an .xlsx file.
        // Unlike the PDF preview, this ignores the "rows to print" cap and writes
        // every record so the spreadsheet holds the complete data.
        private void ExportToExcel()
        {
            string title = GetActiveReportTitle();
            string[] headers;
            var dataRows = new List<string[]>();

            if (IsTransactionReportsSelected)
            {
                headers = new[] { "Transaction ID", "Date", "Patient ID", "Patient Name", "Description", "Amount", "Payment Method" };
                foreach (TransactionReportItem t in TransactionItems)
                    dataRows.Add(new[] { t.ReceiptNumber, t.Date, t.PatientCode, t.PatientName, t.Service, t.Amount.ToString("N2"), t.PaymentMethod });
            }
            else if (IsInventoryReportsSelected)
            {
                headers = new[] { "Item Name", "Quantity on Hand", "Reorder Level", "Last Restocked", "Status" };
                foreach (InventoryReportItem i in InventoryItems)
                    dataRows.Add(new[] { i.ItemName, i.CurrentStock.ToString("N0"), i.Threshold.ToString("N0"), i.LastRestocked, i.Status });
            }
            else if (IsUserActivityLogSelected)
            {
                headers = new[] { "Date / Time", "User", "Role", "Action", "Module", "Details" };
                foreach (ActivityLogReportItem a in ActivityLogItems)
                    dataRows.Add(new[] { a.Timestamp, a.Name, a.Role, a.Action, a.Module, a.Details });
            }
            else
            {
                headers = new[] { "Date", "Patient ID", "Patient Name", "Visit Type", "Service", "Dentist" };
                foreach (PatientVisitReportItem p in PatientVisitsItems)
                    dataRows.Add(new[] { p.Date, p.PatientCode, p.PatientName, p.VisitType, p.Service, p.Dentist });
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Report to Excel",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"{title.Replace(' ', '_')}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var workbook = new ClosedXML.Excel.XLWorkbook();
                string sheetName = title.Length > 31 ? title.Substring(0, 31) : title;
                var ws = workbook.Worksheets.Add(sheetName);
                int colCount = headers.Length;

                // Title + meta band.
                ws.Cell(1, 1).Value = title;
                ws.Range(1, 1, 1, colCount).Merge();
                ws.Cell(1, 1).Style.Font.Bold = true;
                ws.Cell(1, 1).Style.Font.FontSize = 14;
                ws.Cell(1, 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#223357");

                string periodLabel = ShowingText.Replace("Showing: ", string.Empty);
                ws.Cell(2, 1).Value = $"Period: {periodLabel}   •   Generated {DateTime.Now:yyyy-MM-dd HH:mm}   •   {dataRows.Count:N0} record(s)";
                ws.Range(2, 1, 2, colCount).Merge();
                ws.Cell(2, 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#777777");

                // Header row.
                const int headerRow = 4;
                for (int c = 0; c < colCount; c++)
                {
                    var cell = ws.Cell(headerRow, c + 1);
                    cell.Value = headers[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                    cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#223357");
                }

                // Data rows.
                int r = headerRow + 1;
                foreach (string[] row in dataRows)
                {
                    for (int c = 0; c < row.Length; c++)
                        ws.Cell(r, c + 1).Value = row[c];
                    r++;
                }

                ws.SheetView.FreezeRows(headerRow);
                ws.Columns().AdjustToContents();
                workbook.SaveAs(dialog.FileName);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to export to Excel: {ex.Message}",
                    "Export Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            IsPrintOptionsOpen = false;

            var openNow = System.Windows.MessageBox.Show(
                $"Report exported successfully to:\n{dialog.FileName}\n\nDo you want to open it now?",
                "Export Complete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);

            if (openNow == System.Windows.MessageBoxResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"File saved, but could not be opened automatically: {ex.Message}",
                        "Open File",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }
        }

        private string BuildReportHtml()
        {
            string title = GetActiveReportTitle();

            string charts;
            string headerCells;
            StringBuilder rows = new();
            var summary = new List<(string Label, string Value)>();

            int rowLimit = PrintRowLimit;
            int totalRows = 0;

            if (IsTransactionReportsSelected)
            {
                charts = AreaChartSvg("Revenue Trend", RevenueTrend.Select(p => (p.Label, p.Value)), "#50C878", "₱", RevenueInsight)
                       + BarChartSvg("Daily Transactions", DailyTransactionCounts.Select(p => (p.Label, p.Value)), "#2F98D0", "", DailyTransactionsInsight);

                headerCells = Th("Transaction ID", "Date", "Patient ID", "Patient Name", "Description", "Amount", "Payment Method");
                totalRows = TransactionItems.Count;
                foreach (TransactionReportItem t in TransactionItems.Take(rowLimit))
                    rows.Append(Tr(t.ReceiptNumber, t.Date, t.PatientCode, t.PatientName, t.Service, $"₱ {t.Amount:N2}", t.PaymentMethod));

                decimal totalRevenue = TransactionItems.Sum(t => (decimal)t.Amount);
                summary.Add(("Total Transactions", TransactionItems.Count.ToString("N0")));
                summary.Add(("Total Revenue", $"₱ {totalRevenue:N2}"));
                summary.Add(("Patients Billed", TransactionItems.Select(t => t.PatientCode).Distinct().Count().ToString("N0")));

                decimal avgTransaction = TransactionItems.Count > 0 ? totalRevenue / TransactionItems.Count : 0m;
                summary.Add(("Average Transaction Value", $"₱ {avgTransaction:N2}"));

                var topService = TransactionItems
                    .GroupBy(t => string.IsNullOrWhiteSpace(t.Service) ? "Unspecified" : t.Service)
                    .OrderByDescending(g => g.Sum(t => t.Amount))
                    .FirstOrDefault();
                if (topService != null)
                    summary.Add(("Top Service by Revenue", $"{topService.Key} (₱ {topService.Sum(t => (decimal)t.Amount):N2})"));

                var topDay = TransactionItems
                    .GroupBy(t => t.Date)
                    .OrderByDescending(g => g.Sum(t => t.Amount))
                    .FirstOrDefault();
                if (topDay != null)
                    summary.Add(("Highest-Revenue Day", $"{topDay.Key} (₱ {topDay.Sum(t => (decimal)t.Amount):N2})"));

                // Subtotals per payment method.
                foreach (var grp in TransactionItems
                             .GroupBy(t => string.IsNullOrWhiteSpace(t.PaymentMethod) ? "Unspecified" : t.PaymentMethod)
                             .OrderByDescending(g => g.Sum(t => t.Amount)))
                {
                    summary.Add(($"{grp.Key}", $"₱ {grp.Sum(t => (decimal)t.Amount):N2}"));
                }
            }
            else if (IsInventoryReportsSelected)
            {
                charts = BarChartSvg("Stock Levels", InventoryChartData.Select(p => (p.Label, p.Value)), "#FF981D", "", InventoryInsight);

                headerCells = Th("Item Name", "Quantity on Hand", "Reorder Level", "Last Restocked", "Status");
                totalRows = InventoryItems.Count;
                foreach (InventoryReportItem i in InventoryItems.Take(rowLimit))
                    rows.Append(Tr(i.ItemName, i.CurrentStock.ToString("N0"), i.Threshold.ToString("N0"), i.LastRestocked, i.Status));

                int lowStock = InventoryItems.Count(i => i.CurrentStock <= i.Threshold);
                int outOfStock = InventoryItems.Count(i => i.CurrentStock == 0);
                int wellStocked = InventoryItems.Count(i => i.CurrentStock > i.Threshold);
                summary.Add(("Total Items", InventoryItems.Count.ToString("N0")));
                summary.Add(("Low / Out of Stock", lowStock.ToString("N0")));
                summary.Add(("Out of Stock", outOfStock.ToString("N0")));
                summary.Add(("Well Stocked", wellStocked.ToString("N0")));
                summary.Add(("Units in Stock", InventoryItems.Sum(i => i.CurrentStock).ToString("N0")));

                var critical = InventoryItems
                    .OrderBy(i => i.CurrentStock - i.Threshold)
                    .FirstOrDefault();
                if (critical != null)
                    summary.Add(("Most Critical Item", $"{critical.ItemName} ({critical.CurrentStock} / {critical.Threshold})"));
            }
            else if (IsUserActivityLogSelected)
            {
                charts = PieChartSvg("Activity by Type", ActivityByType, ActivityByTypeInsight)
                       + BarChartSvg("Activity by Module", ActivityByModule.Select(p => (p.Label, p.Value)), "#A855F7", "", ActivityByModuleInsight);

                headerCells = Th("Date / Time", "User", "Role", "Action", "Module", "Details");
                totalRows = ActivityLogItems.Count;
                foreach (ActivityLogReportItem a in ActivityLogItems.Take(rowLimit))
                    rows.Append(Tr(a.Timestamp, a.Name, a.Role, a.Action, a.Module, a.Details));

                string mostActive = ActivityLogItems
                    .GroupBy(a => a.Name)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault() ?? "—";
                string topAction = ActivityLogItems
                    .GroupBy(a => a.Action)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault() ?? "—";

                string topModule = ActivityLogItems
                    .GroupBy(a => string.IsNullOrWhiteSpace(a.Module) ? "Unspecified" : a.Module)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault() ?? "—";

                summary.Add(("Total Actions Logged", ActivityLogItems.Count.ToString("N0")));
                summary.Add(("Most Active User", string.IsNullOrWhiteSpace(mostActive) ? "—" : mostActive));
                summary.Add(("Most Frequent Action", string.IsNullOrWhiteSpace(topAction) ? "—" : topAction));
                summary.Add(("Most Active Module", topModule));

                // Action counts per role.
                foreach (var grp in ActivityLogItems
                             .GroupBy(a => string.IsNullOrWhiteSpace(a.Role) ? "Unspecified" : a.Role)
                             .OrderByDescending(g => g.Count()))
                {
                    summary.Add(($"{grp.Key} Actions", grp.Count().ToString("N0")));
                }
            }
            else
            {
                charts = DualLineChartSvg(
                    "Patient Visits Trend",
                    PatientVisitTrend.Select(p => (p.Label, p.Value1, p.Value2)),
                    "Scheduled", "#0EA5E9",
                    "Walk-in", "#50C878",
                    PatientVisitInsight);

                headerCells = Th("Date", "Patient ID", "Patient Name", "Visit Type", "Service", "Dentist");
                totalRows = PatientVisitsItems.Count;
                foreach (PatientVisitReportItem p in PatientVisitsItems.Take(rowLimit))
                    rows.Append(Tr(p.Date, p.PatientCode, p.PatientName, p.VisitType, p.Service, p.Dentist));

                double scheduled = PatientVisitTrend.Sum(p => p.Value1);
                double walkIn = PatientVisitTrend.Sum(p => p.Value2);

                // Busiest day/period from the trend.
                var busiest = PatientVisitTrend
                    .OrderByDescending(p => p.Value1 + p.Value2)
                    .FirstOrDefault();
                string busiestLabel = busiest != null && (busiest.Value1 + busiest.Value2) > 0
                    ? busiest.Label
                    : "—";

                string topService = PatientVisitsItems
                    .Where(p => !string.IsNullOrWhiteSpace(p.Service))
                    .GroupBy(p => p.Service)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault() ?? "—";
                string topDentist = PatientVisitsItems
                    .Where(p => !string.IsNullOrWhiteSpace(p.Dentist))
                    .GroupBy(p => p.Dentist)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault() ?? "—";
                int activeDays = PatientVisitsItems.Select(p => p.Date).Distinct().Count();
                string avgPerDay = activeDays > 0
                    ? ((double)PatientVisitsItems.Count / activeDays).ToString("N1")
                    : "0";

                summary.Add(("Total Visits", PatientVisitsItems.Count.ToString("N0")));
                summary.Add(("Scheduled", scheduled.ToString("N0")));
                summary.Add(("Walk-in", walkIn.ToString("N0")));
                summary.Add(("Busiest Day / Period", busiestLabel));
                summary.Add(("Top Service", topService));
                summary.Add(("Top Dentist", topDentist));
                summary.Add(("Avg Visits / Active Day", avgPerDay));
            }

            int columnCount = headerCells.Split("<th").Length - 1;
            if (rows.Length == 0)
                rows.Append($"<tr><td colspan=\"{columnCount}\" class=\"empty\">No records for the selected period.</td></tr>");

            StringBuilder cards = new();
            foreach (var (label, value) in summary)
                cards.Append($@"<li><span class=""sum-lbl"">{HtmlEncode(label)}:</span> <span class=""sum-val"">{HtmlEncode(value)}</span></li>");

            // Sections included per the Print options dialog, in order:
            // charts → summary → detailed records.
            string chartsSection = PrintIncludeCharts
                ? $@"<h2 class=""section"">Overview</h2>
    <div class=""charts"">{charts}</div>"
                : string.Empty;

            string summarySection = PrintIncludeSummary
                ? $@"<h2 class=""section"">Summary</h2>
    <ul class=""sumlist"">{cards}</ul>"
                : string.Empty;

            string detailedSection = PrintIncludeDetailedRecords
                ? $@"<h2 class=""section"">Detailed Records</h2>
    <table>
        <thead><tr>{headerCells}</tr></thead>
        <tbody>{rows}</tbody>
    </table>"
                : string.Empty;

            string generatedBy = SessionService.GetCurrentUserFullName();
            if (string.IsNullOrWhiteSpace(generatedBy)) generatedBy = "—";

            string generatedOn = DateTime.Now.ToString("MMMM dd, yyyy h:mm tt");

            Version? v = Assembly.GetExecutingAssembly().GetName().Version;
            string appVersion = v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v1.0";

            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"" />
<style>
    * {{ box-sizing: border-box; }}
    body {{ font-family: 'Segoe UI', Arial, sans-serif; color: #1f2430; margin: 26px 32px 70px 32px; }}
    .report-header {{ display: flex; align-items: flex-start; border-bottom: 3px solid #073C98; padding-bottom: 12px; margin-bottom: 16px; }}
    .logo {{ width: 46px; height: 46px; border-radius: 10px; background: #073C98; color: #fff; font-weight: 700; font-size: 20px; display: flex; align-items: center; justify-content: center; margin-right: 14px; }}
    .logo-img {{ width: 48px; height: 48px; object-fit: contain; margin-right: 14px; }}
    .head-text {{ flex: 1; }}
    .clinic {{ font-size: 20px; font-weight: 700; color: #073C98; }}
    .report-title {{ font-size: 17px; font-weight: 700; margin-top: 2px; }}
    .head-meta {{ text-align: right; font-size: 11px; color: #666; line-height: 1.5; }}
    .head-meta b {{ color: #333; }}
    h2.section {{ font-size: 14px; color: #223357; margin: 18px 0 8px 0; }}
    .sumlist {{ list-style: none; margin: 0; padding: 0; columns: 2; column-gap: 40px; font-size: 13px; line-height: 2; }}
    .sumlist li {{ break-inside: avoid; border-bottom: 1px dotted #e2e2e2; }}
    .sum-lbl {{ color: #223357; font-weight: 600; }}
    .sum-val {{ font-weight: 700; float: right; }}
    table {{ width: 100%; border-collapse: collapse; font-size: 11px; table-layout: fixed; }}
    th {{ background: #223357; color: #fff; text-align: left; padding: 7px 9px; }}
    td {{ padding: 6px 9px; border-bottom: 1px solid #e2e2e2; word-wrap: break-word; overflow-wrap: anywhere; }}
    tr:nth-child(even) td {{ background: #f6f8fb; }}
    .empty {{ text-align: center; color: #888; padding: 24px; font-style: italic; }}
    .charts {{ display: flex; flex-wrap: nowrap; gap: 18px; align-items: stretch; }}
    .chart {{ border: 1px solid #e8e8e8; border-radius: 8px; padding: 12px 14px; flex: 1 1 0; min-width: 0; }}
    .chart h2 {{ font-size: 13px; margin: 0 0 8px 0; color: #223357; }}
    .chart-note {{ font-size: 11px; color: #555; margin: 8px 0 0 0; line-height: 1.4; }}
    .legend {{ font-size: 11px; color: #555; margin-top: 6px; }}
    .legend span {{ display: inline-block; width: 10px; height: 10px; border-radius: 2px; margin: 0 4px 0 12px; vertical-align: middle; }}
    .pie-legend {{ font-size: 11px; color: #555; }}
    .pie-leg {{ margin: 3px 0; }}
    .pie-leg span {{ display: inline-block; width: 10px; height: 10px; border-radius: 2px; margin-right: 6px; vertical-align: middle; }}
    .report-footer {{ position: fixed; bottom: 0; left: 0; right: 0; border-top: 1px solid #ddd; padding: 8px 32px; font-size: 10px; color: #888; display: flex; justify-content: space-between; background: #fff; }}
    @media print {{ body {{ margin: 0 0 70px 0; padding: 26px 32px; }} .chart, .stat {{ break-inside: avoid; }} thead {{ display: table-header-group; }} tr {{ break-inside: avoid; }} }}
</style>
</head>
<body>
    <div class=""report-header"">
        {GetLogoHtml()}
        <div class=""head-text"">
            <div class=""clinic"">Cruz Nery Dental Clinic</div>
            <div class=""report-title"">{HtmlEncode(title)}</div>
        </div>
        <div class=""head-meta"">
            <div><b>Period:</b> {HtmlEncode(ShowingText.Replace("Showing: ", string.Empty))}</div>
            <div><b>Generated by:</b> {HtmlEncode(generatedBy)}</div>
            <div><b>Date:</b> {HtmlEncode(generatedOn)}</div>
        </div>
    </div>

    {chartsSection}

    {summarySection}

    {detailedSection}

    <div class=""report-footer"">
        <span>Cruz Nery Clinic Management System {appVersion}</span>
        <span>{HtmlEncode(title)} &nbsp;•&nbsp; {HtmlEncode(generatedOn)}</span>
    </div>
</body>
</html>";
        }

        // Inline SVG bar chart for a single data series.
        private static string BarChartSvg(string title, IEnumerable<(string Label, double Value)> data, string color, string unitPrefix, string interpretation = "")
        {
            var points = data.ToList();

            if (points.Count == 0)
                return $@"<div class=""chart""><h2>{HtmlEncode(title)}</h2><div class=""empty"">No chart data.</div>{NoteHtml(interpretation)}</div>";

            double max = points.Max(p => p.Value);
            if (max <= 0) max = 1;

            const int width = 460, height = 200, padLeft = 8, padBottom = 34, padTop = 8;
            int plotH = height - padBottom - padTop;
            double slot = (double)(width - padLeft) / points.Count;
            double barW = Math.Max(6, slot * 0.6);

            StringBuilder bars = new();
            for (int i = 0; i < points.Count; i++)
            {
                double v = points[i].Value;
                double h = v / max * plotH;
                double x = padLeft + i * slot + (slot - barW) / 2;
                double y = padTop + (plotH - h);

                string valueText = unitPrefix == "₱" ? $"₱{v:N0}" : Inv(v);

                bars.Append($@"<rect x=""{Inv(x)}"" y=""{Inv(y)}"" width=""{Inv(barW)}"" height=""{Inv(h)}"" fill=""{color}"" rx=""2"" />");
                bars.Append($@"<text x=""{Inv(x + barW / 2)}"" y=""{Inv(y - 3)}"" font-size=""8"" fill=""#444"" text-anchor=""middle"">{HtmlEncode(valueText)}</text>");
                bars.Append($@"<text x=""{Inv(x + barW / 2)}"" y=""{height - padBottom + 12}"" font-size=""8"" fill=""#777"" text-anchor=""middle"">{HtmlEncode(ShortLabel(points[i].Label))}</text>");
            }

            return $@"<div class=""chart""><h2>{HtmlEncode(title)}</h2>
<svg viewBox=""0 0 {width} {height}"" width=""100%"" preserveAspectRatio=""xMidYMid meet"">
<line x1=""{padLeft}"" y1=""{height - padBottom}"" x2=""{width}"" y2=""{height - padBottom}"" stroke=""#ddd"" />
{bars}
</svg>{NoteHtml(interpretation)}</div>";
        }

        // Inline SVG smooth area chart for a single series (matches the on-screen revenue chart).
        private static string AreaChartSvg(string title, IEnumerable<(string Label, double Value)> data, string color, string unitPrefix, string interpretation = "")
        {
            var points = data.ToList();

            if (points.Count == 0)
                return $@"<div class=""chart""><h2>{HtmlEncode(title)}</h2><div class=""empty"">No chart data.</div>{NoteHtml(interpretation)}</div>";

            double max = points.Max(p => p.Value);
            if (max <= 0) max = 1;

            const int width = 460, height = 200, padLeft = 8, padRight = 8, padBottom = 34, padTop = 10;
            int plotH = height - padBottom - padTop;
            int plotW = width - padLeft - padRight;
            double baseline = height - padBottom;
            double step = points.Count > 1 ? (double)plotW / (points.Count - 1) : plotW;

            var pts = new List<(double X, double Y)>();
            StringBuilder labels = new();
            for (int i = 0; i < points.Count; i++)
            {
                double x = padLeft + i * step;
                double y = padTop + (plotH - points[i].Value / max * plotH);
                pts.Add((x, y));
                labels.Append($@"<text x=""{Inv(x)}"" y=""{height - padBottom + 12}"" font-size=""8"" fill=""#777"" text-anchor=""middle"">{HtmlEncode(ShortLabel(points[i].Label))}</text>");
            }

            string lineD = SmoothPathData(pts);
            // Area = drop to baseline, follow the smooth line, drop back to baseline, close.
            string areaD = $"M {Inv(pts[0].X)},{Inv(baseline)} L{lineD.Substring(1)} L {Inv(pts[pts.Count - 1].X)},{Inv(baseline)} Z";

            return $@"<div class=""chart""><h2>{HtmlEncode(title)}</h2>
<svg viewBox=""0 0 {width} {height}"" width=""100%"" preserveAspectRatio=""xMidYMid meet"">
<line x1=""{padLeft}"" y1=""{Inv(baseline)}"" x2=""{width - padRight}"" y2=""{Inv(baseline)}"" stroke=""#ddd"" />
<path d=""{areaD}"" fill=""{color}"" fill-opacity=""0.18"" />
<path d=""{lineD}"" fill=""none"" stroke=""{color}"" stroke-width=""2.2"" stroke-linejoin=""round"" stroke-linecap=""round"" />
{labels}
</svg>{NoteHtml(interpretation)}</div>";
        }

        // Builds an SVG path 'd' string through the points using Catmull-Rom → Bézier
        // smoothing (same curve as the on-screen charts).
        private static string SmoothPathData(IReadOnlyList<(double X, double Y)> pts)
        {
            if (pts.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.Append($"M {Inv(pts[0].X)},{Inv(pts[0].Y)}");

            if (pts.Count < 3)
            {
                for (int i = 1; i < pts.Count; i++)
                    sb.Append($" L {Inv(pts[i].X)},{Inv(pts[i].Y)}");
                return sb.ToString();
            }

            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p0 = pts[Math.Max(i - 1, 0)];
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var p3 = pts[Math.Min(i + 2, pts.Count - 1)];

                double c1x = p1.X + (p2.X - p0.X) / 6.0;
                double c1y = p1.Y + (p2.Y - p0.Y) / 6.0;
                double c2x = p2.X - (p3.X - p1.X) / 6.0;
                double c2y = p2.Y - (p3.Y - p1.Y) / 6.0;

                sb.Append($" C {Inv(c1x)},{Inv(c1y)} {Inv(c2x)},{Inv(c2y)} {Inv(p2.X)},{Inv(p2.Y)}");
            }
            return sb.ToString();
        }

        // Renders a chart interpretation paragraph (empty when no text).
        private static string NoteHtml(string interpretation)
            => string.IsNullOrWhiteSpace(interpretation)
                ? string.Empty
                : $@"<p class=""chart-note"">{HtmlEncode(interpretation)}</p>";

        // Inline SVG grouped bar chart for a two-series (e.g. Scheduled vs Walk-in) trend.
        private static string GroupedBarChartSvg(
            string title,
            IEnumerable<(string Label, double Value1, double Value2)> data,
            string name1, string color1,
            string name2, string color2)
        {
            var points = data.ToList();

            if (points.Count == 0)
                return $@"<div class=""chart""><h2>{HtmlEncode(title)}</h2><div class=""empty"">No chart data.</div></div>";

            double max = points.Max(p => Math.Max(p.Value1, p.Value2));
            if (max <= 0) max = 1;

            const int width = 460, height = 200, padLeft = 8, padBottom = 34, padTop = 8;
            int plotH = height - padBottom - padTop;
            double slot = (double)(width - padLeft) / points.Count;
            double barW = Math.Max(4, slot * 0.28);

            StringBuilder bars = new();
            for (int i = 0; i < points.Count; i++)
            {
                double h1 = points[i].Value1 / max * plotH;
                double h2 = points[i].Value2 / max * plotH;
                double groupX = padLeft + i * slot + (slot - barW * 2 - 3) / 2;

                double x1 = groupX, y1 = padTop + (plotH - h1);
                double x2 = groupX + barW + 3, y2 = padTop + (plotH - h2);

                bars.Append($@"<rect x=""{Inv(x1)}"" y=""{Inv(y1)}"" width=""{Inv(barW)}"" height=""{Inv(h1)}"" fill=""{color1}"" rx=""2"" />");
                bars.Append($@"<rect x=""{Inv(x2)}"" y=""{Inv(y2)}"" width=""{Inv(barW)}"" height=""{Inv(h2)}"" fill=""{color2}"" rx=""2"" />");
                bars.Append($@"<text x=""{Inv(groupX + barW)}"" y=""{height - padBottom + 12}"" font-size=""8"" fill=""#777"" text-anchor=""middle"">{HtmlEncode(ShortLabel(points[i].Label))}</text>");
            }

            return $@"<div class=""chart""><h2>{HtmlEncode(title)}</h2>
<svg viewBox=""0 0 {width} {height}"" width=""100%"" preserveAspectRatio=""xMidYMid meet"">
<line x1=""{padLeft}"" y1=""{height - padBottom}"" x2=""{width}"" y2=""{height - padBottom}"" stroke=""#ddd"" />
{bars}
</svg>
<div class=""legend""><span style=""background:{color1}""></span>{HtmlEncode(name1)}<span style=""background:{color2}""></span>{HtmlEncode(name2)}</div></div>";
        }

        // Inline SVG two-series line chart (Scheduled vs Walk-in patient visits),
        // matching the on-screen line graph.
        private static string DualLineChartSvg(
            string title,
            IEnumerable<(string Label, double Value1, double Value2)> data,
            string name1, string color1,
            string name2, string color2,
            string interpretation = "")
        {
            var points = data.ToList();

            if (points.Count == 0)
                return $@"<div class=""chart""><h2>{HtmlEncode(title)}</h2><div class=""empty"">No chart data.</div>{NoteHtml(interpretation)}</div>";

            double max = points.Max(p => Math.Max(p.Value1, p.Value2));
            if (max <= 0) max = 1;

            const int width = 460, height = 200, padLeft = 8, padRight = 8, padBottom = 34, padTop = 8;
            int plotH = height - padBottom - padTop;
            int plotW = width - padLeft - padRight;
            double step = points.Count > 1 ? (double)plotW / (points.Count - 1) : plotW;

            var pts1 = new List<(double X, double Y)>();
            var pts2 = new List<(double X, double Y)>();
            StringBuilder marks = new();
            StringBuilder labels = new();

            for (int i = 0; i < points.Count; i++)
            {
                double x = padLeft + i * step;
                double y1 = padTop + (plotH - points[i].Value1 / max * plotH);
                double y2 = padTop + (plotH - points[i].Value2 / max * plotH);

                pts1.Add((x, y1));
                pts2.Add((x, y2));

                marks.Append($@"<circle cx=""{Inv(x)}"" cy=""{Inv(y1)}"" r=""2.6"" fill=""{color1}"" />");
                marks.Append($@"<circle cx=""{Inv(x)}"" cy=""{Inv(y2)}"" r=""2.6"" fill=""{color2}"" />");

                labels.Append($@"<text x=""{Inv(x)}"" y=""{height - padBottom + 12}"" font-size=""8"" fill=""#777"" text-anchor=""middle"">{HtmlEncode(ShortLabel(points[i].Label))}</text>");
            }

            return $@"<div class=""chart""><h2>{HtmlEncode(title)}</h2>
<svg viewBox=""0 0 {width} {height}"" width=""100%"" preserveAspectRatio=""xMidYMid meet"">
<line x1=""{padLeft}"" y1=""{height - padBottom}"" x2=""{width - padRight}"" y2=""{height - padBottom}"" stroke=""#ddd"" />
<path d=""{SmoothPathData(pts1)}"" fill=""none"" stroke=""{color1}"" stroke-width=""2"" stroke-linejoin=""round"" stroke-linecap=""round"" />
<path d=""{SmoothPathData(pts2)}"" fill=""none"" stroke=""{color2}"" stroke-width=""2"" stroke-linejoin=""round"" stroke-linecap=""round"" />
{marks}
{labels}
</svg>
<div class=""legend""><span style=""background:{color1}""></span>{HtmlEncode(name1)}<span style=""background:{color2}""></span>{HtmlEncode(name2)}</div>{NoteHtml(interpretation)}</div>";
        }

        // Inline SVG pie chart (used for Activity by Type).
        private static string PieChartSvg(string title, IEnumerable<PieChartSlice> slices, string interpretation = "")
        {
            var data = slices.Where(s => s.Value > 0).ToList();

            if (data.Count == 0)
                return $@"<div class=""chart""><h2>{HtmlEncode(title)}</h2><div class=""empty"">No chart data.</div>{NoteHtml(interpretation)}</div>";

            double total = data.Sum(s => s.Value);
            const double cx = 90, cy = 100, r = 80;

            StringBuilder paths = new();
            StringBuilder legend = new();
            double angle = -90; // start at top

            foreach (PieChartSlice s in data)
            {
                double sweep = s.Value / total * 360.0;
                double end = angle + sweep;

                double x1 = cx + r * Math.Cos(angle * Math.PI / 180);
                double y1 = cy + r * Math.Sin(angle * Math.PI / 180);
                double x2 = cx + r * Math.Cos(end * Math.PI / 180);
                double y2 = cy + r * Math.Sin(end * Math.PI / 180);
                int largeArc = sweep > 180 ? 1 : 0;

                // A single full slice would collapse the arc; draw a full circle instead.
                if (data.Count == 1)
                    paths.Append($@"<circle cx=""{Inv(cx)}"" cy=""{Inv(cy)}"" r=""{Inv(r)}"" fill=""{s.HexColor}"" />");
                else
                    paths.Append($@"<path d=""M{Inv(cx)},{Inv(cy)} L{Inv(x1)},{Inv(y1)} A{Inv(r)},{Inv(r)} 0 {largeArc},1 {Inv(x2)},{Inv(y2)} Z"" fill=""{s.HexColor}"" />");

                double pct = s.Value / total * 100.0;
                legend.Append($@"<div class=""pie-leg""><span style=""background:{s.HexColor}""></span>{HtmlEncode(s.Label)} — {Inv(pct)}%</div>");

                angle = end;
            }

            return $@"<div class=""chart""><h2>{HtmlEncode(title)}</h2>
<div style=""display:flex;align-items:center;gap:16px;"">
<svg viewBox=""0 0 180 200"" width=""150"" height=""166"">{paths}</svg>
<div class=""pie-legend"">{legend}</div>
</div>{NoteHtml(interpretation)}</div>";
        }

        // Trims long axis labels (e.g. dates) so the chart stays readable.
        private static string ShortLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return string.Empty;
            return label.Length > 6 ? label.Substring(label.Length - 5) : label;
        }

        // Embeds the clinic logo as a base64 data URI so it renders inside the
        // WebView2 NavigateToString preview. Falls back to a text badge.
        private static string GetLogoHtml()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Images", "cruz-nery-logo.png");
                if (File.Exists(path))
                {
                    string base64 = Convert.ToBase64String(File.ReadAllBytes(path));
                    return $@"<img class=""logo-img"" src=""data:image/png;base64,{base64}"" alt=""Clinic logo"" />";
                }
            }
            catch
            {
                // Fall through to the text badge.
            }

            return @"<div class=""logo"">CN</div>";
        }

        private static string Inv(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static string Th(params string[] headers)
            => string.Concat(headers.Select(h => $"<th>{HtmlEncode(h)}</th>"));

        private static string Tr(params string[] cells)
            => $"<tr>{string.Concat(cells.Select(c => $"<td>{HtmlEncode(c)}</td>"))}</tr>";

        private static string HtmlEncode(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        private void LoadData()
        {
            var (from, to) = GetDateRange();

            if (IsPatientVisitsSelected)
            {
                PatientVisitsItems.Clear();
                foreach (var item in _repository.GetPatientVisits(from, to))
                    PatientVisitsItems.Add(item);
                PatientVisitTrend = _repository.GetPatientVisitTrend(from, to);
                PatientVisitInsight = ChartInsight.SummarizeDual(PatientVisitTrend, "scheduled", "walk-in");
            }
            else if (IsTransactionReportsSelected)
            {
                TransactionItems.Clear();
                foreach (var item in _repository.GetTransactions(from, to))
                    TransactionItems.Add(item);
                RevenueTrend = _repository.GetRevenueTrend(from, to);
                DailyTransactionCounts = _repository.GetDailyTransactionCounts(from, to);
                RevenueInsight = ChartInsight.Summarize(RevenueTrend, "days", "₱");
                DailyTransactionsInsight = ChartInsight.Summarize(DailyTransactionCounts, "days");
            }
            else if (IsInventoryReportsSelected)
            {
                InventoryItems.Clear();
                foreach (var item in _repository.GetInventoryItems())
                    InventoryItems.Add(item);
                InventoryChartData = _repository.GetInventoryChartData();
                InventoryInsight = ChartInsight.Summarize(InventoryChartData, "items");
            }
            else if (IsUserActivityLogSelected)
            {
                ActivityLogItems.Clear();
                foreach (var item in _repository.GetActivityLogs(from, to))
                    ActivityLogItems.Add(item);
                ActivityByType = _repository.GetActivityByType(from, to);
                ActivityByModule = _repository.GetActivityByModule(from, to);
                ActivityByTypeInsight = ChartInsight.SummarizePie(ActivityByType);
                ActivityByModuleInsight = ChartInsight.Summarize(ActivityByModule, "modules");
            }

            ChartDataRefreshed?.Invoke();
        }
    }
}
