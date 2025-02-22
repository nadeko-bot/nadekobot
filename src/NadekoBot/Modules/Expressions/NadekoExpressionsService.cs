#nullable disable
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Common.Yml;
using NadekoBot.Db.Models;
using System.Runtime.CompilerServices;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Modules.Permissions.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NadekoBot.Modules.NadekoExpressions;

public sealed class NadekoExpressionsService : IExecOnMessage, IReadyExecutor, INService
{
    private const string MENTION_PH = "%bot.mention%";

    private const string PREPEND_EXPORT =
        """
        # Keys are triggers, Each key has a LIST of expressions in the following format:
        # - res: Response string
        #   id: Alphanumeric id used for commands related to the expression. (Note, when using .exprsimport, a new id will be generated.)
        #   react:
        #     - <List
        #     -  of
        #     - reactions>
        #   at: Whether expression allows targets (see .h .exprat)
        #   ca: Whether expression expects trigger anywhere (see .h .exprca)
        #   dm: Whether expression DMs the response (see .h .exprdm)
        #   ad: Whether expression automatically deletes triggering message (see .h .exprad)


        """;

    private static readonly ISerializer _exportSerializer = new SerializerBuilder()
                                                            .WithEventEmitter(args
                                                                => new MultilineScalarFlowStyleEmitter(args))
                                                            .WithNamingConvention(CamelCaseNamingConvention.Instance)
                                                            .WithIndentedSequences()
                                                            .ConfigureDefaultValuesHandling(DefaultValuesHandling
                                                                .OmitDefaults)
                                                            .DisableAliases()
                                                            .Build();

    public int Priority
        => 0;

    private readonly object _gexprWriteLock = new();

    private readonly TypedKey<NadekoExpression> _gexprAddedKey = new("gexpr.added");
    private readonly TypedKey<int> _gexprDeletedkey = new("gexpr.deleted");
    private readonly TypedKey<NadekoExpression> _gexprEditedKey = new("gexpr.edited");
    private readonly TypedKey<bool> _exprsReloadedKey = new("exprs.reloaded");

    // it is perfectly fine to have global expressions as an array
    // 1. expressions are almost never added (compared to how many times they are being looped through)
    // 2. only need write locks for this as we'll rebuild+replace the array on every edit
    // 3. there's never many of them (at most a thousand, usually < 100)
    private NadekoExpression[] globalExpressions = Array.Empty<NadekoExpression>();
    private ConcurrentDictionary<ulong, NadekoExpression[]> newguildExpressions = new();

    private readonly DbService _db;

    private readonly DiscordSocketClient _client;

    private readonly IPermissionChecker _permChecker;
    private readonly IBotStrings _strings;
    private readonly IPubSub _pubSub;
    private readonly IMessageSenderService _sender;
    private readonly IReplacementService _repSvc;
    private readonly Random _rng;

    private bool _isReady;
    private ConcurrentHashSet<ulong> _disabledGlobalExpressionGuilds;
    private readonly PermissionService _pc;
    private readonly ShardData _shardData;

    public NadekoExpressionsService(
        DbService db,
        IBotStrings strings,
        IBot bot,
        DiscordSocketClient client,
        IPubSub pubSub,
        IMessageSenderService sender,
        IReplacementService repSvc,
        IPermissionChecker permChecker,
        PermissionService pc,
        ShardData shardData)
    {
        _db = db;
        _client = client;
        _strings = strings;
        _pubSub = pubSub;
        _sender = sender;
        _repSvc = repSvc;
        _permChecker = permChecker;
        _pc = pc;
        _shardData = shardData;
        _rng = new NadekoRandom();

        _pubSub.Sub(_exprsReloadedKey, OnExprsShouldReload);
        pubSub.Sub(_gexprAddedKey, OnGexprAdded);
        pubSub.Sub(_gexprDeletedkey, OnGexprDeleted);
        pubSub.Sub(_gexprEditedKey, OnGexprEdited);

        _client.JoinedGuild += OnJoinedGuild;
    }

