using System.Globalization;
using System.Net.Http.Json;
using NadekoBot.Common.ModuleBehaviors;

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
                            $"https://discordbotlist.com/api/v1/bots/{client.CurrentUser.Id}/stats",
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
        }

        if (shardData.ShardId != 0 || string.IsNullOrWhiteSpace(creds.Votes.DiscordsApiKey))
            return Task.CompletedTask;

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
                    httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json");
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

        return Task.CompletedTask;
    }
}

// The names are also the transaction notes and the text of the reward message.
public enum VoteType
{
    Topgg,
    Discordbotlist,
    Discords
}

public class VoteRewardService(
    ShardData shardData,
    GamblingConfigService gcs,
    GamblingService gs,
    CurrencyService cs,
    DiscordSocketClient client,
    IMessageSenderService sender,
    ICoordinator coordinator
) : INService, IReadyExecutor
{
    private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(5);

    private IMessageChannel? _voteFeedChannel;

    public async Task OnReadyAsync()
    {
        if (shardData.ShardId != 0)
            return;

        // NadekoBot.VotesApi gives each vote to the coordinator, and shard 0 takes them from there.
        if (coordinator is RemoteHttpCoordinator remote)
            _ = Task.Run(() => TakeVotesLoopAsync(remote));

        if (gcs.Data.VoteFeedChannelId is ulong cid)
        {
            _voteFeedChannel = await client.GetChannelAsync(cid) as IMessageChannel;
        }
    }

    private async Task TakeVotesLoopAsync(RemoteHttpCoordinator remote)
    {
        var failing = false;
        while (true)
        {
            CoordVote[] votes;
            try
            {
                votes = await remote.TakeVotesAsync();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (OperationCanceledException) when (remote.IsStopped)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!failing)
                    Log.Warning("Could not take votes from the coordinator: {Message}", ex.Message);

                failing = true;
                await Task.Delay(_retryDelay);
                continue;
            }

            if (failing)
                Log.Information("Shard 0 takes votes from the coordinator again");

            failing = false;
            foreach (var vote in votes)
            {
                if (!ulong.TryParse(vote.UserId, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
                    || !TryParseVoteType(vote.Type, out var type))
                {
                    Log.Warning("The coordinator gave a vote which is not valid: {UserId} on {Platform}", vote.UserId, vote.Type);
                    continue;
                }

                try
                {
                    await UserVotedAsync(userId, type);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not give the vote reward to {UserId} for {Platform}", userId, type);
                }
            }
        }
    }

    private static bool TryParseVoteType(string? text, out VoteType type)
    {
        switch (text)
        {
            case "topgg":
                type = VoteType.Topgg;
                return true;
            case "discordbotlist":
                type = VoteType.Discordbotlist;
                return true;
            case "discords":
                type = VoteType.Discords;
                return true;
            default:
                type = default;
                return false;
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
                    .Confirm($"You've received{N(reward)} for voting on {requestType}!\n\n{msg}")
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