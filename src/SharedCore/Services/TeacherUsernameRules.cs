namespace SharedCore.Services;

public static class TeacherUsernameRules
{
    public const int MaxLength = 64;
    public const string InvalidUsernameMessage = "Uživatelské jméno není platné. Použijte jen písmena bez diakritiky, číslice, tečku, pomlčku nebo podtržítko.";
    public const string RequirementsText = "Uživatelské jméno smí obsahovat jen písmena bez diakritiky, číslice, tečku, pomlčku a podtržítko. Nesmí obsahovat mezery.";
    public const string HelpText = "Uživatelské jméno používejte bez mezer a bez diakritiky, například martin.krnac.";

    public static bool TryNormalize(string? username, out string normalizedUsername, out string errorMessage)
    {
        normalizedUsername = string.Empty;
        var value = username?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            errorMessage = "Uživatelské jméno nesmí být prázdné.";
            return false;
        }

        if (value.Length > MaxLength || value.Any(static ch => !IsAllowedUsernameChar(ch)))
        {
            errorMessage = InvalidUsernameMessage;
            return false;
        }

        normalizedUsername = value;
        errorMessage = string.Empty;
        return true;
    }

    public static string Normalize(string? username)
    {
        if (!TryNormalize(username, out var normalizedUsername, out var errorMessage))
        {
            throw new ArgumentException(errorMessage);
        }

        return normalizedUsername;
    }

    private static bool IsAllowedUsernameChar(char ch) =>
        ch is >= 'a' and <= 'z' ||
        ch is >= '0' and <= '9' ||
        ch is '.' or '_' or '-';
}
