using System;

namespace CruzNeryClinic.Models
{
    public class Patient
    {
        public int PatientId { get; set; }

        public string PatientCode { get; set; } = string.Empty;

        public string FirstName { get; set; } = string.Empty;

        public string MiddleName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string PhoneNumber { get; set; } = string.Empty;
        public string EmailAddress { get; set; } = string.Empty;
        public bool EmailNotificationsEnabled { get; set; }

        public DateTime BirthDate { get; set; } = DateTime.Today;

        public string Gender { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public bool IsPwd { get; set; }

        public bool IsSeniorCitizen { get; set; }

        public string InitialTreatment { get; set; } = string.Empty;

        public bool HasDataPrivacyConsent { get; set; }

        public DateTime? DataPrivacyConsentAt { get; set; }

        public string DataPrivacyConsentVersion { get; set; } = string.Empty;

        public bool HasMedicalCondition { get; set; }

        public string MedicalConditionNotes { get; set; } = string.Empty;

        public string AllergyNotes { get; set; } = string.Empty;

        public string CurrentMedication { get; set; } = string.Empty;

        public bool RequiresMedicalClearance { get; set; }

        public string ClearanceNotes { get; set; } = string.Empty;

        public string InitialTreatmentNotes { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public string FullName
        {
            get
            {
                string middle = string.IsNullOrWhiteSpace(MiddleName)
                    ? string.Empty
                    : $" {MiddleName.Trim()}";

                return $"{FirstName.Trim()}{middle} {LastName.Trim()}".Trim();
            }
        }
    }
}
