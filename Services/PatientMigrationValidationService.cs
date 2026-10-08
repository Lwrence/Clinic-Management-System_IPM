using CruzNeryClinic.Models;
using CruzNeryClinic.Models.Migration;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CruzNeryClinic.Services;

public record MigrationIdentity(int PatientId, string PatientCode, string FirstName, string MiddleName,
    string LastName, DateTime BirthDate, string PhoneNumber);
public record MigrationValidation(Patient? Patient, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool CanImport => Patient != null && Errors.Count == 0;
}

public static class PatientMigrationValidationService
{
    public static string NormalizeName(string value) =>
        Regex.Replace(value.Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ").ToUpperInvariant();

    public static string NormalizePhone(string value)
    {
        string digits = Regex.Replace(value, @"\D", "");
        if (digits.StartsWith("63") && digits.Length == 12) digits = "0" + digits[2..];
        return digits;
    }

    public static bool TryBirthDate(string value, out DateTime date)
    {
        date = default;
        // Numeric slash dates are accepted only when day/month interpretation is unambiguous.
        if (DateTime.TryParseExact(value.Trim(), new[] { "yyyy-MM-dd", "yyyy/MM/dd",
            "d MMM yyyy", "dd MMM yyyy", "d MMMM yyyy", "MMMM d, yyyy", "MMM d, yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        bool dayFirst = DateTime.TryParseExact(value.Trim(), new[] { "d/M/yyyy", "dd/MM/yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dmy);
        bool monthFirst = DateTime.TryParseExact(value.Trim(), new[] { "M/d/yyyy", "MM/dd/yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime mdy);
        if (dayFirst && monthFirst && dmy != mdy) return false;
        if (dayFirst || monthFirst) { date = dayFirst ? dmy : mdy; return true; }
        return false;
    }

    public static MigrationValidation Validate(PatientMigrationDraft draft, IEnumerable<MigrationIdentity> existing,
        IEnumerable<PatientMigrationDraft> selected, DateTime today)
    {
        List<string> errors = new();
        List<string> warnings = draft.ExtractionWarnings.ToList();
        if (string.IsNullOrWhiteSpace(draft.FirstName)) errors.Add("First name is required.");
        if (string.IsNullOrWhiteSpace(draft.LastName)) errors.Add("Last name is required.");
        string phone = NormalizePhone(draft.PhoneNumber);
        if (!Regex.IsMatch(draft.PhoneNumber.Trim(), @"^[+\d\s()\-]+$") || !Regex.IsMatch(phone, @"^09\d{9}$")) errors.Add("Contact number must be an 11-digit mobile number starting with 09.");
        if (!string.IsNullOrWhiteSpace(draft.EmailAddress) && !EmailAddressValidation.IsValid(draft.EmailAddress))
            errors.Add("Email address is invalid. Correct it or leave the optional field blank.");
        bool validDate = TryBirthDate(draft.BirthDateText, out DateTime birthDate);
        if (!validDate) errors.Add("Date of birth is missing, invalid, or ambiguous. Enter yyyy-MM-dd.");
        else if (birthDate.Date > today.Date.AddYears(-1) || birthDate.Date < today.Date.AddYears(-120))
            errors.Add("Patient age must be between 1 and 120 years.");
        string gender = draft.Gender.Trim();
        if (!new[] { "Male", "Female", "Other" }.Contains(gender, StringComparer.OrdinalIgnoreCase))
            errors.Add("Gender must be Male, Female, or Other.");
        if (!draft.HasConsent) errors.Add("Confirm that data privacy consent or authorization is recorded.");
        if (draft.HasMedicalCondition && string.IsNullOrWhiteSpace(draft.MedicalConditionNotes))
            errors.Add("Medical condition notes are required when a condition is indicated.");
        if (draft.RequiresMedicalClearance && string.IsNullOrWhiteSpace(draft.ClearanceNotes))
            errors.Add("Medical clearance notes are required when clearance is indicated.");
        if (string.IsNullOrWhiteSpace(draft.InitialTreatment))
            warnings.Add("No initial treatment was extracted. Review the source; do not invent a treatment.");
        string first = NormalizeName(draft.FirstName), last = NormalizeName(draft.LastName);
        string middle = NormalizeName(draft.MiddleName);
        foreach (MigrationIdentity patient in existing)
        {
            bool sameName = first.Length > 0 && last.Length > 0 &&
                first == NormalizeName(patient.FirstName) && last == NormalizeName(patient.LastName);
            bool sameBirth = validDate && birthDate.Date == patient.BirthDate.Date;
            string otherMiddle = NormalizeName(patient.MiddleName);
            if (sameName && sameBirth && (middle == otherMiddle || middle.Length == 0 || otherMiddle.Length == 0))
                errors.Add($"Duplicate identity: existing patient {patient.PatientCode} (including archived records).");
            else if (sameName || (phone.Length > 0 && phone == NormalizePhone(patient.PhoneNumber)))
                warnings.Add($"Possible duplicate: {patient.PatientCode}. Verify name, birth date, and contact number.");
        }
        foreach (PatientMigrationDraft other in selected.Where(x => !ReferenceEquals(x, draft) && !x.IsImported))
        {
            if (validDate && TryBirthDate(other.BirthDateText, out DateTime otherBirth) &&
                first.Length > 0 && last.Length > 0 && first == NormalizeName(other.FirstName) &&
                last == NormalizeName(other.LastName) && birthDate.Date == otherBirth.Date)
                errors.Add($"Duplicate identity in selected records: {other.SourceDisplay}.");
        }
        if (warnings.Count > 0 && !draft.AcknowledgeWarnings)
            errors.Add("Review and acknowledge the extraction / possible-duplicate warnings.");
        Patient? result = errors.Count == 0 ? new Patient
        {
            FirstName = draft.FirstName.Trim(), MiddleName = draft.MiddleName.Trim(), LastName = draft.LastName.Trim(),
            PhoneNumber = phone, BirthDate = birthDate.Date,
            EmailAddress = draft.EmailAddress.Trim(), EmailNotificationsEnabled = draft.EmailNotificationsEnabled,
            Gender = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(gender.ToLowerInvariant()),
            Address = draft.Address.Trim(), IsPwd = draft.IsPwd,
            IsSeniorCitizen = birthDate.Date <= today.Date.AddYears(-60),
            InitialTreatment = draft.InitialTreatment.Trim(), HasDataPrivacyConsent = true,
            DataPrivacyConsentAt = DateTime.Now, DataPrivacyConsentVersion = "CNDC-DPA-2026-02",
            HasMedicalCondition = draft.HasMedicalCondition, MedicalConditionNotes = draft.MedicalConditionNotes.Trim(),
            AllergyNotes = draft.AllergyNotes.Trim(), CurrentMedication = draft.CurrentMedication.Trim(),
            RequiresMedicalClearance = draft.RequiresMedicalClearance, ClearanceNotes = draft.ClearanceNotes.Trim(),
            InitialTreatmentNotes = draft.InitialTreatmentNotes.Trim()
        } : null;
        return new(result, errors, warnings);
    }
}
