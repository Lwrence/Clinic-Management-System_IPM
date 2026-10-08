using CruzNeryClinic.ViewModels;
using CruzNeryClinic.Services;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Globalization;

namespace CruzNeryClinic.Models.Migration;

public class PatientMigrationDraft : BaseViewModel
{
    public string SourceFileName { get; init; } = "";
    public string SourceHash { get; init; } = "";
    public int SourceRecordNumber { get; init; }
    public string SourceText { get; init; } = "";
    public string SourceDisplay => $"{SourceFileName} / record {SourceRecordNumber}";
    public ObservableCollection<string> ExtractionWarnings { get; } = new();
    public string ExtractionWarningText => string.Join("\n", ExtractionWarnings);
    public ObservableCollection<string> ValidationErrors { get; } = new();
    public ObservableCollection<string> MissingInformationErrors { get; } = new();
    public ObservableCollection<string> DuplicateErrors { get; } = new();
    public ObservableCollection<string> ValidationWarnings { get; } = new();
    public ObservableCollection<MigrationIdentity> PossibleMatches { get; } = new();
    private bool includeInImport, isReviewed, acknowledgeWarnings, hasConsent, isImported;
    private string validationText = "Awaiting validation";
    private string importedPatientCode = "";
    public bool IncludeInImport { get => includeInImport; set => SetProperty(ref includeInImport, value); }
    public bool IsReviewed
    {
        get => isReviewed;
        set { if (SetProperty(ref isReviewed, value)) OnPropertyChanged(nameof(ReviewStatus)); }
    }
    public bool AcknowledgeWarnings { get => acknowledgeWarnings; set => Edit(ref acknowledgeWarnings, value); }
    // Consent is always confirmed by staff, never inferred from a source file.
    public bool HasConsent { get => hasConsent; set => Edit(ref hasConsent, value); }
    public bool IsImported
    {
        get => isImported;
        set
        {
            if (!SetProperty(ref isImported, value)) return;
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanApprove));
            OnPropertyChanged(nameof(ReviewStatus));
        }
    }
    public bool CanEdit => !IsImported;
    public bool CanApprove => CanEdit && ValidatedPatient != null;
    public string ValidationSummary => $"Validation checks · {ValidationErrors.Count} issue(s) · {PossibleMatches.Count} existing match(es)";
    public bool HasValidationErrors => ValidationErrors.Count > 0;
    public bool HasMissingInformationErrors => MissingInformationErrors.Count > 0;
    public bool HasDuplicateIssues => DuplicateErrors.Count > 0 || HasPossibleMatches;
    public bool HasValidationWarnings => ValidationWarnings.Count > 0;
    public bool HasPossibleMatches => PossibleMatches.Count > 0;
    public string ImportedPatientCode { get => importedPatientCode; set => SetProperty(ref importedPatientCode, value); }
    public string ValidationText { get => validationText; set => SetProperty(ref validationText, value); }
    public string ReviewStatus => IsImported ? $"Saved · {ImportedPatientCode}" :
        HasValidationErrors ? "Needs correction" : IsReviewed ? "Approved" : "Awaiting staff approval";
    public string PatientDisplay
    {
        get
        {
            string name = string.Join(" ", new[] { FirstName, MiddleName, LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return name.Length > 0 ? name : UnparsedName.Length > 0 ? UnparsedName : "Incomplete patient record";
        }
    }
    public Patient? ValidatedPatient { get; set; }

    public void UpdateValidation(MigrationValidation validation, IEnumerable<MigrationIdentity> matches)
    {
        var newMatches = matches.ToArray();
        bool changedWarnings = !ValidationWarnings.SequenceEqual(validation.Warnings) ||
            !PossibleMatches.SequenceEqual(newMatches);
        ValidationErrors.Clear();
        foreach (string error in validation.Errors) ValidationErrors.Add(error);
        MissingInformationErrors.Clear();
        DuplicateErrors.Clear();
        foreach (string error in validation.Errors)
        {
            if (error.StartsWith("Duplicate", StringComparison.OrdinalIgnoreCase) || error.Contains("already migrated")) DuplicateErrors.Add(error);
            else MissingInformationErrors.Add(error);
        }
        ValidationWarnings.Clear();
        foreach (string warning in validation.Warnings) ValidationWarnings.Add(warning);
        PossibleMatches.Clear();
        foreach (var match in newMatches) PossibleMatches.Add(match);
        ValidatedPatient = validation.Patient;
        if (validation.Errors.Count > 0 || changedWarnings) IsReviewed = false;
        ValidationText = string.Join("\n", validation.Errors.Concat(validation.Warnings));
        OnPropertyChanged(nameof(ValidationSummary));
        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(HasMissingInformationErrors));
        OnPropertyChanged(nameof(HasDuplicateIssues));
        OnPropertyChanged(nameof(HasValidationWarnings));
        OnPropertyChanged(nameof(HasPossibleMatches));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(ReviewStatus));
    }

    private void Edit<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        isReviewed = false;
        field = value;
        OnPropertyChanged(nameof(IsReviewed));
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(PatientDisplay));
        OnPropertyChanged(nameof(ReviewStatus));
    }
    private string unparsedName = "";
    public string UnparsedName { get => unparsedName; set => Edit(ref unparsedName, value); }
    private string firstName = "";
    public string FirstName { get => firstName; set => Edit(ref firstName, value); }
    private string middleName = "";
    public string MiddleName { get => middleName; set => Edit(ref middleName, value); }
    private string lastName = "";
    public string LastName { get => lastName; set => Edit(ref lastName, value); }
    private string emailAddress = "";
    public string EmailAddress { get => emailAddress; set => Edit(ref emailAddress, value); }
    private bool emailNotificationsEnabled;
    public bool EmailNotificationsEnabled { get => emailNotificationsEnabled; set => Edit(ref emailNotificationsEnabled, value); }
    private string phoneNumber = "";
    public string PhoneNumber { get => phoneNumber; set => Edit(ref phoneNumber, value); }
    private string birthDateText = "";
    public string BirthDateText
    {
        get => birthDateText;
        set
        {
            if (birthDateText == value) return;
            Edit(ref birthDateText, value);
            OnPropertyChanged(nameof(BirthDate));
            OnPropertyChanged(nameof(HasUnresolvedBirthDate));
            OnPropertyChanged(nameof(UnresolvedBirthDateMessage));
        }
    }
    public DateTime? BirthDate
    {
        get => PatientMigrationValidationService.TryBirthDate(BirthDateText, out DateTime date) ? date.Date : null;
        set
        {
            // A blank calendar must not erase an unrecognized date from Word.
            if (BirthDate == value?.Date) return;
            BirthDateText = value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        }
    }
    public bool HasUnresolvedBirthDate => !string.IsNullOrWhiteSpace(BirthDateText) && BirthDate == null;
    public string UnresolvedBirthDateMessage => $"Extracted date: {BirthDateText}. Select the correct birth date using the calendar.";
    private string gender = "";
    public string Gender { get => gender; set => Edit(ref gender, value); }
    private string address = "";
    public string Address { get => address; set => Edit(ref address, value); }
    private string initialTreatment = "";
    public string InitialTreatment { get => initialTreatment; set => Edit(ref initialTreatment, value); }
    private string medicalConditionNotes = "";
    public string MedicalConditionNotes { get => medicalConditionNotes; set => Edit(ref medicalConditionNotes, value); }
    private string allergyNotes = "";
    public string AllergyNotes { get => allergyNotes; set => Edit(ref allergyNotes, value); }
    private string currentMedication = "";
    public string CurrentMedication { get => currentMedication; set => Edit(ref currentMedication, value); }
    private string clearanceNotes = "";
    public string ClearanceNotes { get => clearanceNotes; set => Edit(ref clearanceNotes, value); }
    private string initialTreatmentNotes = "";
    public string InitialTreatmentNotes { get => initialTreatmentNotes; set => Edit(ref initialTreatmentNotes, value); }
    private bool isPwd;
    public bool IsPwd { get => isPwd; set => Edit(ref isPwd, value); }
    private bool hasMedicalCondition;
    public bool HasMedicalCondition { get => hasMedicalCondition; set => Edit(ref hasMedicalCondition, value); }
    private bool requiresMedicalClearance;
    public bool RequiresMedicalClearance { get => requiresMedicalClearance; set => Edit(ref requiresMedicalClearance, value); }
}
