namespace NadekoBot;

public readonly struct LocStr
{
    public readonly string Key;
    public readonly object[] Params;

    public LocStr(string key, params object[] data)
    {
        Key = key;
        Params = data;
    }
}