    private async Task ReloadInternal()
    {
        await using var uow = _db.GetDbContext();
        var guildItems = await uow.GetTable<NadekoExpression>()
                                  .AsNoTracking()
                                  .Where(x => x.GuildId != null
                                              && Queries.GuildOnShard(x.GuildId.Value,
                                                  _shardData.TotalShards,
                                                  _shardData.ShardId))
                                  .ToListAsyncLinqToDB();

        newguildExpressions = guildItems.GroupBy(k => k.GuildId!.Value)
                                        .ToDictionary(g => g.Key,
                                            g => g.Select(x =>
                                                  {
                                                      x.Trigger = x.Trigger.Replace(MENTION_PH,
                                                          _client.CurrentUser.Mention);
                                                      return x;
                                                  })
                                                  .ToArray())
                                        .ToConcurrent();

        _disabledGlobalExpressionGuilds = new(await uow.Set<GuildConfig>()
                                                       .Where(x => x.DisableGlobalExpressions)
                                                       .Select(x => x.GuildId)
                                                       .ToListAsyncLinqToDB());

        lock (_gexprWriteLock)
        {
            var globalItems = uow.Set<NadekoExpression>()
                                 .AsNoTracking()
                                 .Where(x => x.GuildId == null || x.GuildId == 0)
                                 .Where(x => x.Trigger != null)
                                 .AsEnumerable()
                                 .Select(x =>
                                 {
                                     x.Trigger = x.Trigger.Replace(MENTION_PH, _client.CurrentUser.Mention);
                                     return x;
                                 })
                                 .ToArray();

            globalExpressions = globalItems;
        }

        _isReady = true;
    }

