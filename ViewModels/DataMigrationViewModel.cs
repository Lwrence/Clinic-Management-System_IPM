using CommunityToolkit.Mvvm.Input;
using CruzNeryClinic.Models.Migration;
using CruzNeryClinic.Models;
using CruzNeryClinic.Repositories;
using CruzNeryClinic.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;

namespace CruzNeryClinic.ViewModels;

public class DataMigrationViewModel : BaseViewModel
{
    private readonly WordPatientExtractionService extractor;
    private readonly PatientMigrationRepository repository;
    private readonly List<string> selectedDocumentPaths = new();
    private readonly HashSet<string> savedPatientCodes = new(StringComparer.Ordinal);
    private PatientMigrationDraft? selectedDraft;
    private MigrationResultItem? selectedHistoryItem;
    private MigrationSnapshot? snapshot;
    private bool refreshing, isBusy, hasConfirmedSave, viewingBatchResults, isValidationExpanded;
    private int currentStage = 1, historyReturnStage = 1, sessionGeneration;
    private string historyReturnStatus = "Select a Word patient record to begin.";
    private string statusMessage = "Select a Word patient record to begin.";
    private string fileIssues = "";
    private string resultMessage = "";
    private bool isCheckResultOpen, checkPassed, updatingServiceSelection;
    private string checkResultTitle = "", checkResultMessage = "";
    private static readonly HashSet<string> EditableProperties = new()
    {
        "IncludeInImport", "IsReviewed", "AcknowledgeWarnings", "HasConsent", "UnparsedName",
        "FirstName", "MiddleName", "LastName", "PhoneNumber", "BirthDateText", "Gender", "Address",
        "InitialTreatment", "MedicalConditionNotes", "AllergyNotes", "CurrentMedication",
        "ClearanceNotes", "InitialTreatmentNotes", "IsPwd", "HasMedicalCondition", "RequiresMedicalClearance"
    };

    public DataMigrationViewModel() : this(new PatientMigrationRepository(), new WordPatientExtractionService()) { }
    public DataMigrationViewModel(PatientMigrationRepository repository, WordPatientExtractionService? extractor = null)
    {
        this.repository = repository;
        this.extractor = extractor ?? new();
        SelectDocumentsCommand = new RelayCommand(ChooseDocuments, () => CanUseModule() && IsUploadStage);
        ExtractDocumentsCommand = new AsyncRelayCommand(
            () => LoadDocumentsAsync(selectedDocumentPaths.ToArray()),
            () => CanUseModule() && IsUploadStage && HasSelectedDocuments);
        DismissCheckResultCommand = new RelayCommand(ClearCheckResult);
        ValidateCommand = new RelayCommand(CheckInformation, () => CanUseModule() && Drafts.Any(x => !x.IsImported));
        ContinueCommand = new RelayCommand(Continue, () => CanUseModule() &&
            (CurrentStage == 1 ? Drafts.Any(x => !x.IsImported) : CurrentStage == 2 && CanContinueToConfirmation));
        BackCommand = new RelayCommand(Back, () => CanUseModule() && ShowBack);
        ImportCommand = new AsyncRelayCommand(SaveApprovedAsync, CanSave);
        StartNewCommand = new RelayCommand(StartNew, CanUseModule);
        ViewHistoryCommand = new RelayCommand(() => OpenHistory(false), CanUseModule);
        ViewResultsCommand = new RelayCommand(() => OpenHistory(true), () => CanUseModule() && savedPatientCodes.Count > 0);
        Drafts.CollectionChanged += DraftCollectionChanged;
    }

