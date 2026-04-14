using OneOf;
using OneOf.Types;
using System.Net.Http.Json;
using System.Text.Json;

namespace NadekoBot.Modules.Patronage;

public sealed class PatreonClient(
    string clientId,
    string clientSecret,
    string initialRefreshToken,
    IHttpClientFactory httpFactory)
{
    private volatile string _refreshToken = initialRefreshToken;
    private volatile string _accessToken = string.Empty;

    private DateTime _refreshAt = DateTime.UtcNow;

    public PatreonCredentials GetCredentials()
        => new()
        {
            AccessToken = _accessToken,
            ClientId = clientId,
            ClientSecret = clientSecret,
            RefreshToken = _refreshToken,
        };

    public async Task<OneOf<Success, Error<string>>> RefreshTokenAsync(bool force)
    {
        if (!force && IsTokenValid())
            return new Success();

        using var http = httpFactory.CreateClient();
        using var content = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
        {
            new("grant_type", "refresh_token"),
            new("refresh_token", _refreshToken),
            new("client_id", clientId),
            new("client_secret", clientSecret),
        });

        var res = await http.PostAsync("https://www.patreon.com/api/oauth2/token", content);

        if (!res.IsSuccessStatusCode)
            return new Error<string>($"Request did not return a success status code. Status code: {res.StatusCode}");

        try
        {
            var data = await res.Content.ReadFromJsonAsync<PatreonRefreshData>();

            if (data is null)
                return new Error<string>("Invalid data retrieved from Patreon.");

            _refreshToken = data.RefreshToken;
            _accessToken = data.AccessToken;

            _refreshAt = DateTime.UtcNow.AddSeconds(data.ExpiresIn - 5.Minutes().TotalSeconds);
            return new Success();
        }
        catch (Exception ex)
        {
            return new Error<string>($"Error during deserialization: {ex.Message}");
        }
    }

    private async ValueTask<bool> EnsureTokenValidAsync()
    {
        if (!IsTokenValid())
        {
            var res = await RefreshTokenAsync(true);
            return res.Match(
                static _ => true,
                static err =>
                {
                    Log.Warning("Error getting token: {ErrorMessage}", err.Value);
                    return false;
                });
        }

        return true;
    }

    private bool IsTokenValid()
        => _refreshAt > DateTime.UtcNow && !string.IsNullOrWhiteSpace(_accessToken);

    public async Task<OneOf<IAsyncEnumerable<IReadOnlyCollection<PatreonMemberData>>, Error<string>>> GetMembersAsync(string campaignId)
    {
        if (!await EnsureTokenValidAsync())
            return new Error<string>("Unable to get patreon token");

        return OneOf<IAsyncEnumerable<IReadOnlyCollection<PatreonMemberData>>, Error<string>>.FromT0(
            GetMembersInternalAsync(campaignId));
    }

    private async IAsyncEnumerable<IReadOnlyCollection<PatreonMemberData>> GetMembersInternalAsync(string campaignId)
    {
        var page =
            $"https://www.patreon.com/api/oauth2/v2/campaigns/{campaignId}/members"
            + "?fields%5Bmember%5D=full_name,currently_entitled_amount_cents,last_charge_date,last_charge_status"
            + "&fields%5Buser%5D=social_connections"
            + "&include=user"
            + "&sort=-last_charge_date";
        PatreonMembersResponse? data;

        do
        {
            using var http = httpFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, page);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_accessToken}");

            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            data = await JsonSerializer.DeserializeAsync<PatreonMembersResponse>(stream);

            if (data is null)
                break;

            var userData = data.Data
                               .Join(data.Included,
                                   static m => m.Relationships.User.Data.Id,
                                   static u => u.Id,
                                   static (m, u) => new PatreonMemberData()
                                   {
                                       PatreonUserId = m.Relationships.User.Data.Id,
                                       UserId = ulong.TryParse(
                                           u.Attributes?.SocialConnections?.Discord?.UserId ?? string.Empty,
                                           out var userId)
                                           ? userId
                                           : 0,
                                       EntitledToCents = m.Attributes.CurrentlyEntitledAmountCents,
                                       LastChargeDate = m.Attributes.LastChargeDate,
                                       LastChargeStatus = m.Attributes.LastChargeStatus
                                   })
                               .ToArray();

            yield return userData;

        } while (!string.IsNullOrWhiteSpace(page = data.Links?.Next));
    }
}
