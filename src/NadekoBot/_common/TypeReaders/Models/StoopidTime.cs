using System.Text.RegularExpressions;

namespace NadekoBot.Common.TypeReaders.Models;

public class ParsedTimespan
{
    private static readonly Regex _regex = new(
        @"^(?:(?<months>\d)mo)?(?:(?<weeks>\d{1,2})w)?(?:(?<days>\d{1,2})d)?(?:(?<hours>\d{1,4})h)?(?:(?<minutes>\d{1,5})m)?(?:(?<seconds>\d{1,6})s)?$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public string Input { get; set; } = string.Empty;
    public TimeSpan Time { get; set; } = default;

    private ParsedTimespan() { }

    public static ParsedTimespan FromInput(string input)
    {
        var m = _regex.Match(input);

        if (m.Length == 0)
            throw new ArgumentException("Invalid string input format.");

        var namesAndValues = new Dictionary<string, int>();

        foreach (var groupName in _regex.GetGroupNames())
        {
            if (groupName == "0")
                continue;
            if (!int.TryParse(m.Groups[groupName].Value, out var value))
            {
                namesAndValues[groupName] = 0;
                continue;
            }

            if (value < 1)
                throw new ArgumentException($"Invalid {groupName} value.");

            namesAndValues[groupName] = value;
        }

        var ts = new TimeSpan((30 * namesAndValues["months"]) + (7 * namesAndValues["weeks"]) + namesAndValues["days"],
            namesAndValues["hours"],
            namesAndValues["minutes"],
            namesAndValues["seconds"]);
        if (ts > TimeSpan.FromDays(90))
            throw new ArgumentException("Time is too long.");

        return new()
        {
            Input = input,
            Time = ts
        };
    }

    public static implicit operator TimeSpan?(ParsedTimespan? st)
        => st?.Time;
    
    public static implicit operator ParsedTimespan(TimeSpan ts)
        => new()
        {
            Input = ts.ToString(),
            Time = ts
        };
}