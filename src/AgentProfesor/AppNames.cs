namespace AgentProfesor;

/// <summary>
/// Převádí názvy procesů (např. „WINWORD") na lidské názvy (např. „Word"), které uvidí
/// ne-technický uživatel. Neznámé procesy nechá být.
/// </summary>
public static class AppNames
{
    private static readonly Dictionary<string, string> Friendly = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WINWORD"] = "Word",
        ["OUTLOOK"] = "Outlook",
        ["notepad"] = "Poznámkový blok",
        ["wordpad"] = "WordPad",
        ["notepad++"] = "Notepad++",
        ["POWERPNT"] = "PowerPoint",
        ["EXCEL"] = "Excel",
    };

    public static string ToFriendly(string processName)
        => Friendly.TryGetValue(processName, out var name) ? name : processName;
}