    public ObservableCollection<PatientValidationCheckResult> CheckResults { get; } = new();
    public bool HasCheckResults => CheckResults.Count > 0;
    public bool IsCheckResultOpen
    {
        get => isCheckResultOpen;
        private set
        {
            if (SetProperty(ref isCheckResultOpen, value)) OnPropertyChanged(nameof(CanInteract));
        }
    }
    public bool CheckPassed { get => checkPassed; private set => SetProperty(ref checkPassed, value); }
    public string CheckResultTitle { get => checkResultTitle; private set => SetProperty(ref checkResultTitle, value); }
    public string CheckResultMessage { get => checkResultMessage; private set => SetProperty(ref checkResultMessage, value); }
    public ObservableCollection<PatientMigrationDraft> Drafts { get; } = new();
    public ObservableCollection<PatientMigrationDraft> ConfirmationRecords { get; } = new();
    public ObservableCollection<MigrationResultItem> HistoryItems { get; } = new();
    public IReadOnlyList<string> GenderOptions { get; } = new[] { "Male", "Female", "Other" };
    public PatientMigrationDraft? SelectedDraft
    {
        get => selectedDraft;
        set
        {
            bool wasUpdating = updatingServiceSelection;
            updatingServiceSelection = true;
            try
            {
                if (SetProperty(ref selectedDraft, value))
                {
                    OnPropertyChanged(nameof(HasSelectedDraft));
                    NotifySelectedService();
                }
            }
            finally { updatingServiceSelection = wasUpdating; }
        }
    }
    public IReadOnlyList<AppointmentServiceOption> ServiceOptions { get; private set; } = Array.Empty<AppointmentServiceOption>();
    public AppointmentServiceOption? SelectedInitialService
    {
        get => ServiceOptions.FirstOrDefault(x => string.Equals(x.ServiceName,
            SelectedDraft?.InitialTreatment.Trim(), StringComparison.OrdinalIgnoreCase));
        set
        {
            // Clearing or rebinding the list must not discard a historical service.
            if (!updatingServiceSelection && value != null && SelectedDraft?.CanEdit == true && ServiceOptions.Contains(value))
                SelectedDraft.InitialTreatment = value.ServiceName;
        }
    }
    public bool HasUnlistedInitialTreatment => SelectedDraft != null &&
        !string.IsNullOrWhiteSpace(SelectedDraft.InitialTreatment) && SelectedInitialService == null;
    public string UnlistedInitialTreatmentMessage =>
        $"Extracted service: {SelectedDraft?.InitialTreatment}. Choose a matching service if available; the original value is retained until you choose.";

    private void SetServiceOptions(IReadOnlyList<AppointmentServiceOption> options)
    {
        bool wasUpdating = updatingServiceSelection;
        updatingServiceSelection = true;
        try
        {
            ServiceOptions = options;
            OnPropertyChanged(nameof(ServiceOptions));
            NotifySelectedService();
        }
        finally { updatingServiceSelection = wasUpdating; }
    }

    private void NotifySelectedService()
    {
        OnPropertyChanged(nameof(SelectedInitialService));
        OnPropertyChanged(nameof(HasUnlistedInitialTreatment));
        OnPropertyChanged(nameof(UnlistedInitialTreatmentMessage));
    }

