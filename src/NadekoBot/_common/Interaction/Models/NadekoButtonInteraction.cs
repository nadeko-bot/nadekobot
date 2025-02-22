namespace NadekoBot;

public sealed class NadekoButtonInteractionHandler : NadekoInteractionBase
{
    public NadekoButtonInteractionHandler(
        DiscordSocketClient client,
        ulong authorId,
        ButtonBuilder button,
        Func<SocketMessageComponent, Task> onAction,
        bool onlyAuthor,
        bool singleUse = true,
        bool clearAfter = true)
        : base(client, authorId, button.CustomId, onAction, onlyAuthor, singleUse, clearAfter)
    {
        Button = button;
    }

    public ButtonBuilder Button { get; }

    public override void AddTo(ComponentBuilder cb)
        => cb.WithButton(Button);

}