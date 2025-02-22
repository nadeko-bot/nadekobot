#nullable disable
namespace NadekoBot.Services;

/// <summary>
///     Delegate which describes a parser which can convert string input into given data type
/// </summary>
/// <typeparam name="TData">Data type to convert string to</typeparam>
public delegate bool SettingParser<TData>(string input, out TData output);