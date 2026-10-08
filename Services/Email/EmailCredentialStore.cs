using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CruzNeryClinic.Services.Email;

public interface IEmailCredentialStore
{
    string? Read(string senderEmail);
    void Save(string senderEmail, string appPassword);
}

// The Gmail secret belongs to this Windows account, never to the shared LAN database.
public sealed class EmailCredentialStore : IEmailCredentialStore
{
    private readonly string path;
    public EmailCredentialStore(string? path = null) => this.path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CruzNeryClinic", "Security", "email-sender.bin");
    private sealed record Credential(string Address, string Password);

    public string? Read(string senderEmail)
    {
        if (!File.Exists(path)) return null;
        try
        {
            byte[] bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            try
            {
                var credential = JsonSerializer.Deserialize<Credential>(bytes);
                return string.Equals(credential?.Address, senderEmail.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? credential?.Password : null;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }

    public void Save(string senderEmail, string appPassword)
    {
        string password = appPassword.Replace(" ", "").Trim();
        if (password.Length != 16 || !System.Linq.Enumerable.All(password, char.IsAsciiLetter))
            throw new ArgumentException("Enter the 16-letter Gmail app password, not your normal Gmail password.");
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Credential(senderEmail.Trim(), password)));
        try
        {
            byte[] protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, protectedBytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
