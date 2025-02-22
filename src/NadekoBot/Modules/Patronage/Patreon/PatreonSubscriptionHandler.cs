#nullable disable
namespace NadekoBot.Modules.Patronage;

/// <summary>
/// Service tasked with handling pledges on patreon
/// </summary>
public sealed class PatreonSubscriptionHandler : ISubscriptionHandler, INService
{
        private readonly IBotCredsProvider _credsProvider;
        private readonly PatreonClient _patreonClient;

    public PatreonSubscriptionHandler(IBotCredsProvider credsProvider)
    {
        _credsProvider = credsProvider;
        var botCreds = credsProvider.GetCreds();
        _patreonClient = new PatreonClient(botCreds.Patreon.ClientId, botCreds.Patreon.ClientSecret, botCreds.Patreon.RefreshToken);
    }
    
    public async IAsyncEnumerable<IReadOnlyCollection<ISubscriberData>> GetPatronsAsync()
    {
        var botCreds = _credsProvider.GetCreds();
        
        if (string.IsNullOrWhiteSpace(botCreds.Patreon.CampaignId)
            || string.IsNullOrWhiteSpace(botCreds.Patreon.ClientId)
            || string.IsNullOrWhiteSpace(botCreds.Patreon.ClientSecret)
            || string.IsNullOrWhiteSpace(botCreds.Patreon.RefreshToken))
            yield break;
        
        var result = await _patreonClient.RefreshTokenAsync(false);
        if (!result.TryPickT0(out _, out var error))
        {
            Log.Warning("Unable to refresh patreon token: {ErrorMessage}", error.Value);
            yield break;
        }
        
        var patreonCreds = _patreonClient.GetCredentials();
        
        _credsProvider.ModifyCredsFile(c =>
        {
            c.Patreon.AccessToken = patreonCreds.AccessToken;
            c.Patreon.RefreshToken = patreonCreds.RefreshToken;
        });

        IAsyncEnumerable<IEnumerable<ISubscriberData>> data;
        try
        {
            var maybeUserData = await _patreonClient.GetMembersAsync(botCreds.Patreon.CampaignId);
            data = maybeUserData.Match(
                static userData => userData,
                static err =>
                {
                    Log.Warning("Error while getting patreon members: {ErrorMessage}", err.Value);
                    return AsyncEnumerable.Empty<IReadOnlyCollection<ISubscriberData>>();
                });
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Unexpected error while refreshing patreon members: {ErroMessage}",
                ex.Message);
            
            yield break;
        }

        var now = DateTime.UtcNow;
        var firstOfThisMonth = new DateOnly(now.Year, now.Month, 1);
        await foreach (var batch in data)
        {
            // send only active patrons
            var toReturn = batch.Where(x => x.Cents > 0
                                            && x.LastCharge is { } lc
                                            && lc.ToUniversalTime().ToDateOnly() >= firstOfThisMonth)
                                .ToArray();
            
            if (toReturn.Length > 0)
                yield return toReturn;
        }
    }
}