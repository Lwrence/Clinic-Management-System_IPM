namespace CruzNeryClinic.Models.Migration;

public record MigrationResultItem(string SourceFileName, int SourceRecordNumber, string PatientCode,
    string PatientName, string Result, string Details, string ProcessedBy, DateTime ProcessedAt)
{
    public string SourceDisplay => SourceRecordNumber > 0
        ? $"{SourceFileName} / record {SourceRecordNumber}" : SourceFileName;
    public string ProcessedAtDisplay => ProcessedAt.ToString("MMM dd, yyyy h:mm tt");
}
