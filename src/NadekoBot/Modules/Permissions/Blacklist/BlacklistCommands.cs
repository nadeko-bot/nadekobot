#nullable disable
using NadekoBot.Modules.Permissions.Services;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Permissions;

public partial class Permissions
{
    [Group]
    public partial class BlacklistCommands : NadekoModule<BlacklistService>
    {
        private readonly DiscordSocketClient _client;

        public BlacklistCommands(DiscordSocketClient client)
            => _client = client;

        private async Task ListBlacklistInternal(string title, BlacklistType type, int page = 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(page);

            var list = await _service.GetBlacklist(type);
            var allItems = await list
                                 .Select(i =>
                                 {
                                     try
                                     {
                                         return Task.FromResult(type switch
                                         {
                                             BlacklistType.Channel => Format.Code(i.ItemId.ToString())
                                                                      + " "
                                                                      + (_client.GetChannel(i.ItemId)?.ToString()
                                                                         ?? ""),
                                             BlacklistType.User => Format.Code(i.ItemId.ToString())
                                                                   + " "
                                                                   + ((_client.GetUser(i.ItemId))
                                                                      ?.ToString()
                                                                      ?? ""),
                                             BlacklistType.Server => Format.Code(i.ItemId.ToString())
                                                                     + " "
                                                                     + (_client.GetGuild(i.ItemId)?.ToString() ?? ""),
                                             _ => Format.Code(i.ItemId.ToString())
                                         });
                                     }
                                     catch
                                     {
                                         Log.Warning("Can't get {BlacklistType} [{BlacklistItemId}]",
                                             i.Type,
                                             i.ItemId);

                                         return Task.FromResult(Format.Code(i.ItemId.ToString()));
                                     }
                                 })
                                 .WhenAll();

            await Response()
                  .Paginated()
                  .Items(allItems)
                  .PageSize(10)
                  .CurrentPage(page)
                  .Page((pageItems, _) =>
                  {
                      if (pageItems.Count == 0)
                          return CreateEmbed()
                                        .WithOkColor()
                                        .WithTitle(title)
                                        .WithDescription(GetText(strs.empty_page));

                      return CreateEmbed()
                                    .WithTitle(title)
                                    .WithDescription(pageItems.Join('\n'))
                                    .WithOkColor();
                  })
                  .SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        public Task UserBlacklist(int page = 1)
        {
            if (--page < 0)
                return Task.CompletedTask;

            return ListBlacklistInternal(GetText(strs.blacklisted_users), BlacklistType.User, page);
        }

        [Cmd]
        [OwnerOnly]
        public Task ChannelBlacklist(int page = 1)
        {
            if (--page < 0)
                return Task.CompletedTask;

            return ListBlacklistInternal(GetText(strs.blacklisted_channels), BlacklistType.Channel, page);
        }

        [Cmd]
        [OwnerOnly]
        public Task ServerBlacklist(int page = 1)
        {
            if (--page < 0)
                return Task.CompletedTask;

            return ListBlacklistInternal(GetText(strs.blacklisted_servers), BlacklistType.Server, page);
        }

        [Cmd]
        [OwnerOnly]
        public Task UserBlacklist(AddRemove action, ulong id)
            => Blacklist(action, id, BlacklistType.User);

        [Cmd]
        [OwnerOnly]
        public Task UserBlacklist(AddRemove action, IUser usr)
            => Blacklist(action, usr.Id, BlacklistType.User);

        [Cmd]
        [OwnerOnly]
        public Task ChannelBlacklist(AddRemove action, ulong id)
            => Blacklist(action, id, BlacklistType.Channel);

        [Cmd]
        [OwnerOnly]
        public Task ServerBlacklist(AddRemove action, ulong id)
            => Blacklist(action, id, BlacklistType.Server);

        [Cmd]
        [OwnerOnly]
        public Task ServerBlacklist(AddRemove action, IGuild guild)
            => Blacklist(action, guild.Id, BlacklistType.Server);

        private async Task Blacklist(AddRemove action, ulong id, BlacklistType type)
        {
            if (action == AddRemove.Add)
                await _service.Blacklist(type, id);
            else
                await _service.UnBlacklist(type, id);

            if (action == AddRemove.Add)
            {
                await Response()
                      .Confirm(strs.blacklisted(Format.Code(type.ToString()),
                          Format.Code(id.ToString())))
                      .SendAsync();
            }
            else
            {
                await Response()
                      .Confirm(strs.unblacklisted(Format.Code(type.ToString()),
                          Format.Code(id.ToString())))
                      .SendAsync();
            }
        }
    }
}