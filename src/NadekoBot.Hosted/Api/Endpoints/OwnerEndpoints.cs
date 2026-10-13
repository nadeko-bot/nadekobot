using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

public static class OwnerEndpoints
{
    public static RouteGroupBuilder MapOwnersEndpoints(this RouteGroupBuilder owner)
    {
        owner.MapGet("/owners", GetOwners);
        owner.MapPut("/owners", SetOwners);
        return owner;
    }

    private static async Task<IResult> GetOwners(IBotCredsProvider credsProvider, DiscordSocketClient client, DbService db)
        => Results.Json(await ReadOwnersInternalAsync(credsProvider, client, db), ApiJsonContext.Default.OwnersDto);

    // The reload refills the owner list which every owner check reads, so the next command uses it.
    private static async Task<IResult> SetOwners(
        SetOwnersRequest body,
        CredsStore creds,
        IBotCredsProvider credsProvider,
        DiscordSocketClient client,
        DbService db)
    {
        if (!OwnerIdList.TryParse(body.Ids, out var owners))
            return ApiErrors.InvalidValue;

        await creds.SetOwnersAsync(owners);
        credsProvider.Reload();
        Log.Information("The bot owners were replaced over the API");
        return Results.Json(await ReadOwnersInternalAsync(credsProvider, client, db), ApiJsonContext.Default.OwnersDto);
    }

    // Names come from the cache or the names the bot stored; the bot never calls Discord only for a name.
    private static async Task<OwnersDto> ReadOwnersInternalAsync(
        IBotCredsProvider credsProvider,
        DiscordSocketClient client,
        DbService db)
    {
        var ownerIds = credsProvider.GetCreds().OwnerIds;
        var ids = new ulong[ownerIds.Count];
        ownerIds.CopyTo(ids, 0);

        var names = new string?[ids.Length];
        var uncached = new List<ulong>(ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            if ((names[i] = client.GetUser(ids[i])?.Username) is null)
                uncached.Add(ids[i]);
        }

        var stored = await XpEndpoints.ReadStoredUsernamesAsync(db, uncached);
        var owners = new OwnerDto[ids.Length];
        for (var i = 0; i < ids.Length; i++)
            owners[i] = new(ids[i].ToString(), names[i] ?? stored.GetValueOrDefault(ids[i]));

        return new(owners);
    }
}
