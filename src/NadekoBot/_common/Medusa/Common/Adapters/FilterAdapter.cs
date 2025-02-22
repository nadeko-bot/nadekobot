using NadekoBot.Medusa;

namespace Nadeko.Medusa.Adapters;

public class FilterAdapter : PreconditionAttribute
{
    private readonly FilterAttribute _filterAttribute;
    private readonly IMedusaStrings _strings;

    public FilterAdapter(FilterAttribute filterAttribute,
        IMedusaStrings strings)
    {
        _filterAttribute = filterAttribute;
        _strings = strings;
    }

    public override async Task<PreconditionResult> CheckPermissionsAsync(
        ICommandContext context,
        CommandInfo command,
        IServiceProvider services)
    {
        var medusaContext = ContextAdapterFactory.CreateNew(context,
            _strings,
            services);

        var result = await _filterAttribute.CheckAsync(medusaContext);
        
        if (!result)
            return PreconditionResult.FromError($"Precondition '{_filterAttribute.GetType().Name}' failed.");
        
        return PreconditionResult.FromSuccess();
    }
}