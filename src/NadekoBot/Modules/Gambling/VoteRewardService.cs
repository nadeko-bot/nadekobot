using System.Globalization;
using System.Net.Http.Json;
using Grpc.Core;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.GrpcVotesApi;

namespace NadekoBot.Modules.Gambling.Services;

public sealed class ServerCountRewardService(
    IBotCreds creds,
    IHttpClientFactory httpFactory,
    DiscordSocketClient client,
    ShardData shardData
)
    : INService, IReadyExecutor
{
    private Task dblTask = Task.CompletedTask;
    private Task discordsTask = Task.CompletedTask;

    public Task OnReadyAsync()
    {
        if (creds.Votes is null)
            return Task.CompletedTask;

        if (!string.IsNullOrWhiteSpace(creds.Votes.DblApiKey))
        {
            dblTask = Task.Run(async () =>
            {
                var dblApiKey = creds.Votes.DblApiKey;
                while (true)
                {
                    try
                    {
                        using var httpClient = httpFactory.CreateClient();
                        httpClient.DefaultRequestHeaders.Clear();
                        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", dblApiKey);
                        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json");
                        await httpClient.PostAsJsonAsync(
                            $"https://discordbotlist.com/api/v1/bots/{116275390695079945}/stats",
                            new
                            {
                                users = client.Guilds.Sum(x => x.MemberCount),
                                shard_id = shardData.ShardId,
                                guilds = client.Guilds.Count,
                                voice_connections = 0
                            });
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Unable to send server count to DBL");
                    }

                    await Task.Delay(TimeSpan.FromHours(12));
                }
            });

            if (shardData.ShardId != 0)
                return Task.CompletedTask;

            if (!string.IsNullOrWhiteSpace(creds.Votes.DiscordsApiKey))
            {
                discordsTask = Task.Run(async () =>
                {
                    var discordsApiKey = creds.Votes.DiscordsApiKey;
                    while (true)
                    {
                        try
                        {
                            using var httpClient = httpFactory.CreateClient();
                            httpClient.DefaultRequestHeaders.Clear();
                            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", discordsApiKey);
                            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type",
                                "application/json");
                            await httpClient.PostAsJsonAsync(
                                $"https://discords.com/bots/api/bot/{client.CurrentUser.Id}/setservers",
                                new
                                {
                                    server_count = client.Guilds.Count * shardData.TotalShards,
                                });
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Unable to send server count to Discords");
                        }

                        await Task.Delay(TimeSpan.FromHours(12));
                    }
                });
            }
        }

        return Task.CompletedTask;
    }
}

public class VoteRewardService(
    ShardData shardData,
    GamblingConfigService gcs,
    GamblingService gs,
    CurrencyService cs,
    DiscordSocketClient client,
    IMessageSenderService sender,
    IBotCreds creds
) : INService, IReadyExecutor
{
    private Server? _app;
    private IMessageChannel? _voteFeedChannel;

    public async Task OnReadyAsync()
    {
        if (shardData.ShardId != 0)
            return;

        if (creds.Votes is null || creds.Votes.Host is null || creds.Votes.Port == 0)
            return;

        var serverCreds = ServerCredentials.Insecure;
        var ssd = VoteService.BindService(new VotesGrpcService(this));

        _app = new()
        {
            Ports =
            {
                new(creds.Votes.Host, creds.Votes.Port, serverCreds),
            }
        };

        _app.Services.Add(ssd);
        _app.Start();

        if (gcs.Data.VoteFeedChannelId is ulong cid)
        {
            _voteFeedChannel = await client.GetChannelAsync(cid) as IMessageChannel;
        }
    }

    public void SetVoiceChannel(IMessageChannel? channel)
    {
        gcs.ModifyConfig(c => { c.VoteFeedChannelId = channel?.Id; });
        _voteFeedChannel = channel;
    }

    public async Task UserVotedAsync(ulong userId, VoteType requestType)
    {
        var gcsData = gcs.Data;
        var reward = gcsData.VoteReward;
        if (reward <= 0)
            return;

        (reward, var msg) = await gs.GetAmountAndMessage(userId, reward);
        await cs.AddAsync(userId, reward, new("vote", requestType.ToString()));

        _ = Task.Run(async () =>
        {
            try
            {
                var user = await client.GetUserAsync(userId);

                await sender
                    .Response(user)
                    .Confirm($"You've received{N(reward)} for voting!\n\n{msg}")
                    .SendAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Unable to send vote confirmation message to user {UserId}", userId);
            }
        });

        _ = Task.Run(async () =>
        {
            if (_voteFeedChannel is not null)
            {
                try
                {
                    var user = await client.GetUserAsync(userId);
                    await _voteFeedChannel.SendMessageAsync(
                        $"**{user}** just received **{N(reward)}** for voting!",
                        allowedMentions: AllowedMentions.None);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Unable to send vote reward message to user {UserId}", userId);
                }
            }
        });
    }

    private string N(long amount)
        => CurrencyHelper.N(amount, CultureInfo.InvariantCulture, gcs.Data.Currency.Sign);
}

public sealed class VotesGrpcService(VoteRewardService vrs)
    : VoteService.VoteServiceBase, INService
{
    public override async Task<GrpcVoteResult> VoteReceived(GrpcVoteData request, ServerCallContext context)
    {
        await vrs.UserVotedAsync(ulong.Parse(request.UserId), request.Type);

        return new();
    }
}