using Discord;
using NadekoBot.Medusa;

public sealed class MySnek : Snek
{
    // .hello
    [cmd]
    public async Task Hello(AnyContext ctx)
    {
        await ctx.SendConfirmAsync("Hello everyone!");
    }

    // .hello @Someone - the "hello" string from res.yml, with the user in place of {0}
    [cmd]
    public async Task Hello(AnyContext ctx, [leftover] IUser target)
    {
        await ctx.ConfirmLocalizedAsync("hello", target);
    }

    // .info John,24
    // EmployeeInfoParser below turns the text into an EmployeeInfo
    [cmd]
    public async Task Info(AnyContext ctx, [leftover] EmployeeInfo data)
    {
        await ctx.SendConfirmAsync($"Employee {data.Name} is {data.Age} years old.");
    }
}

public sealed class EmployeeInfo
{
    public required string Name { get; init; }
    public required int Age { get; init; }
}

public sealed class EmployeeInfoParser : ParamParser<EmployeeInfo>
{
    public override ValueTask<ParseResult<EmployeeInfo>> TryParseAsync(AnyContext ctx, string input)
    {
        var parts = input.Split(',', 2, StringSplitOptions.TrimEntries);

        if (parts.Length == 2 && parts[0].Length is > 0 and <= 32 && int.TryParse(parts[1], out var age))
            return new(ParseResult<EmployeeInfo>.Success(new() { Name = parts[0], Age = age }));

        return new(ParseResult<EmployeeInfo>.Fail());
    }
}