    public MigrationResultItem? SelectedHistoryItem { get => selectedHistoryItem; set => SetProperty(ref selectedHistoryItem, value); }
    public bool IsValidationExpanded
    {
        get => isValidationExpanded;
        set => SetProperty(ref isValidationExpanded, value);
    }
    public bool HasSelectedDraft => SelectedDraft != null;
    public bool HasDrafts => Drafts.Count > 0;
    public IEnumerable<MigrationResultItem> DisplayedHistoryItems => viewingBatchResults
        ? HistoryItems.Where(x => x.Result == "Saved" && savedPatientCodes.Contains(x.PatientCode))
        : HistoryItems;
    public bool HasHistory => DisplayedHistoryItems.Any();
    public string HistoryTitle => viewingBatchResults ? "Migration Results" : "Migration History";
    public string HistoryDescription => viewingBatchResults
        ? "Saved records from this migration, within the latest 100 tracked results."
        : "The latest 100 saved, rejected, and failed migration results.";
    public bool HasSelectedDocuments => selectedDocumentPaths.Count > 0;
    public string SelectedFileNames => HasSelectedDocuments
        ? string.Join("\n", selectedDocumentPaths.Select(Path.GetFileName))
        : "No Word file selected.";
    public bool HasFileIssues => !string.IsNullOrWhiteSpace(FileIssues);
    public string StatusMessage { get => statusMessage; set => SetProperty(ref statusMessage, value); }
    public string FileIssues
    {
        get => fileIssues;
        set { if (SetProperty(ref fileIssues, value)) OnPropertyChanged(nameof(HasFileIssues)); }
    }
    public string ResultMessage { get => resultMessage; set => SetProperty(ref resultMessage, value); }
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetProperty(ref isBusy, value)) return;
            OnPropertyChanged(nameof(CanInteract));
            NotifyCommands();
        }
    }
    public bool CanInteract => !IsBusy && !IsCheckResultOpen && SessionService.CanAccessModule("DataMigration");
    public bool HasConfirmedSave
    {
        get => hasConfirmedSave;
        set { if (SetProperty(ref hasConfirmedSave, value)) ImportCommand.NotifyCanExecuteChanged(); }
    }
    // The workflow has three steps; completion and history are separate views.
    public int CurrentStage
    {
        get => currentStage;
        private set
        {
            if (!SetProperty(ref currentStage, value)) return;
            foreach (string name in new[] { nameof(IsUploadStage), nameof(IsReviewStage), nameof(IsConfirmStage),
                nameof(IsCompletionStage), nameof(IsHistoryStage), nameof(IsWorkflowStage), nameof(ContinueButtonText),
                nameof(ShowBack), nameof(BackButtonText), nameof(ShowResumeReview) }) OnPropertyChanged(name);
            NotifyCommands();
        }
    }
    public bool ShowBack => CurrentStage is 2 or 3 or 5;
    public string BackButtonText => IsHistoryStage ? "Return to Migration" : "Back";
    public bool ShowResumeReview => IsUploadStage && Drafts.Any(x => !x.IsImported);
    public bool IsWorkflowStage => CurrentStage is 1 or 2 or 3;
    public bool IsUploadStage => CurrentStage == 1;
    public bool IsReviewStage => CurrentStage == 2;
    public bool IsConfirmStage => CurrentStage == 3;
    public bool IsCompletionStage => CurrentStage == 4;
    public bool IsHistoryStage => CurrentStage == 5;
    public int ExtractedCount => Drafts.Count;
    public int SelectedCount => Drafts.Count(x => x.IncludeInImport && !x.IsImported);
    public int ApprovedCount => Drafts.Count(x => x.IncludeInImport && !x.IsImported && x.IsReviewed && x.ValidatedPatient != null);
    public int NeedsCorrectionCount => Drafts.Count(x => x.IncludeInImport && !x.IsImported && x.HasValidationErrors);
    public string Summary => $"{ExtractedCount} extracted · {SelectedCount} selected · {ApprovedCount} approved";
    public string ConfirmationSummary => $"Save {ConfirmationRecords.Count} approved patient record(s)";
    public string ReviewerDisplay => $"{(string.IsNullOrWhiteSpace(SessionService.GetCurrentUserFullName()) ? SessionService.CurrentUser?.Username : SessionService.GetCurrentUserFullName())} ({SessionService.GetCurrentUserRole()})";
    public string ContinueButtonText => CurrentStage == 1 ? "Resume Review" : "Continue to Confirmation";
    public bool CanContinueToConfirmation => snapshot != null && SelectedCount > 0 &&
        Drafts.Where(x => x.IncludeInImport && !x.IsImported).All(x => x.IsReviewed && x.ValidatedPatient != null);

    public IRelayCommand SelectDocumentsCommand { get; }
    public IAsyncRelayCommand ExtractDocumentsCommand { get; }
    public IRelayCommand ValidateCommand { get; }
    public IRelayCommand DismissCheckResultCommand { get; }
    public IRelayCommand ContinueCommand { get; }
    public IRelayCommand BackCommand { get; }
    public IAsyncRelayCommand ImportCommand { get; }
    public IRelayCommand StartNewCommand { get; }
    public IRelayCommand ViewHistoryCommand { get; }
    public IRelayCommand ViewResultsCommand { get; }
    private bool CanUseModule() => !IsBusy && SessionService.CanAccessModule("DataMigration");

    private void ChooseDocuments()
    {
        if (!CanUseModule() || !IsUploadStage) return;
        OpenFileDialog dialog = new() { Filter = "Word patient records (*.docx)|*.docx", Multiselect = true };
        if (dialog.ShowDialog() == true) SelectDocuments(dialog.FileNames);
    }

    public void SelectDocuments(IEnumerable<string> paths)
    {
        if (!CanUseModule()) throw new UnauthorizedAccessException("Sign in with an authorized clinic account before selecting patient records.");
        if (!IsUploadStage) throw new InvalidOperationException("Return to Upload before selecting another Word record.");
        var selected = paths.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        selectedDocumentPaths.Clear();
        selectedDocumentPaths.AddRange(selected);
        FileIssues = "";
        StatusMessage = HasSelectedDocuments ? "Selected Word records are ready for extraction." : "Select a Word patient record to begin.";
        OnPropertyChanged(nameof(HasSelectedDocuments));
        OnPropertyChanged(nameof(SelectedFileNames));
        NotifyCommands();
    }

    public async Task LoadDocumentsAsync(IEnumerable<string> paths)
    {
        if (!CanUseModule()) throw new UnauthorizedAccessException("Sign in with an authorized clinic account before uploading patient records.");
        var files = paths.ToArray();
        if (files.Length == 0) return;
        int generation = sessionGeneration;
        IsBusy = true;
        StatusMessage = "Extracting available patient information from Word...";
        List<string> issues = new();
        try
        {
            foreach (string path in files)
            {
                try
                {
                    var extracted = await Task.Run(() => extractor.Extract(path));
                    if (generation != sessionGeneration) return;
                    foreach (var draft in extracted)
                    {
                        if (Drafts.Any(x => x.SourceHash == draft.SourceHash && x.SourceRecordNumber == draft.SourceRecordNumber)) continue;
                        draft.IncludeInImport = true;
                        Drafts.Add(draft);
                    }
                }
                catch (Exception ex)
                {
                    if (generation != sessionGeneration) return;
                    string detail = $"{Path.GetFileName(path)}: {ex.Message}";
                    issues.Add(detail);
                    if (!TryRecordFailure(Path.GetFileName(path), 0, "Rejected", ex.Message))
                        issues.Add("This upload result could not be recorded. Check database connectivity.");
                }
            }
            FileIssues = string.Join("\n", issues);
            SelectedDraft ??= Drafts.FirstOrDefault(x => !x.IsImported);
            Validate();
            if (Drafts.Any(x => !x.IsImported)) CurrentStage = 2;
            else StatusMessage = "No supported patient records were extracted. Select an actual Word patient record.";
        }
        finally { if (generation == sessionGeneration) { IsBusy = false; RefreshCounts(); } }
    }

    private void DraftCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (PatientMigrationDraft draft in e.OldItems) draft.PropertyChanged -= DraftChanged;
        if (e.NewItems != null) foreach (PatientMigrationDraft draft in e.NewItems) draft.PropertyChanged += DraftChanged;
        RefreshCounts();
    }

    private void DraftChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (refreshing || e.PropertyName == null || !EditableProperties.Contains(e.PropertyName)) return;
        ClearCheckResult();
        if (e.PropertyName == nameof(PatientMigrationDraft.InitialTreatment)) NotifySelectedService();
        HasConfirmedSave = false;
        if (CurrentStage == 3) CurrentStage = 2;
        RefreshValidation();
    }

    private void CheckInformation()
    {
        if (!CanUseModule()) return;
        ClearCheckResult();
        if (!Validate())
        {
            ShowCheckResult("Information check unavailable", StatusMessage, false);
            return;
        }
        var selected = Drafts.Where(x => x.IncludeInImport && !x.IsImported).ToArray();
        if (selected.Length == 0)
        {
            IsValidationExpanded = false;
            StatusMessage = "No patient records selected. Check a record in the extracted list, then check information again.";
            ShowCheckResult("No patient records selected", StatusMessage, false);
            return;
        }

        if (SelectedDraft == null || !selected.Contains(SelectedDraft) || !SelectedDraft.HasValidationErrors)
            SelectedDraft = selected.FirstOrDefault(x => x.HasValidationErrors)
                ?? (SelectedDraft != null && selected.Contains(SelectedDraft) ? SelectedDraft : selected[0]);
        IsValidationExpanded = true;

        int corrections = selected.Count(x => x.HasValidationErrors);
        int warnings = selected.Count(x => x.HasValidationWarnings);
        int awaitingApproval = selected.Count(x => !x.HasValidationErrors && !x.IsReviewed);
        string nextStep = corrections > 0 ? "Review the validation details for the selected patient."
            : awaitingApproval > 0 ? "Checks passed. Review and approve each selected patient."
            : "All selected patients are approved. Continue to Confirmation.";
        StatusMessage = $"Information checked at {DateTime.Now:HH:mm:ss}: {selected.Length} selected; " +
            $"{corrections} need correction; {warnings} have warnings; {awaitingApproval} await approval. {nextStep}";

        foreach (var draft in selected.Where(x => x.HasValidationErrors || x.HasValidationWarnings))
            CheckResults.Add(new(draft.PatientDisplay, draft.SourceDisplay,
                draft.ValidationErrors.Concat(draft.ValidationWarnings.Select(x => $"Verify: {x}")).ToArray()));
        OnPropertyChanged(nameof(HasCheckResults));

        string title = corrections > 0 ? "Patient records need attention"
            : warnings > 0 ? "Required information is complete"
            : "Information check passed";
        string message = corrections > 0
            ? $"{corrections} of {selected.Length} selected patient record(s) need correction. Complete or correct the items listed below, then check information again."
            : warnings > 0
                ? $"No missing or invalid required information was found in {selected.Length} selected patient record(s). Review the warnings listed below before approving. {nextStep}"
                : $"All {selected.Length} selected patient record(s) have complete required information and no validation issues. {nextStep}";
        ShowCheckResult(title, message, corrections == 0 && warnings == 0);
    }

    private void ShowCheckResult(string title, string message, bool passed)
    {
        CheckResultTitle = title;
        CheckResultMessage = message;
        CheckPassed = passed;
        IsCheckResultOpen = true;
    }

    private void ClearCheckResult()
    {
        IsCheckResultOpen = false;
        CheckResults.Clear();
        CheckPassed = false;
        CheckResultTitle = "";
        CheckResultMessage = "";
        OnPropertyChanged(nameof(HasCheckResults));
    }

    private bool Validate()
    {
        if (!SessionService.CanAccessModule("DataMigration"))
        {
            snapshot = null;
            StatusMessage = "Sign in with an authorized clinic account to review migration records.";
            NotifyCommands();
            return false;
        }
        try
        {
            snapshot = repository.GetSnapshot();
            SetServiceOptions(repository.GetActiveServices());
            RefreshValidation();
            StatusMessage = "Complete missing information, check possible duplicates, and approve each record to save.";
            return true;
        }
        catch (Exception ex)
        {
            snapshot = null;
            StatusMessage = $"Patient validation is unavailable: {ex.Message}";
            NotifyCommands();
            return false;
        }
    }

    private void RefreshValidation()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            var selected = Drafts.Where(x => x.IncludeInImport && !x.IsImported).ToArray();
            foreach (var draft in Drafts.Where(x => !x.IsImported))
            {
                var result = PatientMigrationValidationService.Validate(draft, snapshot?.Patients ?? new(), selected, DateTime.Today);
                if (snapshot?.ImportedSources.Contains($"{draft.SourceHash}:{draft.SourceRecordNumber}") == true)
                    result = result with { Patient = null, Errors = result.Errors.Append("This document record was already migrated. Exclude it from this import.").ToArray() };
                draft.UpdateValidation(result, FindMatches(draft));
            }
            if (!CanContinueToConfirmation) HasConfirmedSave = false;
            RefreshCounts();
        }
        finally { refreshing = false; }
    }

    private IEnumerable<MigrationIdentity> FindMatches(PatientMigrationDraft draft)
    {
        string first = PatientMigrationValidationService.NormalizeName(draft.FirstName);
        string last = PatientMigrationValidationService.NormalizeName(draft.LastName);
        string phone = PatientMigrationValidationService.NormalizePhone(draft.PhoneNumber);
        return (snapshot?.Patients ?? new()).Where(patient =>
            (first.Length > 0 && last.Length > 0 && first == PatientMigrationValidationService.NormalizeName(patient.FirstName) &&
             last == PatientMigrationValidationService.NormalizeName(patient.LastName)) ||
            (phone.Length > 0 && phone == PatientMigrationValidationService.NormalizePhone(patient.PhoneNumber)));
    }

    private void Continue()
    {
        if (!CanUseModule()) return;
        if (CurrentStage == 1) { if (Drafts.Any(x => !x.IsImported)) CurrentStage = 2; return; }
        if (CurrentStage != 2 || !Validate() || !CanContinueToConfirmation) return;
        ConfirmationRecords.Clear();
        foreach (var draft in Drafts.Where(x => x.IncludeInImport && !x.IsImported)) ConfirmationRecords.Add(draft);
        HasConfirmedSave = false;
        OnPropertyChanged(nameof(ConfirmationSummary));
        CurrentStage = 3;
        StatusMessage = "Confirm the approved patient information before saving.";
    }

    private void Back()
    {
        if (!CanUseModule() || !ShowBack) return;
        HasConfirmedSave = false;
        if (IsHistoryStage)
        {
            CurrentStage = historyReturnStage;
            StatusMessage = historyReturnStatus;
        }
        else CurrentStage--;
    }

    private bool CanSave() => CanUseModule() && CurrentStage == 3 && HasConfirmedSave && CanContinueToConfirmation;

    private async Task SaveApprovedAsync()
    {
        if (!CanSave()) return;
        if (!Validate() || !CanContinueToConfirmation)
        {
            HasConfirmedSave = false;
            CurrentStage = 2;
            return;
        }
        var selected = Drafts.Where(x => x.IncludeInImport && !x.IsImported).ToArray();
        int generation = sessionGeneration;
        IsBusy = true;
        refreshing = true;
        StatusMessage = "Saving approved patient information...";
        try
        {
            var ids = await Task.Run(() => repository.ImportReviewed(selected));
            if (generation != sessionGeneration) return;
            savedPatientCodes.Clear();
            foreach (var draft in selected) savedPatientCodes.Add(draft.ImportedPatientCode);
            ResultMessage = $"{ids.Count} approved patient record(s) saved to the centralized patient database.";
            StatusMessage = "Migration completed.";
            CurrentStage = 4;
        }
        catch (Exception ex)
        {
            if (generation != sessionGeneration) return;
            bool tracked = true;
            foreach (var draft in selected)
                tracked &= TryRecordFailure(draft.SourceFileName, draft.SourceRecordNumber, "Failed", ex.Message);
            ResultMessage = "The migration failed. No patient records from this batch were saved.";
            StatusMessage = $"{ResultMessage} {ex.Message}" + (tracked ? "" : " The failure could not be recorded; check database connectivity.");
            HasConfirmedSave = false;
            CurrentStage = 2;
        }
        finally
        {
            if (generation == sessionGeneration)
            {
                refreshing = false;
                IsBusy = false;
                RefreshCounts();
                RefreshHistory();
            }
        }
    }

    private bool TryRecordFailure(string fileName, int record, string result, string detail)
    {
        try { repository.RecordFailedAttempt(fileName, record, result, detail); return true; }
        catch { return false; }
    }

    private bool RefreshHistory()
    {
        try
        {
            HistoryItems.Clear();
            foreach (var item in repository.GetHistory()) HistoryItems.Add(item);
        }
        catch (Exception ex) { StatusMessage += $" Migration tracking is unavailable: {ex.Message}"; NotifyHistory(); return false; }
        NotifyHistory();
        return true;
    }

    private void NotifyHistory()
    {
        SelectedHistoryItem = DisplayedHistoryItems.FirstOrDefault();
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(DisplayedHistoryItems));
    }

    private void OpenHistory(bool forCompletedBatch)
    {
        if (!CanUseModule() || forCompletedBatch && savedPatientCodes.Count == 0) return;
        if (!IsHistoryStage)
        {
            historyReturnStage = CurrentStage;
            historyReturnStatus = StatusMessage;
        }
        HasConfirmedSave = false;
        viewingBatchResults = forCompletedBatch;
        OnPropertyChanged(nameof(HistoryTitle));
        OnPropertyChanged(nameof(HistoryDescription));
        bool historyAvailable = RefreshHistory();
        CurrentStage = 5;
        if (historyAvailable) StatusMessage = HasHistory ? "Select a result to see its details." : "No migration results recorded yet.";
    }

    private void StartNew()
    {
        if (!CanUseModule()) return;
        if (Drafts.Any(x => !x.IsImported) && MessageBox.Show("Discard unsaved draft changes and start a new migration?",
            "Start New Migration", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        ResetSession();
    }

    // Only in-memory migration work is cleared; saved patients and tracking remain.
    public void ResetSession()
    {
        sessionGeneration++;
        ClearCheckResult();
        refreshing = false;
        foreach (var draft in Drafts) draft.PropertyChanged -= DraftChanged;
        Drafts.Clear();
        ConfirmationRecords.Clear();
        HistoryItems.Clear();
        SelectedHistoryItem = null;
        viewingBatchResults = false;
        historyReturnStage = 1;
        historyReturnStatus = "Select a Word patient record to begin.";
        selectedDocumentPaths.Clear();
        savedPatientCodes.Clear();
        SelectedDraft = null;
        snapshot = null;
        SetServiceOptions(Array.Empty<AppointmentServiceOption>());
        HasConfirmedSave = false;
        FileIssues = "";
        IsValidationExpanded = false;
        ResultMessage = "";
        StatusMessage = "Select a Word patient record to begin.";
        CurrentStage = 1;
        IsBusy = false;
        NotifyHistory();
        OnPropertyChanged(nameof(HistoryTitle));
        OnPropertyChanged(nameof(HistoryDescription));
        OnPropertyChanged(nameof(HasSelectedDocuments));
        OnPropertyChanged(nameof(SelectedFileNames));
        RefreshCounts();
    }

    private void RefreshCounts()
    {
        foreach (string property in new[] { nameof(HasDrafts), nameof(ShowBack), nameof(ShowResumeReview), nameof(ExtractedCount), nameof(SelectedCount),
            nameof(ApprovedCount), nameof(NeedsCorrectionCount), nameof(Summary), nameof(CanContinueToConfirmation) })
            OnPropertyChanged(property);
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        SelectDocumentsCommand.NotifyCanExecuteChanged();
        ExtractDocumentsCommand.NotifyCanExecuteChanged();
        ValidateCommand.NotifyCanExecuteChanged();
        ContinueCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        StartNewCommand.NotifyCanExecuteChanged();
        ViewHistoryCommand.NotifyCanExecuteChanged();
        ViewResultsCommand.NotifyCanExecuteChanged();
    }
}
