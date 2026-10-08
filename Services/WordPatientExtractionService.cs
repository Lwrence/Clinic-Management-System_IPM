using CruzNeryClinic.Models.Migration;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CruzNeryClinic.Services;

public class WordPatientExtractionService
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly Dictionary<string, string> Labels = BuildLabels();
    private const long MaxFileBytes = 20 * 1024 * 1024;
    private const long MaxXmlBytes = 10 * 1024 * 1024;

    public IReadOnlyList<PatientMigrationDraft> Extract(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select a .docx file. Save older .doc files as .docx in Word first.");
        // Parse and hash the same snapshot, releasing the original before extraction.
        using MemoryStream input = new(ReadDocumentSnapshot(path), writable: false);
        string hash = Convert.ToHexString(SHA256.HashData(input));
        input.Position = 0;
        using ZipArchive archive = new(input, ZipArchiveMode.Read);
        ZipArchiveEntry entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("This file is not a supported Word document.");
        if (entry.Length > MaxXmlBytes) throw new InvalidDataException("Document text exceeds the supported limit.");
        using Stream xmlStream = entry.Open();
        using XmlReader reader = XmlReader.Create(xmlStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXmlBytes
        });
        XDocument document = XDocument.Load(reader);
        XElement body = document.Root?.Element(W + "body")
            ?? throw new InvalidDataException("Word document body is missing.");
        List<PatientMigrationDraft> records = new();
        bool foundPatientTable = false;
        foreach (XElement table in body.Elements(W + "tbl"))
        {
            XElement[] sourceRows = table.Elements(W + "tr").ToArray();
            var rows = sourceRows.Select(row => row.Elements(W + "tc")
                .Select(CellText).ToArray()).ToArray();
            int headerIndex = Array.FindIndex(rows, row =>
                (row.Select(Key).Contains("FirstName") && row.Select(Key).Contains("LastName")) ||
                (row.Select(Key).Contains("UnparsedName") &&
                 (row.Select(Key).Contains("BirthDateText") || row.Select(Key).Contains("PhoneNumber"))));
            if (headerIndex < 0) continue;
            foundPatientTable = true;
            string?[] headers = rows[headerIndex].Select(Key).ToArray();
            for (int rowIndex = headerIndex + 1; rowIndex < rows.Length; rowIndex++)
            {
                string[] row = rows[rowIndex];
                if (row.All(string.IsNullOrWhiteSpace)) continue;
                if (row.Select(Key).SequenceEqual(headers)) continue; // repeated page header
                PatientMigrationDraft draft = NewDraft(path, hash, records.Count + 1,
                    string.Join("\n", row.Select((value, i) => $"{(i < headers.Length ? rows[headerIndex][i] : $"Extra cell {i + 1}")}: {value}")));
                bool mergedCells = sourceRows[rowIndex].Descendants(W + "gridSpan").Any() ||
                    sourceRows[rowIndex].Descendants(W + "vMerge").Any();
                if (row.Length != headers.Length || mergedCells)
                {
                    draft.ExtractionWarnings.Add("Row has merged or missing cells. Automatic field mapping was skipped; verify against the original Word document and enter details manually.");
                    records.Add(draft);
                    continue;
                }
                for (int i = 0; i < Math.Min(headers.Length, row.Length); i++)
                {
                    if (headers[i] != null) Apply(draft, headers[i]!, row[i]);
                    else if (!string.IsNullOrWhiteSpace(row[i]))
                        draft.ExtractionWarnings.Add($"Unmapped column '{rows[headerIndex][i]}' remains in the source preview.");
                }
                records.Add(draft);
            }
        }
        if (records.Count > 0) return records;
        if (foundPatientTable) throw new InvalidDataException("Patient table contains no nonempty patient rows.");

        string sourceText = string.Join("\n", body.Descendants(W + "p").Select(ParagraphText));
        PatientMigrationDraft form = NewDraft(path, hash, 1, sourceText);
        int recognized = 0;
        foreach (XElement table in body.Elements(W + "tbl"))
        foreach (XElement row in table.Elements(W + "tr"))
        {
            string[] cells = row.Elements(W + "tc").Select(CellText).ToArray();
            for (int i = 0; i + 1 < cells.Length; i += 2)
                if (Key(cells[i]) is string field) { Apply(form, field, cells[i + 1]); recognized++; }
        }
        string? pending = null;
        foreach (XElement paragraph in body.Elements(W + "p"))
        {
            string text = ParagraphText(paragraph).Trim();
            if (text.Length == 0) continue;
            Match match = Regex.Match(text, @"^([^:\t]+)[:\t]\s*(.*)$");
            if (match.Success && Key(match.Groups[1].Value) is string field)
            {
                string value = match.Groups[2].Value.Trim();
                if (value.Length == 0) pending = field;
                else { Apply(form, field, value); recognized++; pending = null; }
            }
            else if (Key(text) is string label) pending = label;
            else if (pending != null) { Apply(form, pending, text); recognized++; pending = null; }
        }
        bool hasSeparateNames = !string.IsNullOrWhiteSpace(form.FirstName) && !string.IsNullOrWhiteSpace(form.LastName);
        bool hasFullNameWithIdentity = !string.IsNullOrWhiteSpace(form.UnparsedName) &&
            (!string.IsNullOrWhiteSpace(form.BirthDateText) || !string.IsNullOrWhiteSpace(form.PhoneNumber));
        if (recognized == 0 || !(hasSeparateNames || hasFullNameWithIdentity))
            throw new InvalidDataException("No supported patient record found. Upload an actual patient record with First Name and Last Name, or Patient Name with Date of Birth or Contact Number. A generic Name field alone is not a patient record.");
        form.ExtractionWarnings.Add("Labeled form extracted as one patient. Verify that this document contains only one patient.");
        return new[] { form };
    }

    private static byte[] ReadDocumentSnapshot(string path)
    {
        try
        {
            using FileStream source = new(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
            if (source.Length > MaxFileBytes) throw new InvalidDataException("Word file exceeds the 20 MB limit.");
            using MemoryStream snapshot = new();
            byte[] buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (snapshot.Length + bytesRead > MaxFileBytes)
                    throw new InvalidDataException("Word file exceeds the 20 MB limit.");
                snapshot.Write(buffer, 0, bytesRead);
            }
            return snapshot.ToArray();
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            throw new IOException("This Word document is locked by another program. Save and close it in Word, then select the file and try extracting again.", ex);
        }
    }

    private static PatientMigrationDraft NewDraft(string path, string hash, int number, string source) =>
        new() { SourceFileName = Path.GetFileName(path), SourceHash = hash, SourceRecordNumber = number, SourceText = source };

    private static string ParagraphText(XElement paragraph) => string.Concat(paragraph.Descendants()
        .Where(e => e.Name == W + "t" || e.Name == W + "tab" || e.Name == W + "br")
        .Select(e => e.Name == W + "t" ? e.Value : e.Name == W + "tab" ? "\t" : "\n"));
    private static string CellText(XElement cell) => string.Join("\n", cell.Elements(W + "p").Select(ParagraphText)).Trim();
    private static string? Key(string label) => Labels.GetValueOrDefault(
        Regex.Replace(label.ToLowerInvariant(), @"[^a-z0-9]", ""));

    private static Dictionary<string, string> BuildLabels()
    {
        Dictionary<string, string> labels = new();
        void Add(string field, params string[] names)
        {
            labels[Regex.Replace(field.ToLowerInvariant(), @"[^a-z0-9]", "")] = field;
            foreach (string name in names) labels[Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]", "")] = field;
        }
        Add("UnparsedName", "Name", "Full Name", "Patient Name", "Name of Patient");
        Add("FirstName", "First Name", "Given Name", "Given Names");
        Add("MiddleName", "Middle Name", "Middle Initial");
        Add("LastName", "Last Name", "Surname", "Family Name");
        Add("EmailAddress", "Email", "Email Address", "E-mail", "E-mail Address", "Patient Email");
        Add("PhoneNumber", "Phone", "Mobile", "Mobile Number", "Contact Number", "Contact No", "Contact", "Mobile No", "Telephone");
        Add("BirthDateText", "Date of Birth", "Birth Date", "Birthday", "DOB");
        Add("Gender", "Sex", "Gender / Sex");
        Add("Address", "Home Address", "Patient Address");
        Add("IsPwd", "PWD", "Person with Disability", "Is PWD");
        Add("InitialTreatment", "Initial Treatment", "Initial Service", "Treatment", "Service");
        Add("HasMedicalCondition", "Has Medical Condition");
        Add("MedicalConditionNotes", "Medical Conditions", "Medical History", "Medical Notes");
        Add("AllergyNotes", "Allergies", "Allergy");
        Add("CurrentMedication", "Medication", "Medications", "Current Medications");
        Add("RequiresMedicalClearance", "Requires Medical Clearance");
        Add("ClearanceNotes", "Medical Clearance Notes");
        Add("InitialTreatmentNotes", "Initial Visit Notes", "Treatment Notes");
        return labels;
    }

    private static void Apply(PatientMigrationDraft draft, string field, string value)
    {
        value = value.Trim();
        if (field == "UnparsedName" && value.Length > 0)
            draft.ExtractionWarnings.Add("Full name retained in source preview. Enter verified first, middle, and last names separately.");
        if (field == "Gender") value = value.ToLowerInvariant() switch
        {
            "m" or "male" => "Male", "f" or "female" => "Female", "other" => "Other", _ => value
        };
        var property = typeof(PatientMigrationDraft).GetProperty(field)!;
        if (property.PropertyType == typeof(bool))
        {
            if (value.Length == 0) return;
            bool? parsed = value.ToLowerInvariant() switch
            {
                "yes" or "y" or "true" or "1" or "checked" => true,
                "no" or "n" or "false" or "0" or "unchecked" => false,
                _ => null
            };
            if (parsed == null) draft.ExtractionWarnings.Add($"Unrecognized {field} value '{value}'. Confirm it manually.");
            else property.SetValue(draft, parsed.Value);
        }
        else
        {
            string previous = (string)property.GetValue(draft)!;
            if (previous.Length > 0 && value.Length > 0 && previous != value)
                draft.ExtractionWarnings.Add($"Conflicting {field} values. Review source and correct this field.");
            else if (value.Length > 0) property.SetValue(draft, value);
        }
        if (field == "MedicalConditionNotes" && value.Length > 0 &&
            !new[] { "none", "no", "n/a" }.Contains(value.ToLowerInvariant())) draft.HasMedicalCondition = true;
    }
}
