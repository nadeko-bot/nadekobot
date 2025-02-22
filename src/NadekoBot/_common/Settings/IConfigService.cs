#nullable disable
namespace NadekoBot.Services;

/// <summary>
///     Interface that all services which deal with configs should implement
/// </summary>
public interface IConfigService
{
    /// <summary>
    ///     Name of the config
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Loads new data and publishes the new state
    /// </summary>
    void Reload();

    /// <summary>
    ///     Gets the list of props you can set
    /// </summary>
    /// <returns>List of props</returns>
    IReadOnlyList<string> GetSettableProps();

    /// <summary>
    ///     Gets the value of the specified property
    /// </summary>
    /// <param name="prop">Prop name</param>
    /// <returns>Value of the prop</returns>
    string GetSetting(string prop);

    /// <summary>
    ///     Gets the value of the specified property
    /// </summary>
    /// <param name="prop">Prop name</param>
    /// <returns>Value of the prop</returns>
    string GetComment(string prop);

    /// <summary>
    ///     Sets the value of the specified property
    /// </summary>
    /// <param name="prop">Property to set</param>
    /// <param name="newValue">Value to set the property to</param>
    /// <returns>Success</returns>
    bool SetSetting(string prop, string newValue);
}