namespace NadekoBot;

public interface IBotCredsProvider
{
    public void Reload();
    public IBotCreds GetCreds();
    public void ModifyCredsFile(Action<IBotCreds> func);
}