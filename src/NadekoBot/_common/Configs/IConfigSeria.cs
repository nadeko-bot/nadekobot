namespace NadekoBot.Common.Configs;

/// <summary>
///     Base interface for available config serializers
/// </summary>
public interface IConfigSeria
{
    /// <summary>
    ///     Serialize the object to string
    /// </summary>
    public string Serialize<T>(T obj)
        where T : notnull;

    /// <summary>
    ///     Deserialize string data into an object of the specified type
    /// </summary>
    public T Deserialize<T>(string data);
}