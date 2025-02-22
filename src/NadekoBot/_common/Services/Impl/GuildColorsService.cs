using AngleSharp.Common;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.Services.Impl;

public sealed class GuildColorsService : IReadyExecutor, IGuildColorsService, INService
{
    private readonly DbService _db;
    private readonly ConcurrentDictionary<ulong, Colors> _colors = new();
    private readonly DiscordSocketClient _client;
    private readonly IBotCreds _creds;

    public GuildColorsService(DbService db, DiscordSocketClient client, IBotCreds creds)
    {
        _db = db;
        _client = client;
        _creds = creds;
    }

    public Colors? GetColors(ulong guildId)
    {
        if (_colors.TryGetValue(guildId, out var color))
            return color;

        return null;
    }

    public async Task SetOkColor(ulong guildId, Rgba32? color)
    {
        var toInsert = color?.ToHex();
        await _db.GetDbContext()
                 .GetTable<GuildColors>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         OkColor = toInsert
                     },
                     old => new()
                     {
                         OkColor = toInsert
                     },
                     () => new()
                     {
                         GuildId = guildId
                     });

        if (!_colors.TryAdd(guildId, new Colors(color?.ToDiscordColor(), null, null)))
        {
            _colors[guildId] = _colors[guildId] with
            {
                Ok = color?.ToDiscordColor()
            };
        }
    }

    public async Task SetErrorColor(ulong guildId, Rgba32? color)
    {
        var toInsert = color?.ToHex();
        await _db.GetDbContext()
                 .GetTable<GuildColors>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         ErrorColor = toInsert
                     },
                     old => new()
                     {
                         ErrorColor = toInsert
                     },
                     () => new()
                     {
                         GuildId = guildId
                     });
        
        if (!_colors.TryAdd(guildId, new Colors(null, null, color?.ToDiscordColor())))
        {
            _colors[guildId] = _colors[guildId] with
            {
                Error = color?.ToDiscordColor()
            };
        }
    }

    public async Task SetPendingColor(ulong guildId, Rgba32? color)
    {
        var toInsert = color?.ToHex();
        await _db.GetDbContext()
                 .GetTable<GuildColors>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         PendingColor = toInsert
                     },
                     old => new()
                     {
                         PendingColor = toInsert
                     },
                     () => new()
                     {
                         GuildId = guildId
                     });
        
        if (!_colors.TryAdd(guildId, new Colors(null, color?.ToDiscordColor(), null)))
        {
            _colors[guildId] = _colors[guildId] with
            {
                Warn = color?.ToDiscordColor()
            };
        }
    }

    public async Task OnReadyAsync()
    {
        await using var ctx = _db.GetDbContext();
        var guildColors = await ctx.GetTable<GuildColors>()
                                   .Where(x => Queries.GuildOnShard(x.GuildId,
                                       _creds.TotalShards,
                                       _client.ShardId))
                                   .ToListAsync();

        foreach (var color in guildColors)
        {
            var colors = new Colors(
                ConvertColor(color.OkColor),
                ConvertColor(color.PendingColor),
                ConvertColor(color.ErrorColor));

            _colors.TryAdd(color.GuildId, colors);
        }
    }

    private Color? ConvertColor(string? colorErrorColor)
    {
        if (string.IsNullOrWhiteSpace(colorErrorColor))
            return null;

        if (!Rgba32.TryParseHex(colorErrorColor, out var clr))
            return null;

        return clr.ToDiscordColor();
    }
}