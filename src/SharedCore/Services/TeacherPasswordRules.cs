namespace SharedCore.Services;

public static class TeacherPasswordRules
{
    public const int MinLength = 12;
    public static string RequirementsText => $"Heslo musí mít alespoň {MinLength} znaků.";

    public static bool TryValidate(string? password, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            errorMessage = "Heslo nesmí být prázdné.";
            return false;
        }

        if (password.Length < MinLength)
        {
            errorMessage = RequirementsText;
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    public static void EnsureValid(string? password)
    {
        if (!TryValidate(password, out var errorMessage))
        {
            throw new ArgumentException(errorMessage);
        }
    }
}
