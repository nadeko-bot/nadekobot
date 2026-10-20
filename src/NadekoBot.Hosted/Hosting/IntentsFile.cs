namespace NadekoBot.Hosted;

// data/.intents: the privileged intents which Discord allowed at the last start, for a start while Discord does not answer.
public static class IntentsFile
{
    private const int MAX_BYTES = 256;
    private const string PRESENCE = "presence";
    private const string MEMBERS = "members";
    private const string MESSAGE_CONTENT = "messageContent";

    // Indexed by IntentState.MissingMask.
    private static readonly string[] _lines =
    [
        "presence,members,messageContent\n", "members,messageContent\n", "presence,messageContent\n", "messageContent\n",
        "presence,members\n", "members\n", "presence\n", "\n"
    ];

    public static IntentState? Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            // A valid file is never empty, and a fifo or device reports length 0, so this also keeps the read from blocking.
            if (!info.Exists || info.LinkTarget is not null || info.Length is 0 or > MAX_BYTES)
                return null;

            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not read {Path}", path);
            return null;
        }
    }

    public static IntentState? Parse(ReadOnlySpan<char> text)
    {
        var line = text.Trim();
        if (line.ContainsAny('\r', '\n'))
            return null;

        bool presence = false, members = false, messageContent = false;
        foreach (var range in line.Split(','))
        {
            var name = line[range].Trim();
            if (name.IsEmpty)
                continue;

            if (name.SequenceEqual(PRESENCE))
                presence = true;
            else if (name.SequenceEqual(MEMBERS))
                members = true;
            else if (name.SequenceEqual(MESSAGE_CONTENT))
                messageContent = true;
            else
                return null;
        }

        return new IntentState(presence, members, messageContent);
    }

    public static string Format(IntentState intents)
        => _lines[intents.MissingMask];

    public static bool Write(string path, IntentState intents)
    {
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, Format(intents));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not write {Path}", path);
            return false;
        }
    }

    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not delete {Path}", path);
        }
    }
}
