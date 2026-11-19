using System.Net;
using System.Text;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Gambling.Blackjack;
using NadekoBot.Modules.Gambling.Common.AnimalRacing;
using NadekoBot.Modules.Games.Common;
using NadekoBot.Modules.Games.Quests;
using NadekoBot.Modules.Games.SavedGames;
using NadekoBot.Modules.Games.Services;

namespace NadekoBot.Modules.Gambling.Services;

public enum RaceStartResult : byte
{
    Started,
    AlreadyRunning,
    NoAnimals,
    NoPermission
}

// Owns the races and their messages, so a race which a restart interrupted runs again without a command.
public sealed class AnimalRaceService(
    DiscordSocketClient client,
    IMessageSenderService sender,
    IBotStrings strings,
    ILocalization localization,
    ICommandHandler cmdHandler,
    GamesConfigService gamesConfig,
    GamblingConfigService gamblingConfig,
    QuestService quests,
    SavedGameStore store,
    AnimalRaceLedger ledger,
    ShardData shardData,
    TimeProvider? timeProvider = null) : INService, IReadyExecutor, ISavedGameHandler
{
    public const int MAX_RACES_PER_SERVER = 1;

    private const string BORDER = "|🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🔚|";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<ulong, RaceRun> _races = new();
    private int _started;

    public SavedGameKind Kind
        => SavedGameKind.AnimalRace;

    public Task OnReadyAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return Task.CompletedTask;

        client.MessageReceived += OnMessageReceivedInternal;
        _ = Task.Run(RefundOrphansInternalAsync);
        return Task.CompletedTask;
    }

    private Task OnMessageReceivedInternal(SocketMessage msg)
    {
        if (msg.Channel is IGuildChannel gc
            && _races.TryGetValue(gc.GuildId, out var run)
            && run.Channel.Id == gc.Id)
            run.Repost.OnMessage(msg.Id);

        return Task.CompletedTask;
    }

    private async Task RefundOrphansInternalAsync()
    {
        try
        {
            var refunded = await ledger.RefundOrphansAsync(shardData.TotalShards, shardData.ShardId);
            if (refunded > 0)
                Log.Information("Animal race refunded {Count} bets of races which no longer exist", refunded);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Animal race could not refund the bets of races which no longer exist");
        }
    }

    public async Task<RaceStartResult> StartAsync(ITextChannel channel, RaceOptions options)
    {
        // a race without animals never starts and so never ends
        var animals = gamesConfig.Data.RaceAnimals;
        if (animals.Count == 0)
            return RaceStartResult.NoAnimals;

        // members would bet on a race which they cannot see
        if (client.GetGuild(channel.GuildId)?.CurrentUser is not { } bot
            || bot.GetPermissions(channel) is not { SendMessages: true, EmbedLinks: true })
            return RaceStartResult.NoPermission;

        if (_races.ContainsKey(channel.GuildId))
            return RaceStartResult.AlreadyRunning;

        var race = new AnimalRace(options, animals.Shuffle());
        var add = await store.AddAsync(channel.GuildId,
            channel.Id,
            SavedGameKind.AnimalRace,
            MAX_RACES_PER_SERVER,
            SaveInternal(race, false));

        if (add.Result != SavedGameAddResult.Added)
            return RaceStartResult.AlreadyRunning;

        var run = new RaceRun(race, channel, add.Id, false, _time);
        if (!TryRunInternal(run))
        {
            await store.EndOrRetryAsync(add.Id);
            return RaceStartResult.AlreadyRunning;
        }

        race.Initialize();
        return RaceStartResult.Started;
    }

    public async Task<(RaceJoinResult Result, RaceAnimal? Animal)> JoinAsync(IGuildUser user, long amount)
    {
        if (!_races.TryGetValue(user.GuildId, out var run))
            return (RaceJoinResult.NoRace, null);

        var channelId = run.Channel.Id;
        var (result, racer) = await run.Race.JoinAsync(user.Id,
            user.ToString() ?? user.Username,
            amount,
            () => ledger.JoinAsync(user.GuildId, channelId, user.Id, amount));

        return (result, racer?.Animal);
    }

    public async Task<SavedGameRestoreResult> RestoreAsync(SavedGame saved)
    {
        if (SavedGameJson.Read(saved.State, SavedGameJsonContext.Default.RaceSave)
            is not { V: RaceSave.VERSION, Animals: { Length: > 0 } savedAnimals } save)
            return SavedGameRestoreResult.Drop;

        if (SavedGameChannel.Find(client, saved, out var retry) is not { } channel)
            return retry ? SavedGameRestoreResult.Retry : SavedGameRestoreResult.Drop;

        var entries = await ledger.GetEntriesAsync(saved.GuildId, saved.ChannelId);
        if (entries.Length > savedAnimals.Length)
            return SavedGameRestoreResult.Drop;

        var animals = new RaceAnimal[savedAnimals.Length];
        for (var i = 0; i < animals.Length; i++)
        {
            animals[i] = new()
            {
                Icon = savedAnimals[i].Icon ?? string.Empty,
                Name = savedAnimals[i].Name ?? string.Empty
            };
        }

        var options = new RaceOptions { StartTime = save.StartTime };
        options.NormalizeOptions();

        var race = new AnimalRace(options, animals);
        foreach (var entry in entries)
        {
            var name = channel.Guild.GetUser(entry.UserId)?.ToString() ?? MentionUtils.MentionUser(entry.UserId);
            if (!race.AddRestored(entry.UserId, name, entry.Amount))
                return SavedGameRestoreResult.Drop;
        }

        var run = new RaceRun(race, channel, saved.Id, save.Running, _time);
        try
        {
            if (save.Running)
            {
                await sender.Response(channel)
                    .Confirm(GetTextInternal(run, strs.animal_race), GetTextInternal(run, strs.animal_race_restarted))
                    .SendAsync();
            }
            else
            {
                await SendRestoredStartingInternalAsync(run);
            }
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            Log.Warning(ex, "Animal race could not post in channel {ChannelId} after a restart", saved.ChannelId);
            return SavedGameRestoreResult.Drop;
        }

        if (!TryRunInternal(run))
            return SavedGameRestoreResult.Drop;

        if (save.Running)
            race.StartNow();
        else
            race.Initialize();

        return SavedGameRestoreResult.Restored;
    }

    public async Task DropAsync(SavedGame saved)
        => await ledger.RefundAsync(saved.Id, saved.GuildId, saved.ChannelId);

    private bool TryRunInternal(RaceRun run)
    {
        if (!_races.TryAdd(run.Channel.GuildId, run))
            return false;

        var race = run.Race;
        race.OnStartingFailed += _ => OnStartingFailedInternalAsync(run);
        race.OnStarted += _ => OnStartedInternalAsync(run);
        race.OnStateUpdate += _ => OnStateUpdateInternalAsync(run);
        race.OnEnded += _ => OnEndedInternalAsync(run);
        return true;
    }

    private void RemoveInternal(RaceRun run)
    {
        if (_races.TryGetValue(run.Channel.GuildId, out var current) && ReferenceEquals(current, run))
            _races.TryRemove(run.Channel.GuildId, out _);
    }

    private static byte[] SaveInternal(AnimalRace race, bool running)
    {
        var animals = new RaceAnimalSave[race.Animals.Count];
        for (var i = 0; i < animals.Length; i++)
            animals[i] = new() { Icon = race.Animals[i].Icon, Name = race.Animals[i].Name };

        return SavedGameJson.Write(new RaceSave
            {
                V = RaceSave.VERSION,
                StartTime = race.Options.StartTime,
                Animals = animals,
                Running = running
            },
            SavedGameJsonContext.Default.RaceSave);
    }

    private string GetTextInternal(RaceRun run, LocStr str)
        => strings.GetText(str, run.Channel.GuildId);

    private string FormatCurrencyInternal(RaceRun run, long amount)
        => CurrencyHelper.N(amount,
            localization.GetCultureInfo(run.Channel.GuildId),
            gamblingConfig.Data.Currency.Sign);

    private async Task SendRestoredStartingInternalAsync(RaceRun run)
    {
        var text = GetTextInternal(run, strs.game_restored)
                   + "\n"
                   + GetTextInternal(run, strs.animal_race_starting(run.Race.Options.StartTime));

        await sender.Response(run.Channel)
            .Confirm(GetTextInternal(run, strs.animal_race),
                text,
                footer: GetTextInternal(run, strs.animal_race_join_instr(cmdHandler.GetPrefix(run.Channel.GuildId))))
            .SendAsync();
    }

    private async Task OnStartingFailedInternalAsync(RaceRun run)
    {
        await RefundOrRetryInternalAsync(run);
        RemoveInternal(run);

        await TrySendInternalAsync(run,
            sender.Response(run.Channel).Error(GetTextInternal(run, strs.animal_race_failed)));
    }

    private async Task OnStartedInternalAsync(RaceRun run)
    {
        // a race which ran before a restart already counted for the quest
        if (!run.WasRunning)
        {
            try
            {
                await store.SaveNowAsync(run.SavedId, SaveInternal(run.Race, true));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Animal race could not save the start of the race in channel {ChannelId}", run.Channel.Id);
            }

            foreach (var user in run.Race.Users)
            {
                try
                {
                    await quests.ReportActionAsync(user.UserId, QuestEventType.RaceJoined);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Animal race could not report the race quest");
                }
            }
        }

        var text = run.Race.Users.Count == run.Race.MaxUsers
            ? GetTextInternal(run, strs.animal_race_full)
            : GetTextInternal(run, strs.animal_race_starting_with_x(run.Race.Users.Count));

        await TrySendInternalAsync(run,
            sender.Response(run.Channel).Confirm(GetTextInternal(run, strs.animal_race), text));
    }

    private async Task OnStateUpdateInternalAsync(RaceRun run)
    {
        if (run.CannotPost)
            return;

        var embed = sender.CreateEmbed(run.Channel.GuildId)
            .WithOkColor()
            .WithTitle(GetTextInternal(run, strs.animal_race))
            .WithDescription(RenderInternal(run.Race));

        var old = run.Message;
        if (old is not null && !run.Repost.IsDue)
        {
            try
            {
                await old.ModifyAsync(x => x.Embed = embed.Build());
                return;
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
            {
                // someone deleted the race message, so the bot posts it again
                old = null;
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                run.CannotPost = true;
                return;
            }
        }

        // the race goes to the bottom of the chat again, so the members can see it
        IUserMessage msg;
        try
        {
            msg = await sender.Response(run.Channel).Embed(embed).SendAsync();
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            run.CannotPost = true;
            return;
        }

        run.Message = msg;
        run.Repost.OnTablePosted(msg.Id);

        if (old is not null)
            await TryDeleteInternalAsync(old);
    }

    private static async Task TryDeleteInternalAsync(IUserMessage message)
    {
        try
        {
            await message.DeleteAsync();
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            Log.Information("Animal race could not delete its old board in channel {ChannelId}", message.Channel.Id);
        }
    }

    private static string RenderInternal(AnimalRace race)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BORDER);
        foreach (var user in race.Users)
        {
            sb.Append($"{user.Progress * 100 / AnimalRace.FINISH,-2}%|");
            sb.Append('‣', user.Progress);
            sb.Append(user.Animal.Icon);

            var place = race.FinishedUsers.IndexOf(user);
            if (place != -1)
                sb.Append($"#{place + 1} {(place == 0 ? "🏆" : "")}");

            sb.AppendLine();
        }

        sb.Append(BORDER);
        return sb.ToString();
    }

    private async Task OnEndedInternalAsync(RaceRun run)
    {
        var race = run.Race;
        var winner = race.Failed || race.FinishedUsers.Count == 0 ? null : race.FinishedUsers[0];
        if (winner is null)
        {
            await RefundOrRetryInternalAsync(run);
            RemoveInternal(run);
            await TrySendInternalAsync(run, sender.Response(run.Channel).Error(GetTextInternal(run, strs.error_occured)));
            return;
        }

        var multi = race.Multi;
        long paid;
        try
        {
            paid = (await ledger.SettleAsync(run.SavedId, run.Channel.GuildId, run.Channel.Id, winner.UserId, multi)).Payout;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Animal race could not pay the race in channel {ChannelId}. The bot tries again", run.Channel.Id);
            store.RetryLater(run.SavedId,
                () => ledger.SettleAsync(run.SavedId, run.Channel.GuildId, run.Channel.Id, winner.UserId, multi));

            // the retry pays this amount
            paid = (long)(winner.Bet * multi);
        }

        RemoveInternal(run);

        if (paid > 0)
        {
            await TrySendInternalAsync(run,
                sender.Response(run.Channel)
                    .Embed(sender.CreateEmbed(run.Channel.GuildId)
                        .WithOkColor()
                        .WithTitle(GetTextInternal(run, strs.animal_race))
                        .WithDescription(GetTextInternal(run,
                            strs.animal_race_won_money(Format.Bold(winner.Username),
                                winner.Animal.Icon,
                                FormatCurrencyInternal(run, paid))))
                        .WithFooter($"x{multi:F2}")));
            return;
        }

        await TrySendInternalAsync(run,
            sender.Response(run.Channel)
                .Confirm(GetTextInternal(run, strs.animal_race),
                    GetTextInternal(run, strs.animal_race_won(Format.Bold(winner.Username), winner.Animal.Icon))));
    }

    private async Task RefundOrRetryInternalAsync(RaceRun run)
    {
        try
        {
            await ledger.RefundAsync(run.SavedId, run.Channel.GuildId, run.Channel.Id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Animal race could not refund the race in channel {ChannelId}. The bot tries again", run.Channel.Id);
            store.RetryLater(run.SavedId, () => ledger.RefundAsync(run.SavedId, run.Channel.GuildId, run.Channel.Id));
        }
    }

    private static async Task TrySendInternalAsync(RaceRun run, ResponseBuilder response)
    {
        if (run.CannotPost)
            return;

        try
        {
            await response.SendAsync();
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            run.CannotPost = true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Animal race could not post in channel {ChannelId}", run.Channel.Id);
        }
    }

    private sealed class RaceRun(AnimalRace race, ITextChannel channel, int savedId, bool wasRunning, TimeProvider time)
    {
        public AnimalRace Race { get; } = race;
        public ITextChannel Channel { get; } = channel;
        public int SavedId { get; } = savedId;
        public bool WasRunning { get; } = wasRunning;
        public BlackjackRepostGate Repost { get; } = new(time);
        public IUserMessage? Message { get; set; }
        public bool CannotPost { get; set; }
    }
}
