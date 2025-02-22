#nullable disable
namespace NadekoBot.Common.TypeReaders;

public class CommandOrExprInfo
{
    public enum Type
    {
        Normal,
        Custom
    }

    public string Name { get; set; }
    public Type CmdType { get; set; }

    public bool IsCustom
        => CmdType == Type.Custom;

    public CommandOrExprInfo(string input, Type type)
    {
        Name = input;
        CmdType = type;
    }
}