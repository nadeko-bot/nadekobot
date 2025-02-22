#nullable disable
using OneOf;
using OneOf.Types;
using System.Net.Http.Json;
using System.Text.Json;

namespace NadekoBot.Modules.Patronage;

public class PatreonClient : IDisposable
{
    private readonly string _clientId;
    private readonly string _clientSecret;
    private string refreshToken;
    
    
    private string accessToken = string.Empty;
    private readonly HttpClient _http;
    
    private DateTime refreshAt = DateTime.UtcNow;

    public PatreonClient(string clientId, string clientSecret, string refreshToken)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        this.refreshToken = refreshToken;

        _http = new();
    }

    public void Dispose()
        => _http.Dispose();

    public PatreonCredentials GetCredentials()
        => new PatreonCredentials()
        {
            AccessToken = accessToken,
            ClientId = _clientId,
            ClientSecret = _clientSecret,
            RefreshToken = refreshToken,
        };

    public async Task<OneOf<Success, Error<string>>> RefreshTokenAsync(bool force)
    {
        if (!force && IsTokenValid())
            return new Success();

        var res = await _http.PostAsync("https://www.patreon.com/api/oauth2/token"
                                        + "?grant_type=refresh_token"
                                        + $"&refresh_token={refreshToken}"
                                        + $"&client_id={_clientId}"
                                        + $"&client_secret={_clientSecret}",
            null);

        if (!res.IsSuccessStatusCode)
            return new Error<string>($"Request did not return a sucess status code. Status code: {res.StatusCode}");

        try
        {
            var data = await res.Content.ReadFromJsonAsync<PatreonRefreshData>();

            if (data is null)
                return new Error<string>($"Invalid data retrieved from Patreon.");

            refreshToken = data.RefreshToken;
            accessToken = data.AccessToken;

            refreshAt = DateTime.UtcNow.AddSeconds(data.ExpiresIn - 5.Minutes().TotalSeconds);
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
        => refreshAt > DateTime.UtcNow && !string.IsNullOrWhiteSpace(accessToken);

    public async Task<OneOf<IAsyncEnumerable<IReadOnlyCollection<PatreonMemberData>>, Error<string>>> GetMembersAsync(string campaignId)
    {
        if (!await EnsureTokenValidAsync())
            return new Error<string>("Unable to get patreon token");

        return OneOf<IAsyncEnumerable<IReadOnlyCollection<PatreonMemberData>>, Error<string>>.FromT0(
            GetMembersInternalAsync(campaignId));
    }
    
    private async IAsyncEnumerable<IReadOnlyCollection<PatreonMemberData>> GetMembersInternalAsync(string campaignId)
    {
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization",
            $"Bearer {accessToken}");

        var page =
            $"https://www.patreon.com/api/oauth2/v2/campaigns/{campaignId}/members"
            + $"?fields%5Bmember%5D=full_name,currently_entitled_amount_cents,last_charge_date,last_charge_status"
            + $"&fields%5Buser%5D=social_connections"
            + $"&include=user"
            + $"&sort=-last_charge_date";
        PatreonMembersResponse data;

        do
        {
            var res = await _http.GetStreamAsync(page);
            data = await JsonSerializer.DeserializeAsync<PatreonMembersResponse>(res);

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