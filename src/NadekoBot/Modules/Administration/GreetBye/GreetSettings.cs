namespace NadekoBot.Services;

public enum GreetType
{
    Greet,
    GreetDm,
    Bye,
    Boost,
}

public class GreetSettings
{
    public int Id { get; set; }
    
    public ulong GuildId { get; set; }
    public GreetType GreetType { get; set; }
    public string? MessageText { get; set; }
    public bool IsEnabled { get; set; }
    public ulong? ChannelId { get; set; }
    public int AutoDeleteTimer { get; set; }
}