    private NadekoExpression TryGetExpression(IUserMessage umsg)
    {
        if (!_isReady)
            return null;

        if (umsg.Channel is not SocketTextChannel channel)
            return null;

        var content = umsg.Content.Trim().ToLowerInvariant();

        if (newguildExpressions.TryGetValue(channel.Guild.Id, out var expressions) && expressions.Length > 0)
        {
            var expr = MatchExpressions(content, expressions);
            if (expr is not null)
                return expr;
        }

        if (_disabledGlobalExpressionGuilds.Contains(channel.Guild.Id))
            return null;

        return MatchExpressions(content, globalExpressions);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private NadekoExpression MatchExpressions(in ReadOnlySpan<char> content, NadekoExpression[] exprs)
    {
        var result = new List<NadekoExpression>(1);
        for (var i = 0; i < exprs.Length; i++)
        {
            var expr = exprs[i];
            var trigger = expr.Trigger;
            if (content.Length > trigger.Length)
            {
                // if input is greater than the trigger, it can only work if:
                // it has CA enabled
                if (expr.ContainsAnywhere)
                {
                    // if ca is enabled, we have to check if it is a word within the content
                    var wp = content.GetWordPosition(trigger);

                    // if it is, then that's valid
                    if (wp != WordPosition.None)
                        result.Add(expr);

                    // if it's not, then it cant' work under any circumstance,
                    // because content is greater than the trigger length
                    // so it can't be equal, and it's not contained as a word
                    continue;
                }

                // if CA is disabled, and expr has AllowTarget, then the
                // content has to start with the trigger followed by a space
                if (expr.AllowTarget
                    && content.StartsWith(trigger, StringComparison.OrdinalIgnoreCase)
                    && content[trigger.Length] == ' ')
                    result.Add(expr);
            }
            else if (content.Length < expr.Trigger.Length)
            {
                // if input length is less than trigger length, it means
                // that the reaction can never be triggered
            }
            else
            {
                // if input length is the same as trigger length
                // reaction can only trigger if the strings are equal
                if (content.SequenceEqual(expr.Trigger))
                    result.Add(expr);
            }
        }

        if (result.Count == 0)
            return null;

        var cancelled = result.FirstOrDefault(x => x.Response == "-");
        if (cancelled is not null)
            return cancelled;

        return result[_rng.Next(0, result.Count)];
    }

    public async Task<bool> ExecOnMessageAsync(IGuild guild, IUserMessage msg)
    {
        // maybe this message is an expression
        var expr = TryGetExpression(msg);

        if (expr is null || expr.Response == "-")
            return false;

        try
        {
            if (guild is not SocketGuild sg)
                return false;

            var result = await _permChecker.CheckPermsAsync(
                guild,
                msg.Channel,
                msg.Author,
                "ACTUALEXPRESSIONS",
                expr.Trigger
            );

            if (!result.IsAllowed)
            {
                var cache = _pc.GetCacheFor(guild.Id);
                if (cache.Verbose)
                {
                    if (result.TryPickT3(out var disallowed, out _))
                    {
                        var permissionMessage = _strings.GetText(strs.perm_prevent(disallowed.PermIndex + 1,
                                Format.Bold(disallowed.PermText)),
                            sg.Id);

                        try
                        {
                            await _sender.Response(msg.Channel)
                                         .Error(permissionMessage)
                                         .SendAsync();
                        }
                        catch
                        {
                        }

                        Log.Information("{PermissionMessage}", permissionMessage);
                    }
                }

                return true;
            }

            var cu = sg.CurrentUser;

            var channel = expr.DmResponse ? await msg.Author.CreateDMChannelAsync() : msg.Channel;

            // have no perms to speak in that channel
            if (channel is ITextChannel tc && !cu.GetPermissions(tc).SendMessages)
                return false;

            var sentMsg = await Send(expr, msg, channel);

            var reactions = expr.GetReactions();
            foreach (var reaction in reactions)
            {
                try
                {
                    await sentMsg.AddReactionAsync(reaction.ToIEmote());
                }
                catch
                {
                    Log.Warning("Unable to add reactions to message {Message} in server {GuildId}",
                        sentMsg.Id,
                        expr.GuildId);
                    break;
                }

                await Task.Delay(1000);
            }

            if (expr.AutoDeleteTrigger)
            {
                try
                {
                    await msg.DeleteAsync();
                }
                catch
                {
                }
            }

            Log.Information("s: {GuildId} c: {ChannelId} u: {UserId} | {UserName} executed expression {Expr}",
                guild.Id,
                msg.Channel.Id,
                msg.Author.Id,
                msg.Author.ToString(),
                expr.Trigger);

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error in Expression RunBehavior: {ErrorMessage}", ex.Message);
        }

        return false;
    }


    public string ResolveTriggerString(string str)
        => str.Replace("%bot.mention%", _client.CurrentUser.Mention, StringComparison.Ordinal);

    public async Task<IUserMessage> Send(
        NadekoExpression cr,
        IUserMessage ctx,
        IMessageChannel channel
    )
    {
        var trigger = ResolveTriggerString(cr.Trigger);
        var substringIndex = trigger.Length;
        if (cr.ContainsAnywhere)
        {
            var pos = ctx.Content.AsSpan().GetWordPosition(trigger);
            if (pos == WordPosition.Start)
                substringIndex += 1;
            else if (pos == WordPosition.End)
                substringIndex = ctx.Content.Length;
            else if (pos == WordPosition.Middle)
                substringIndex += ctx.Content.IndexOf(trigger, StringComparison.InvariantCulture);
        }

        var canMentionEveryone = (ctx.Author as IGuildUser)?.GuildPermissions.MentionEveryone ?? true;

        var repCtx = new ReplacementContext(client: _client,
                guild: (ctx.Channel as ITextChannel)?.Guild as SocketGuild,
                channel: ctx.Channel,
                user: ctx.Author
            )
            .WithOverride("%target%",
                () => canMentionEveryone
                    ? ctx.Content[substringIndex..].Trim()
                    : ctx.Content[substringIndex..].Trim().SanitizeMentions(true));

        var text = SmartText.CreateFrom(cr.Response);
        text = await _repSvc.ReplaceAsync(text, repCtx);

        return await _sender.Response(channel).Text(text).Sanitize(false).SendAsync();
    }

    public async Task ResetExprReactions(ulong? maybeGuildId, int id)
    {
        NadekoExpression expr;
        await using var uow = _db.GetDbContext();
        expr = uow.Set<NadekoExpression>().GetById(id);
        if (expr is null)
            return;

        expr.Reactions = string.Empty;

        await uow.SaveChangesAsync();
    }

    private Task UpdateInternalAsync(ulong? maybeGuildId, NadekoExpression expr)
    {
        if (maybeGuildId is { } guildId)
            UpdateInternal(guildId, expr);
        else
            return _pubSub.Pub(_gexprEditedKey, expr);

        return Task.CompletedTask;
    }

    private void UpdateInternal(ulong? maybeGuildId, NadekoExpression expr)
    {
        if (maybeGuildId is { } guildId)
        {
            newguildExpressions.AddOrUpdate(guildId,
                [expr],
                (_, old) =>
                {
                    var newArray = old.ToArray();
                    for (var i = 0; i < newArray.Length; i++)
                    {
                        if (newArray[i].Id == expr.Id)
                            newArray[i] = expr;
                    }

                    return newArray;
                });
        }
        else
        {
            lock (_gexprWriteLock)
            {
                var exprs = globalExpressions;
                for (var i = 0; i < exprs.Length; i++)
                {
                    if (exprs[i].Id == expr.Id)
                        exprs[i] = expr;
                }
            }
        }
    }

    private Task AddInternalAsync(ulong? maybeGuildId, NadekoExpression expr)
    {
        // only do this for perf purposes
        expr.Trigger = expr.Trigger.Replace(MENTION_PH, _client.CurrentUser.Mention);

        if (maybeGuildId is { } guildId)
            newguildExpressions.AddOrUpdate(guildId, [expr], (_, old) => old.With(expr));
        else
            return _pubSub.Pub(_gexprAddedKey, expr);

        return Task.CompletedTask;
    }

    private Task DeleteInternalAsync(ulong? maybeGuildId, int id)
    {
        if (maybeGuildId is { } guildId)
        {
            newguildExpressions.AddOrUpdate(guildId,
                Array.Empty<NadekoExpression>(),
                (key, old) => DeleteInternal(old, id, out _));

            return Task.CompletedTask;
        }

        lock (_gexprWriteLock)
        {
            var expr = Array.Find(globalExpressions, item => item.Id == id);
            if (expr is not null)
                return _pubSub.Pub(_gexprDeletedkey, expr.Id);
        }

        return Task.CompletedTask;
    }

    private NadekoExpression[] DeleteInternal(
        IReadOnlyList<NadekoExpression> exprs,
        int id,
        out NadekoExpression deleted)
    {
        deleted = null;
        if (exprs is null || exprs.Count == 0)
            return exprs as NadekoExpression[] ?? exprs?.ToArray();

        var newExprs = new NadekoExpression[exprs.Count - 1];
        for (int i = 0, k = 0; i < exprs.Count; i++, k++)
        {
            if (exprs[i].Id == id)
            {
                deleted = exprs[i];
                k--;
                continue;
            }

            newExprs[k] = exprs[i];
        }

        return newExprs;
    }

    public async Task SetExprReactions(ulong? guildId, int id, IEnumerable<string> emojis)
    {
        NadekoExpression expr;
        await using (var uow = _db.GetDbContext())
        {
            expr = uow.Set<NadekoExpression>().GetById(id);
            if (expr is null)
                return;

            expr.Reactions = string.Join("@@@", emojis);

            await uow.SaveChangesAsync();
        }

        await UpdateInternalAsync(guildId, expr);
    }

    public async Task<(bool Sucess, bool NewValue)> ToggleExprOptionAsync(ulong? guildId, int id, ExprField field)
    {
        var newVal = false;
        NadekoExpression expr;
        await using (var uow = _db.GetDbContext())
        {
            expr = uow.Set<NadekoExpression>().GetById(id);

            if (expr is null || expr.GuildId != guildId)
                return (false, false);
            if (field == ExprField.AutoDelete)
                newVal = expr.AutoDeleteTrigger = !expr.AutoDeleteTrigger;
            else if (field == ExprField.ContainsAnywhere)
                newVal = expr.ContainsAnywhere = !expr.ContainsAnywhere;
            else if (field == ExprField.DmResponse)
                newVal = expr.DmResponse = !expr.DmResponse;
            else if (field == ExprField.AllowTarget)
                newVal = expr.AllowTarget = !expr.AllowTarget;

            await uow.SaveChangesAsync();
        }

        await UpdateInternalAsync(guildId, expr);

        return (true, newVal);
    }

    public NadekoExpression GetExpression(ulong? guildId, int id)
    {
        using var uow = _db.GetDbContext();
        var expr = uow.Set<NadekoExpression>().GetById(id);
        if (expr is null || expr.GuildId != guildId)
            return null;

        return expr;
    }

    public int DeleteAllExpressions(ulong guildId)
    {
        using var uow = _db.GetDbContext();
        var count = uow.Set<NadekoExpression>().ClearFromGuild(guildId);
        uow.SaveChanges();

        newguildExpressions.TryRemove(guildId, out _);

        return count;
    }

    public bool ExpressionExists(ulong? guildId, string input)
    {
        input = input.ToLowerInvariant();

        var gexprs = globalExpressions;
        foreach (var t in gexprs)
        {
            if (t.Trigger == input)
                return true;
        }

        if (guildId is ulong gid && newguildExpressions.TryGetValue(gid, out var guildExprs))
        {
            foreach (var t in guildExprs)
            {
                if (t.Trigger == input)
                    return true;
            }
        }

        return false;
    }

    public string ExportExpressions(ulong? guildId)
    {
        var exprs = GetExpressionsFor(guildId);

        var exprsDict = exprs.GroupBy(x => x.Trigger).ToDictionary(x => x.Key, x => x.Select(ExportedExpr.FromModel));

        return PREPEND_EXPORT + _exportSerializer.Serialize(exprsDict).UnescapeUnicodeCodePoints();
    }

    public async Task<bool> ImportExpressionsAsync(ulong? guildId, string input)
    {
        Dictionary<string, List<ExportedExpr>> data;
        try
        {
            data = Yaml.Deserializer.Deserialize<Dictionary<string, List<ExportedExpr>>>(input);
            if (data.Sum(x => x.Value.Count) == 0)
                return false;
        }
        catch
        {
            return false;
        }

        await using var uow = _db.GetDbContext();
        foreach (var entry in data)
        {
            var trigger = entry.Key;
            await uow.Set<NadekoExpression>()
                     .AddRangeAsync(entry.Value
                                         .Where(expr => !string.IsNullOrWhiteSpace(expr.Res))
                                         .Select(expr => new NadekoExpression
                                         {
                                             GuildId = guildId,
                                             Response = expr.Res,
                                             Reactions = expr.React?.Join("@@@"),
                                             Trigger = trigger,
                                             AllowTarget = expr.At,
                                             ContainsAnywhere = expr.Ca,
                                             DmResponse = expr.Dm,
                                             AutoDeleteTrigger = expr.Ad
                                         }));
        }

        await uow.SaveChangesAsync();
        await TriggerReloadExpressions();
        return true;
    }

    #region Event Handlers

    public async Task OnReadyAsync()
    {
        await OnExprsShouldReload(true);
    }

    private ValueTask OnExprsShouldReload(bool _)
        => new(ReloadInternal());

    private ValueTask OnGexprAdded(NadekoExpression c)
    {
        lock (_gexprWriteLock)
        {
            var newGlobalReactions = new NadekoExpression[globalExpressions.Length + 1];
            Array.Copy(globalExpressions, newGlobalReactions, globalExpressions.Length);
            newGlobalReactions[globalExpressions.Length] = c;
            globalExpressions = newGlobalReactions;
        }

        return default;
    }

    private ValueTask OnGexprEdited(NadekoExpression c)
    {
        lock (_gexprWriteLock)
        {
            for (var i = 0; i < globalExpressions.Length; i++)
            {
                if (globalExpressions[i].Id == c.Id)
                {
                    globalExpressions[i] = c;
                    return default;
                }
            }

            // if edited expr is not found?!
            // add it
            OnGexprAdded(c);
        }

        return default;
    }

    private ValueTask OnGexprDeleted(int id)
    {
        lock (_gexprWriteLock)
        {
            var newGlobalReactions = DeleteInternal(globalExpressions, id, out _);
            globalExpressions = newGlobalReactions;
        }

        return default;
    }

    public Task TriggerReloadExpressions()
        => _pubSub.Pub(_exprsReloadedKey, true);

    #endregion

    #region Client Event Handlers

    private Task OnJoinedGuild(SocketGuild g)
    {
        newguildExpressions[g.Id] = [];
        
        return Task.CompletedTask;
    }

    #endregion

    #region Basic Operations

    public async Task<NadekoExpression> AddAsync(
        ulong? guildId,
        string key,
        string message,
        bool ca = false,
        bool ad = false,
        bool dm = false)
    {
        key = key.ToLowerInvariant();
        var expr = new NadekoExpression
        {
            GuildId = guildId,
            Trigger = key,
            Response = message,
            ContainsAnywhere = ca,
            AutoDeleteTrigger = ad,
            DmResponse = dm
        };

        if (expr.Response.Contains("%target%", StringComparison.OrdinalIgnoreCase))
            expr.AllowTarget = true;

        await using (var uow = _db.GetDbContext())
        {
            uow.Set<NadekoExpression>().Add(expr);
            await uow.SaveChangesAsync();
        }

        await AddInternalAsync(guildId, expr);

        return expr;
    }

    public async Task<NadekoExpression> EditAsync(
        ulong? guildId,
        int id,
        string message,
        bool? ca = null,
        bool? ad = null,
        bool? dm = null)
    {
        await using var uow = _db.GetDbContext();
        var expr = uow.Set<NadekoExpression>().GetById(id);

        if (expr is null || expr.GuildId != guildId)
            return null;

        // disable allowtarget if message had target, but it was removed from it
        if (!message.Contains("%target%", StringComparison.OrdinalIgnoreCase)
            && expr.Response.Contains("%target%", StringComparison.OrdinalIgnoreCase))
            expr.AllowTarget = false;

        expr.Response = message;

        // enable allow target if message is edited to contain target
        if (expr.Response.Contains("%target%", StringComparison.OrdinalIgnoreCase))
            expr.AllowTarget = true;

        expr.ContainsAnywhere = ca ?? expr.ContainsAnywhere;
        expr.AutoDeleteTrigger = ad ?? expr.AutoDeleteTrigger;
        expr.DmResponse = dm ?? expr.DmResponse;

        await uow.SaveChangesAsync();
        await UpdateInternalAsync(guildId, expr);

        return expr;
    }


    public async Task<NadekoExpression> DeleteAsync(ulong? guildId, int id)
    {
        await using var uow = _db.GetDbContext();
        var toDelete = uow.Set<NadekoExpression>().GetById(id);

        if (toDelete is null)
            return null;

        if ((toDelete.IsGlobal() && guildId is null) || guildId == toDelete.GuildId)
        {
            uow.Set<NadekoExpression>().Remove(toDelete);
            await uow.SaveChangesAsync();
            await DeleteInternalAsync(guildId, id);
            return toDelete;
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NadekoExpression[] GetExpressionsFor(ulong? maybeGuildId)
    {
        if (maybeGuildId is { } guildId)
            return newguildExpressions.TryGetValue(guildId, out var exprs) ? exprs : Array.Empty<NadekoExpression>();

        return globalExpressions;
    }

    #endregion

    public async Task<bool> ToggleGlobalExpressionsAsync(ulong guildId)
    {
        await using var ctx = _db.GetDbContext();
        var gc = ctx.GuildConfigsForId(guildId, set => set);
        var toReturn = gc.DisableGlobalExpressions = !gc.DisableGlobalExpressions;
        await ctx.SaveChangesAsync();

        if (toReturn)
            _disabledGlobalExpressionGuilds.Add(guildId);
        else
            _disabledGlobalExpressionGuilds.TryRemove(guildId);

        return toReturn;
    }


    public async Task<(IReadOnlyCollection<NadekoExpression> Exprs, int TotalCount)> FindExpressionsAsync(
        ulong guildId,
        string query,
        int page)
    {
        await using var ctx = _db.GetDbContext();

        if (newguildExpressions.TryGetValue(guildId, out var exprs))
        {
            return (exprs.Where(x => x.Trigger.Contains(query) || x.Response.Contains(query))
                         .Skip(page * 9)
                         .Take(9)
                         .ToArray(), exprs.Length);
        }

        return ([], 0);
    }
}