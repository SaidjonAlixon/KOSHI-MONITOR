namespace KOSHI.Application.Security;

public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 256;

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password1234", "Password123!", "qwerty123456", "123456789012", "admin1234567", "letmein12345",
        "welcome12345", "Passw0rd1234", "iloveyou1234", "koshi1234567", "Qwerty123456!",
    };

    public static IReadOnlyList<string> Validate(string? password, string? email = null)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Password is required.");
            return errors;
        }

        if (password.Length < MinLength) errors.Add($"Password must be at least {MinLength} characters long.");
        if (password.Length > MaxLength) errors.Add($"Password must be at most {MaxLength} characters long.");

        var classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;
        if (classes < 3) errors.Add("Password must contain at least three of: lowercase, uppercase, digit, symbol.");

        if (Common.Contains(password)) errors.Add("Password is too common.");

        var local = email?.Split('@')[0];
        if (!string.IsNullOrWhiteSpace(local) && local.Length >= 3 && password.Contains(local, StringComparison.OrdinalIgnoreCase))
            errors.Add("Password must not contain the e-mail name.");

        return errors;
    }
}
