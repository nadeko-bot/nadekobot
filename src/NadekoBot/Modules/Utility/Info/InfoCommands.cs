#nullable disable
using System.Text;
using NadekoBot.Modules.Patronage;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class InfoCommands : NadekoModule
    {
        private readonly DiscordSocketClient _client;
        private readonly IStatsService _stats;
        private readonly IPatronageService _ps;

        public InfoCommands(DiscordSocketClient client, IStatsService stats, IPatronageService ps)
        {
            _client = client;
            _stats = stats;
            _ps = ps;
        }

        [Cmd]
        [OwnerOnly]
        public Task ServerInfo(ulong guildId)
            => InternalServerInfo(guildId);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public Task ServerInfo()
            => InternalServerInfo(ctx.Guild.Id);

        private async Task InternalServerInfo(ulong guildId)
        {
            var guild = (IGuild)_client.GetGuild(guildId)
                        ?? await _client.Rest.GetGuildAsync(guildId);
            
            if (guild is null)
                return;

            var ownername = await guild.GetUserAsync(guild.OwnerId);
            var textchn = (await guild.GetTextChannelsAsync()).Count;
            var voicechn = (await guild.GetVoiceChannelsAsync()).Count;
            var channels = $@"{GetText(strs.text_channels(textchn))}
{GetText(strs.voice_channels(voicechn))}";
            var createdAt = new DateTime(2015, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(guild.Id >> 22);
            var features = guild.Features.Value.ToString();
            if (string.IsNullOrWhiteSpace(features))
                features = "-";

            var embed = CreateEmbed()
                               .WithAuthor(GetText(strs.server_info))
                               .WithTitle(guild.Name)
                               .AddField(GetText(strs.id), guild.Id.ToString(), true)
                               .AddField(GetText(strs.owner), ownername.ToString(), true)
                               .AddField(GetText(strs.members), (guild as SocketGuild)?.MemberCount.ToString() ?? guild.ApproximateMemberCount?.ToString() ?? "?", true)
                               .AddField(GetText(strs.channels), channels, true)
                               .AddField(GetText(strs.created_at), $"{createdAt:dd.MM.yyyy HH:mm}", true)
                               .AddField(GetText(strs.roles), (guild.Roles.Count - 1).ToString(), true)
                               .AddField(GetText(strs.features), features)
                               .WithOkColor();

            if (Uri.IsWellFormedUriString(guild.IconUrl, UriKind.Absolute))
                embed.WithThumbnailUrl(guild.IconUrl);

            if (guild.Emotes.Any())
            {
                embed.AddField(GetText(strs.custom_emojis) + $"({guild.Emotes.Count})",
                    string.Join(" ", guild.Emotes.Shuffle().Take(20).Select(e => $"{e.Name} {e}")).TrimTo(1020));
            }

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task ChannelInfo(ITextChannel channel = null)
        {
            var ch = channel ?? (ITextChannel)ctx.Channel;
            if (ch is null)
                return;
            var createdAt = new DateTime(2015, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ch.Id >> 22);
            var usercount = (await ch.GetUsersAsync().FlattenAsync()).Count();
            var embed = CreateEmbed()
                               .WithTitle(ch.Name)
                               .WithDescription(ch.Topic?.SanitizeMentions(true))
                               .AddField(GetText(strs.id), ch.Id.ToString(), true)
                               .AddField(GetText(strs.created_at), $"{createdAt:dd.MM.yyyy HH:mm}", true)
                               .AddField(GetText(strs.users), usercount.ToString(), true)
                               .WithOkColor();
            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        [RequireUserPermission(GuildPermission.ManageRoles)]
        public async Task RoleInfo([Leftover] SocketRole role)
        {
            if (role.IsEveryone)
                return;

            var createdAt = new DateTime(2015, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)
                .AddMilliseconds(role.Id >> 22);
            var usercount = role.Members.LongCount();
            var embed = CreateEmbed()
                               .WithTitle(role.Name.TrimTo(128))
                               .WithDescription(role.Permissions.ToList().Join(" | "))
                               .AddField(GetText(strs.id), role.Id.ToString(), true)
                               .AddField(GetText(strs.created_at), $"{createdAt:dd.MM.yyyy HH:mm}", true)
                               .AddField(GetText(strs.users), usercount.ToString(), true)
                               .AddField(GetText(strs.color),
                                   $"#{role.Color.R:X2}{role.Color.G:X2}{role.Color.B:X2}",
                                   true)
                               .AddField(GetText(strs.mentionable), role.IsMentionable.ToString(), true)
                               .AddField(GetText(strs.hoisted), role.IsHoisted.ToString(), true)
                               .WithOkColor();

            if (!string.IsNullOrWhiteSpace(role.GetIconUrl()))
                embed = embed.WithThumbnailUrl(role.GetIconUrl());

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task UserInfo(IGuildUser usr = null)
        {
            var user = usr ?? ctx.User as IGuildUser;

            if (user is null)
                return;

            var embed = CreateEmbed()
                               .AddField(GetText(strs.name), $"**{user.Username}**#{user.Discriminator}", true);
            if (!string.IsNullOrWhiteSpace(user.Nickname))
                embed.AddField(GetText(strs.nickname), user.Nickname, true);

            var joinedAt = GetJoinedAt(user);

            embed.AddField(GetText(strs.id), user.Id.ToString(), true)
                 .AddField(GetText(strs.joined_server), $"{joinedAt?.ToString("dd.MM.yyyy HH:mm") ?? "?"}", true)
                 .AddField(GetText(strs.joined_discord), $"{user.CreatedAt:dd.MM.yyyy HH:mm}", true)
                 .AddField(GetText(strs.roles),
                     $"**({user.RoleIds.Count - 1})** - {string.Join("\n", user.GetRoles().Take(10).Where(r => r.Id != r.Guild.EveryoneRole.Id).Select(r => r.Name)).SanitizeMentions(true)}",
                     true)
                 .WithOkColor();

            var mPatron = await _ps.GetPatronAsync(user.Id);

            if (mPatron is {} patron && patron.Tier != PatronTier.None)
            {
                embed.WithFooter(patron.Tier switch
                {
                    PatronTier.V => "❤️❤️",
                    PatronTier.X => "❤️❤️❤️",
                    PatronTier.XX => "❤️❤️❤️❤️",
                    PatronTier.L => "❤️❤️❤️❤️❤️",
                    _ => "❤️",
                });
            }

            var av = user.RealAvatarUrl();
            if (av.IsAbsoluteUri)
                embed.WithThumbnailUrl(av.ToString());

            await Response().Embed(embed).SendAsync();
        }

        private DateTimeOffset? GetJoinedAt(IGuildUser user)
        {
            var joinedAt = user.JoinedAt;
            if (user.GuildId != 117523346618318850)
                return joinedAt;

            if (user.Id == 351244576092192778)
                return new DateTimeOffset(2019, 12, 25, 9, 33, 0, TimeSpan.Zero);

            return joinedAt;
        }
    }
}