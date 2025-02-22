using NadekoBot.Modules.Utility.Services;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using NadekoBot.Modules.Searches.Common;

namespace NadekoBot.Modules.Utility;

public partial class Utility : NadekoModule
{
    public enum CreateInviteType
    {
        Any,
        New
    }

    public enum MeOrBot
    {
        Me,
        Bot
    }

    private static readonly JsonSerializerOptions _showEmbedSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = LowerCaseNamingPolicy.Default
    };

    private readonly DiscordSocketClient _client;
    private readonly ICoordinator _coord;
    private readonly IStatsService _stats;
    private readonly IBotCreds _creds;
    private readonly DownloadTracker _tracker;
    private readonly IHttpClientFactory _httpFactory;
    private readonly VerboseErrorsService _veService;
    private readonly IServiceProvider _services;
    private readonly AfkService _afkService;

    public Utility(
        DiscordSocketClient client,
        ICoordinator coord,
        IStatsService stats,
        IBotCreds creds,
        DownloadTracker tracker,
        IHttpClientFactory httpFactory,
        VerboseErrorsService veService,
        IServiceProvider services,
        AfkService afkService)
    {
        _client = client;
        _coord = coord;
        _stats = stats;
        _creds = creds;
        _tracker = tracker;
        _httpFactory = httpFactory;
        _veService = veService;
        _services = services;
        _afkService = afkService;
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.ManageMessages)]
    [Priority(1)]
    public async Task Say(ITextChannel channel, [Leftover] SmartText message)
    {
        if (!((IGuildUser)ctx.User).GetPermissions(channel).SendMessages)
        {
            await Response().Error(strs.insuf_perms_u).SendAsync();
            return;
        }

        if (!((ctx.Guild as SocketGuild)?.CurrentUser.GetPermissions(channel).SendMessages ?? false))
        {
            await Response().Error(strs.insuf_perms_i).SendAsync();
            return;
        }

        var repCtx = new ReplacementContext(Context);
        message = await repSvc.ReplaceAsync(message, repCtx);

        await Response()
              .Text(message)
              .Channel(channel)
              .UserBasedMentions()
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.ManageMessages)]
    [Priority(0)]
    public Task Say([Leftover] SmartText message)
        => Say((ITextChannel)ctx.Channel, message);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task WhosPlaying([Leftover] string? game)
    {
        game = game?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(game))
            return;

        if (ctx.Guild is not SocketGuild socketGuild)
        {
            Log.Warning("Can't cast guild to socket guild");
            return;
        }


        var userNames = new List<IUser>(socketGuild.Users.Count / 100);
        foreach (var user in socketGuild.Users)
        {
            if (user.Activities.Any(x => x.Name is not null && x.Name.ToUpperInvariant() == game))
            {
                userNames.Add(user);
            }
        }

        await Response()
              .Sanitize()
              .Paginated()
              .Items(userNames)
              .PageSize(20)
              .Page((names, _) =>
              {
                  if (names.Count == 0)
                  {
                      return CreateEmbed()
                                    .WithErrorColor()
                                    .WithDescription(GetText(strs.nobody_playing_game));
                  }

                  var eb = CreateEmbed()
                                  .WithOkColor();

                  var users = names.Join('\n');

                  return eb.WithDescription(users);
              })
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [Priority(0)]
    public async Task InRole(int page, [Leftover] IRole? role = null)
    {
        if (--page < 0)
            return;

        await ctx.Channel.TriggerTypingAsync();
        await _tracker.EnsureUsersDownloadedAsync(ctx.Guild);

        var users = await ctx.Guild.GetUsersAsync(
            CacheMode.CacheOnly
        );

        users = role is null
            ? users
            : users.Where(u => u.RoleIds.Contains(role.Id)).ToList();


        var roleUsers = new List<string>(users.Count);
        foreach (var u in users)
        {
            roleUsers.Add($"{u.Mention} {Format.Spoiler(Format.Code(u.Username))}");
        }

        await Response()
              .Paginated()
              .Items(roleUsers)
              .PageSize(20)
              .CurrentPage(page)
              .Page((pageUsers, _) =>
              {
                  if (pageUsers.Count == 0)
                      return CreateEmbed().WithOkColor().WithDescription(GetText(strs.no_user_on_this_page));

                  var roleName = Format.Bold(role?.Name ?? "No Role");

                  return CreateEmbed()
                                .WithOkColor()
                                .WithTitle(GetText(strs.inrole_list(role?.GetIconUrl() + roleName, roleUsers.Count)))
                                .WithDescription(string.Join("\n", pageUsers));
              })
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [Priority(1)]
    public Task InRole([Leftover] IRole? role = null)
        => InRole(1, role);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task CheckPerms(MeOrBot who = MeOrBot.Me)
    {
        var user = who == MeOrBot.Me ? (IGuildUser)ctx.User : ((SocketGuild)ctx.Guild).CurrentUser;
        var perms = user.GetPermissions((ITextChannel)ctx.Channel);
        await SendPerms(perms);
    }

    private async Task SendPerms(ChannelPermissions perms)
    {
        var builder = new StringBuilder();
        foreach (var p in perms.GetType()
                               .GetProperties()
                               .Where(static p =>
                               {
                                   var method = p.GetGetMethod();
                                   if (method is null)
                                       return false;
                                   return !method.GetParameters().Any();
                               }))
            builder.AppendLine($"{p.Name} : {p.GetValue(perms, null)}");
        await Response().Confirm(builder.ToString()).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task UserId([Leftover] IGuildUser? target = null)
    {
        var usr = target ?? ctx.User;
        await Response()
              .Confirm(strs.userid("🆔",
                  Format.Bold(usr.ToString()),
                  Format.Code(usr.Id.ToString())))
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task RoleId([Leftover] IRole role)
        => await Response()
                 .Confirm(strs.roleid("🆔",
                     Format.Bold(role.ToString()),
                     Format.Code(role.Id.ToString())))
                 .SendAsync();

    [Cmd]
    public async Task ChannelId()
        => await Response().Confirm(strs.channelid("🆔", Format.Code(ctx.Channel.Id.ToString()))).SendAsync();

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task ServerId()
        => await Response().Confirm(strs.serverid("🆔", Format.Code(ctx.Guild.Id.ToString()))).SendAsync();

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task Roles(IGuildUser? target, int page = 1)
    {
        var guild = ctx.Guild;

        const int rolesPerPage = 20;

        if (page is < 1 or > 100)
            return;

        if (target is not null)
        {
            var roles = target.GetRoles()
                              .Except(new[] { guild.EveryoneRole })
                              .OrderBy(r => -r.Position)
                              .Skip((page - 1) * rolesPerPage)
                              .Take(rolesPerPage)
                              .ToArray();
            if (!roles.Any())
                await Response().Error(strs.no_roles_on_page).SendAsync();
            else
            {
                await Response()
                      .Confirm(GetText(strs.roles_page(page, Format.Bold(target.ToString()))),
                          "\n• " + string.Join("\n• ", (IEnumerable<IRole>)roles))
                      .SendAsync();
            }
        }
        else
        {
            var roles = guild.Roles.Except(new[] { guild.EveryoneRole })
                             .OrderBy(r => -r.Position)
                             .Skip((page - 1) * rolesPerPage)
                             .Take(rolesPerPage)
                             .ToArray();
            if (!roles.Any())
                await Response().Error(strs.no_roles_on_page).SendAsync();
            else
            {
                await Response()
                      .Confirm(GetText(strs.roles_all_page(page)),
                          "\n• " + string.Join("\n• ", (IEnumerable<IRole>)roles).SanitizeMentions(true))
                      .SendAsync();
            }
        }
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public Task Roles(int page = 1)
        => Roles(null, page);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task ChannelTopic([Leftover] ITextChannel? channel = null)
    {
        if (channel is null)
            channel = (ITextChannel)ctx.Channel;

        var topic = channel.Topic;
        if (string.IsNullOrWhiteSpace(topic))
            await Response().Error(strs.no_topic_set).SendAsync();
        else
            await Response().Confirm(GetText(strs.channel_topic), topic).SendAsync();
    }

    [Cmd]
    public async Task Stats()
    {
        var ownerIds = string.Join("\n", _creds.OwnerIds);
        if (string.IsNullOrWhiteSpace(ownerIds))
            ownerIds = "-";

        var eb = CreateEmbed()
                        .WithOkColor()
                        .WithAuthor($"NadekoBot v{StatsService.BotVersion}",
                            "https://nadeko-pictures.nyc3.digitaloceanspaces.com/other/avatar.png",
                            "https://nadekobot.readthedocs.io/en/latest/")
                        .AddField(GetText(strs.author), _stats.Author, true)
                        .AddField(GetText(strs.botid), _client.CurrentUser.Id.ToString(), true)
                        .AddField(GetText(strs.shard),
                            $"#{_client.ShardId} / {_creds.TotalShards}",
                            true)
                        .AddField(GetText(strs.commands_ran), _stats.CommandsRan.ToString(), true)
                        .AddField(GetText(strs.messages),
                            $"{_stats.MessageCounter} ({_stats.MessagesPerSecond:F2}/sec)",
                            true)
                        .AddField(GetText(strs.memory),
                            FormattableString.Invariant($"{_stats.GetPrivateMemoryMegabytes():F2} MB"),
                            true)
                        .AddField(GetText(strs.owner_ids), ownerIds, true)
                        .AddField(GetText(strs.uptime), _stats.GetUptimeString("\n"), true)
                        .AddField(GetText(strs.presence),
                            GetText(strs.presence_txt(_coord.GetGuildCount(),
                                _stats.TextChannels,
                                _stats.VoiceChannels)),
                            true);

        await Response()
              .Embed(eb)
              .SendAsync();
    }

    [Cmd]
    public async Task Showemojis([Leftover] string _)
    {
        var tags = ctx.Message.Tags.Where(t => t.Type == TagType.Emoji).Select(t => (Emote)t.Value);

        var result = string.Join("\n", tags.Select(m => GetText(strs.showemojis(m, m.Url))));

        if (string.IsNullOrWhiteSpace(result))
            await Response().Error(strs.showemojis_none).SendAsync();
        else
            await Response().Text(result.TrimTo(2000)).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [BotPerm(GuildPerm.ManageEmojisAndStickers)]
    [UserPerm(GuildPerm.ManageEmojisAndStickers)]
    [Priority(2)]
    public Task EmojiAdd(string name, Emote emote)
        => EmojiAdd(name, emote.Url);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [BotPerm(GuildPerm.ManageEmojisAndStickers)]
    [UserPerm(GuildPerm.ManageEmojisAndStickers)]
    [Priority(1)]
    public Task EmojiAdd(Emote emote)
        => EmojiAdd(emote.Name, emote.Url);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [BotPerm(GuildPerm.ManageEmojisAndStickers)]
    [UserPerm(GuildPerm.ManageEmojisAndStickers)]
    [Priority(0)]
    public async Task EmojiAdd(string name, string? url = null)
    {
        name = name.Trim(':');

        url ??= ctx.Message.Attachments.FirstOrDefault()?.Url;

        if (url is null)
            return;

        using var http = _httpFactory.CreateClient();
        using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (!res.IsImage() || res.GetContentLength() > 262_144)
        {
            await Response().Error(strs.invalid_emoji_link).SendAsync();
            return;
        }

        await using var imgStream = await res.Content.ReadAsStreamAsync();
        Emote em;
        try
        {
            em = await ctx.Guild.CreateEmoteAsync(name, new(imgStream));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error adding emoji on server {GuildId}", ctx.Guild.Id);

            await Response().Error(strs.emoji_add_error).SendAsync();
            return;
        }

        await Response().Confirm(strs.emoji_added(em.ToString())).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [BotPerm(GuildPerm.ManageEmojisAndStickers)]
    [UserPerm(GuildPerm.ManageEmojisAndStickers)]
    [Priority(0)]
    public async Task EmojiRemove(params Emote[] emotes)
    {
        if (emotes.Length == 0)
            return;

        var g = (SocketGuild)ctx.Guild;

        var fails = new List<Emote>();
        foreach (var emote in emotes)
        {
            var guildEmote = g.Emotes.FirstOrDefault(x => x.Id == emote.Id);
            if (guildEmote is null)
            {
                fails.Add(emote);
            }
            else
            {
                await ctx.Guild.DeleteEmoteAsync(guildEmote);
            }
        }

        if (fails.Count > 0)
        {
            await Response().Pending(strs.emoji_not_removed(fails.Select(x => x.ToString()).Join(" "))).SendAsync();
            return;
        }

        await ctx.OkAsync();
    }


    [Cmd]
    [RequireContext(ContextType.Guild)]
    [BotPerm(GuildPerm.ManageEmojisAndStickers)]
    [UserPerm(GuildPerm.ManageEmojisAndStickers)]
    public async Task StickerAdd(string? name = null, string? description = null, params string[] tags)
    {
        string format;
        Stream? stream = null;

        try
        {
            if (ctx.Message.Stickers.Count is 1 && ctx.Message.Stickers.First() is SocketSticker ss)
            {
                name ??= ss.Name;
                description = ss.Description;
                tags = tags is null or { Length: 0 } ? ss.Tags.ToArray() : tags;
                format = FormatToExtension(ss.Format);

                using var http = _httpFactory.CreateClient();
                stream = await http.GetStreamAsync(ss.GetStickerUrl());
            }
            else if (ctx.Message.Attachments.Count is 1 && name is not null)
            {
                if (tags.Length == 0)
                    tags = [name];

                if (ctx.Message.Attachments.Count != 1)
                {
                    await Response().Error(strs.sticker_error).SendAsync();
                    return;
                }

                var attach = ctx.Message.Attachments.First();


                if (attach.Size > 512_000 || attach.Width != 300 || attach.Height != 300)
                {
                    await Response().Error(strs.sticker_error).SendAsync();
                    return;
                }

                format = attach.Filename
                               .Split('.')
                               .Last()
                               .ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(format) || (format != "png" && format != "apng"))
                {
                    await Response().Error(strs.sticker_error).SendAsync();
                    return;
                }

                using var http = _httpFactory.CreateClient();
                stream = await http.GetStreamAsync(attach.Url);
            }
            else
            {
                await Response().Error(strs.sticker_error).SendAsync();
                return;
            }

            try
            {
                await ctx.Guild.CreateStickerAsync(
                    name,
                    stream,
                    $"{name}.{format}",
                    tags,
                    string.IsNullOrWhiteSpace(description) ? "Missing description" : description
                );

                await ctx.OkAsync();
            }
            catch
                (Exception ex)
            {
                Log.Warning(ex, "Error occurred while adding a sticker: {Message}", ex.Message);
                await Response().Error(strs.error_occured).SendAsync();
            }
        }
        finally
        {
            await (stream?.DisposeAsync() ?? ValueTask.CompletedTask);
        }
    }

    private static string FormatToExtension(StickerFormatType format)
    {
        switch (format)
        {
            case StickerFormatType.None:
            case StickerFormatType.Png:
            case StickerFormatType.Apng:
                return "png";
            case StickerFormatType.Lottie:
                return "lottie";
            default:
                throw new ArgumentException(nameof(format));
        }
    }

    [Cmd]
    [OwnerOnly]
    public async Task ServerList(int page = 1)
    {
        page -= 1;

        if (page < 0)
            return;

        var allGuilds = _client.Guilds
                               .OrderBy(g => g.Name)
                               .ToList();

        await Response()
              .Paginated()
              .Items(allGuilds)
              .PageSize(9)
              .Page((guilds, _) =>
              {
                  if (!guilds.Any())
                  {
                      return CreateEmbed()
                                    .WithDescription(GetText(strs.listservers_none))
                                    .WithErrorColor();
                  }

                  var embed = CreateEmbed()
                                     .WithOkColor();
                  foreach (var guild in guilds)
                      embed.AddField(guild.Name, GetText(strs.listservers(guild.Id, guild.MemberCount, guild.OwnerId)));

                  return embed;
              })
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public Task ShowEmbed(ulong messageId)
        => ShowEmbed((ITextChannel)ctx.Channel, messageId);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task ShowEmbed(ITextChannel ch, ulong messageId)
    {
        var user = (IGuildUser)ctx.User;
        var perms = user.GetPermissions(ch);
        if (!perms.ReadMessageHistory || !perms.ViewChannel)
        {
            await Response().Error(strs.insuf_perms_u).SendAsync();
            return;
        }

        var msg = await ch.GetMessageAsync(messageId);
        if (msg is null)
        {
            await Response().Error(strs.msg_not_found).SendAsync();
            return;
        }

        if (!msg.Embeds.Any())
        {
            await Response().Error(strs.not_found).SendAsync();
            return;
        }

        var json = new SmartEmbedTextArray()
        {
            Content = msg.Content,
            Embeds = msg.Embeds
                        .Map(x => new SmartEmbedArrayElementText(x))
        }.ToJson(_showEmbedSerializerOptions);

        await Response().Confirm(Format.Code(json, "json").Replace("](", "]\\(")).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [OwnerOnly]
    public async Task SaveChat(int cnt)
    {
        var msgs = new List<IMessage>(cnt);
        await ctx.Channel.GetMessagesAsync(cnt).ForEachAsync(dled => msgs.AddRange(dled));

        var title = $"Chatlog-{ctx.Guild.Name}/#{ctx.Channel.Name}-{DateTime.Now}.txt";
        var grouping = msgs.GroupBy(x => $"{x.CreatedAt.Date:dd.MM.yyyy}")
                           .Select(g => new
                           {
                               date = g.Key,
                               messages = g.OrderBy(x => x.CreatedAt)
                                           .Select(s =>
                                           {
                                               var msg = $"【{s.Timestamp:HH:mm:ss}】{s.Author}:";
                                               if (string.IsNullOrWhiteSpace(s.ToString()))
                                               {
                                                   if (s.Attachments.Any())
                                                   {
                                                       msg += "FILES_UPLOADED: "
                                                              + string.Join("\n", s.Attachments.Select(x => x.Url));
                                                   }
                                                   else if (s.Embeds.Any())
                                                   {
                                                       msg += "EMBEDS: "
                                                              + string.Join("\n--------\n",
                                                                  s.Embeds.Select(x
                                                                      => $"Description: {x.Description}"));
                                                   }
                                               }
                                               else
                                                   msg += s.ToString();

                                               return msg;
                                           })
                           });
        await using var stream = await JsonConvert.SerializeObject(grouping, Formatting.Indented).ToStream();
        await ctx.User.SendFileAsync(stream, title, title);
    }

    [Cmd]
    [Ratelimit(3)]
    public async Task Ping()
    {
        var sw = Stopwatch.StartNew();
        var msg = await Response().Text("🏓").SendAsync();
        sw.Stop();
        msg.DeleteAfter(0);

        await Response()
              .Confirm($"{Format.Bold(ctx.User.ToString())} 🏓 {(int)sw.Elapsed.TotalMilliseconds}ms")
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.ManageMessages)]
    public async Task VerboseError(bool? newstate = null)
    {
        var state = await _veService.ToggleVerboseErrors(ctx.Guild.Id, newstate);

        if (state)
            await Response().Confirm(strs.verbose_errors_enabled).SendAsync();
        else
            await Response().Confirm(strs.verbose_errors_disabled).SendAsync();
    }

    [Cmd]
    public async Task Afk([Leftover] string text = "No reason specified.")
    {
        var succ = await _afkService.SetAfkAsync(ctx.User.Id, text);

        if (succ)
        {
            await Response()
                  .Confirm(strs.afk_set)
                  .SendAsync();
        }
    }

    [Cmd]
    [NoPublicBot]
    [OwnerOnly]
    public async Task Eval([Leftover] string scriptText)
    {
        _ = ctx.Channel.TriggerTypingAsync();

        if (scriptText.StartsWith("```cs"))
            scriptText = scriptText[5..];
        else if (scriptText.StartsWith("```"))
            scriptText = scriptText[3..];

        if (scriptText.EndsWith("```"))
            scriptText = scriptText[..^3];

        var script = CSharpScript.Create(scriptText,
            ScriptOptions.Default
                         .WithReferences(this.GetType().Assembly)
                         .WithImports(
                             "System",
                             "System.Collections.Generic",
                             "System.IO",
                             "System.Linq",
                             "System.Net.Http",
                             "System.Threading",
                             "System.Threading.Tasks",
                             "NadekoBot",
                             "NadekoBot.Extensions",
                             "Microsoft.Extensions.DependencyInjection",
                             "NadekoBot.Common",
                             "NadekoBot.Modules",
                             "System.Text",
                             "System.Text.Json"),
            globalsType: typeof(EvalGlobals));

        try
        {
            var result = await script.RunAsync(new EvalGlobals()
            {
                ctx = this.ctx,
                guild = this.ctx.Guild,
                channel = this.ctx.Channel,
                user = this.ctx.User,
                self = this,
                services = _services
            });

            var output = result.ReturnValue?.ToString();
            if (!string.IsNullOrWhiteSpace(output))
            {
                var eb = CreateEmbed()
                                .WithOkColor()
                                .AddField("Code", scriptText)
                                .AddField("Output", output.TrimTo(512)!);

                _ = Response().Embed(eb).SendAsync();
            }
        }
        catch (Exception ex)
        {
            await Response().Error(ex.Message).SendAsync();
        }
    }

    [Cmd]
    public async Task Snipe()
    {
        if (ctx.Message.ReferencedMessage is not { } msg)
        {
            var msgs = await ctx.Channel.GetMessagesAsync(ctx.Message, Direction.Before, 3).FlattenAsync();
            msg = msgs.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Content) || (x.Attachments.FirstOrDefault()?.Width is not null)) as IUserMessage;

            if (msg is null)
                return;
        }

        var eb = CreateEmbed()
                        .WithOkColor()
                        .WithDescription(msg.Content)
                        .WithAuthor(msg.Author)
                        .WithTimestamp(msg.Timestamp)
                        .WithImageUrl(msg.Attachments.FirstOrDefault()?.Url)
                        .WithFooter(GetText(strs.sniped_by(ctx.User.ToString())), ctx.User.GetDisplayAvatarUrl());

        ctx.Message.DeleteAfter(1);
        await Response().Embed(eb).SendAsync();
    }
}