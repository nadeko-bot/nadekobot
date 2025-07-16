public class ResponseMessageModel
{
    public required IMessageChannel TargetChannel { get; set; }
    public MessageReference? MessageReference { get; set; }
    public string? Text { get; set; }
    public Embed? Embed { get; set; }
    public Embed[]? Embeds { get; set; }
    public required AllowedMentions SanitizeMentions { get; set; }
    public IUser? User { get; set; }
    public bool Ephemeral { get; set; }
    public NadekoInteractionBase? Interaction { get; set; }
}