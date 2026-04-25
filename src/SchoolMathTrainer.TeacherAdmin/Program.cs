using System.Text;
using SharedCore.Models;
using SharedCore.Services;

Console.OutputEncoding = Encoding.UTF8;

try
{
    var parsed = CommandLine.Parse(args);
    if (parsed.Command is "help" or "-h" or "--help" or "")
    {
        PrintUsage();
        return 0;
    }

    var store = new TeacherAccountStore(parsed.DataRoot);
    switch (parsed.Command)
    {
        case "create-teacher":
            Require(parsed.Username, "--username");
            var createPassword = ResolvePassword(parsed);
            var displayName = string.IsNullOrWhiteSpace(parsed.DisplayName)
                ? parsed.Username
                : parsed.DisplayName;
            var role = TeacherRoles.Normalize(parsed.Role);
            var created = store.CreateTeacher(parsed.Username, displayName, createPassword, role);
            new TeacherAuthAuditLogger(store).Write(
                "teacher_account_created",
                created.Username,
                "TeacherAdmin CLI",
                "create-teacher",
                0,
                "Teacher account created by CLI.",
                created.Role);
            Console.WriteLine($"OK: Učitel '{created.Username}' byl vytvořen. Role: {created.Role}. Aktivní: {created.IsActive}.");
            return 0;

        case "set-teacher-password":
            Require(parsed.Username, "--username");
            var setPassword = ResolvePassword(parsed);
            var changed = store.SetTeacherPassword(parsed.Username, setPassword);
            Console.WriteLine($"OK: Heslo učitele '{changed.Username}' bylo změněno.");
            return 0;

        case "deactivate-teacher":
            Require(parsed.Username, "--username");
            var deactivated = store.SetTeacherActive(parsed.Username, false);
            Console.WriteLine($"OK: Učitel '{deactivated.Username}' byl deaktivován.");
            return 0;

        case "activate-teacher":
            Require(parsed.Username, "--username");
            var activated = store.SetTeacherActive(parsed.Username, true);
            Console.WriteLine($"OK: Učitel '{activated.Username}' byl aktivován.");
            return 0;

        case "list-teachers":
            var teachers = store.ListTeachers();
            if (teachers.Count == 0)
            {
                Console.WriteLine("OK: Nejsou založení žádní učitelé.");
                return 0;
            }

            foreach (var teacher in teachers)
            {
                Console.WriteLine($"{teacher.Username}\t{teacher.DisplayName}\trole={TeacherRoles.Normalize(teacher.Role)}\tactive={teacher.IsActive}\tcreatedUtc={teacher.CreatedUtc:O}\tupdatedUtc={teacher.UpdatedUtc:O}");
            }

            return 0;

        default:
            Console.Error.WriteLine($"Nerozpoznaný příkaz: {parsed.Command}");
            PrintUsage();
            return 2;
    }
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or FormatException)
{
    Console.Error.WriteLine($"CHYBA: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("SchoolMathTrainer.TeacherAdmin");
    Console.WriteLine();
    Console.WriteLine("Příkazy:");
    Console.WriteLine("  create-teacher --username <jmeno> --display-name <zobrazene-jmeno> [--role Admin|Teacher] [--password-stdin] [--data-root <cesta>]");
    Console.WriteLine("  set-teacher-password --username <jmeno> [--password-stdin] [--data-root <cesta>]");
    Console.WriteLine("  deactivate-teacher --username <jmeno> [--data-root <cesta>]");
    Console.WriteLine("  activate-teacher --username <jmeno> [--data-root <cesta>]");
    Console.WriteLine("  list-teachers [--data-root <cesta>]");
    Console.WriteLine();
    Console.WriteLine("Výchozí data-root: /var/lib/schoolmath/data");
}

static void Require(string value, string optionName)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new ArgumentException($"Chybí povinný parametr {optionName}.");
    }
}

static string ResolvePassword(CommandLine parsed)
{
    if (parsed.PasswordStdin)
    {
        var stdinPassword = Console.In.ReadLine();
        if (stdinPassword is null)
        {
            throw new ArgumentException("Heslo nebylo předáno přes stdin.");
        }

        return stdinPassword;
    }

    Console.Error.Write("Zadejte heslo učitele: ");
    return ReadPassword();
}

static string ReadPassword()
{
    var builder = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.Error.WriteLine();
            break;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (builder.Length > 0)
            {
                builder.Length--;
            }

            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            builder.Append(key.KeyChar);
        }
    }

    return builder.ToString();
}

internal sealed class CommandLine
{
    public string Command { get; init; } = string.Empty;
    public string DataRoot { get; init; } = TeacherAccountStore.DefaultDataRoot;
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = TeacherRoles.Teacher;
    public bool PasswordStdin { get; init; }

    public static CommandLine Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var command = args.Length > 0 ? args[0] : string.Empty;

        for (var index = 1; index < args.Length; index++)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Nečekaný argument: {key}");
            }

            if (string.Equals(key, "--password-stdin", StringComparison.OrdinalIgnoreCase))
            {
                values[key] = "true";
                continue;
            }

            if (!IsValueOption(key))
            {
                throw new ArgumentException($"Nepodporovaný parametr: {key}");
            }

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Parametr {key} nemá hodnotu.");
            }

            values[key] = args[++index];
        }

        return new CommandLine
        {
            Command = command,
            DataRoot = Get(values, "--data-root", TeacherAccountStore.DefaultDataRoot),
            Username = Get(values, "--username"),
            DisplayName = Get(values, "--display-name"),
            Role = TeacherRoles.Normalize(Get(values, "--role", TeacherRoles.Teacher)),
            PasswordStdin = values.ContainsKey("--password-stdin")
        };
    }

    private static string Get(Dictionary<string, string> values, string key, string defaultValue = "") =>
        values.TryGetValue(key, out var value) ? value : defaultValue;

    private static bool IsValueOption(string key) =>
        string.Equals(key, "--data-root", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, "--username", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, "--display-name", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, "--role", StringComparison.OrdinalIgnoreCase);
}
