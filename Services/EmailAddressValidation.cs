using System;
using System.Net.Mail;

namespace CruzNeryClinic.Services;

public static class EmailAddressValidation
{
    public static bool IsValid(string? value)
    {
        string text = value?.Trim() ?? "";
        return text.Length is > 0 and <= 254 && !text.Contains('\r') && !text.Contains('\n') &&
            MailAddress.TryCreate(text, out MailAddress? address) &&
            string.IsNullOrEmpty(address.DisplayName) &&
            string.Equals(address.Address, text, StringComparison.OrdinalIgnoreCase) &&
            address.Host.Contains('.') && !address.Host.EndsWith('.');
    }
}
