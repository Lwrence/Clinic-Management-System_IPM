namespace CruzNeryClinic.Models.Migration;

public record PatientValidationCheckResult(string PatientName, string SourceDisplay, IReadOnlyList<string> Issues);
