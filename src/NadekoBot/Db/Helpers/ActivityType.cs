namespace NadekoBot.Db;

public enum DbActivityType
{
    /// <summary>The user is playing a game.</summary>
    Playing,

    /// <summary>The user is streaming online.</summary>
    Streaming,

    /// <summary>The user is listening to a song.</summary>
    Listening,

    /// <summary>The user is watching some form of media.</summary>
    Watching,

    /// <summary>The user has set a custom status.</summary>
    CustomStatus,

    /// <summary>The user is competing in a game.</summary>
    Competing,
}