namespace NadekoBot.Modules.Patronage;

public sealed class PatreonSubscriptionHandler(
    IBotCredsProvider credsProvider,
    IHttpClientFactory httpFactory)
    : ISubscriptionHandler, INService
{
    private readonly PatreonClient _patreonClient = new(
        credsProvider.GetCreds().Patreon.ClientId,
        credsProvider.GetCreds().Patreon.ClientSecret,
        credsProvider.GetCreds().Patreon.RefreshToken,
        httpFactory);

    public async IAsyncEnumerable<IReadOnlyCollection<ISubscriberData>> GetPatronsAsync()
    {
        var botCreds = credsProvider.GetCreds();

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

        credsProvider.ModifyCredsFile(c =>
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
            var toReturn = batch.Where(x => x.Cents > 0
                                            && x.LastCharge is { } lc
                                            && lc.ToUniversalTime().ToDateOnly() >= firstOfThisMonth)
                                .ToArray();

            if (toReturn.Length > 0)
                yield return toReturn;
        }
    }
}
