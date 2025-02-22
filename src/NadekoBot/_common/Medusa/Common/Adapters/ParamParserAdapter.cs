using NadekoBot.Medusa;

public sealed class ParamParserAdapter<T> : TypeReader
{
    private readonly ParamParser<T> _parser;
    private readonly IMedusaStrings _strings;
    private readonly IServiceProvider _services;

    public ParamParserAdapter(ParamParser<T> parser,
        IMedusaStrings strings,
        IServiceProvider services)
    {
        _parser = parser;
        _strings = strings;
        _services = services;
    }

    public override async Task<Discord.Commands.TypeReaderResult> ReadAsync(
        ICommandContext context,
        string input,
        IServiceProvider services)
    {
        var medusaContext = ContextAdapterFactory.CreateNew(context,
            _strings,
            _services);
        
        var result = await _parser.TryParseAsync(medusaContext, input);
        
        if(result.IsSuccess)
            return Discord.Commands.TypeReaderResult.FromSuccess(result.Data);
        
        return Discord.Commands.TypeReaderResult.FromError(CommandError.Unsuccessful, "Invalid input");
    }
}