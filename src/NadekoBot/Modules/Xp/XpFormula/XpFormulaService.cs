using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Xp;

public sealed class XpFormulaService(DbService db, ShardData shardData) : IReadyExecutor, INService
{
    public const int MIN_A = 1;
    public const int MAX_A = 100;
    public const int MIN_C = 0;
    public const int MAX_C = 500;

    private static readonly XpFormula _default = new(9, 27);
    private ConcurrentDictionary<ulong, XpFormula> _formulas = new();

    public async Task OnReadyAsync()
    {
        await using var ctx = db.GetDbContext();
        _formulas = await ctx.GetTable<XpSettings>()
            .AsNoTracking()
            .Where(Queries.GuildOnShard<XpSettings>(x => x.GuildId, shardData.TotalShards, shardData.ShardId))
            .Where(x => x.XpFormulaA != _default.A || x.XpFormulaC != _default.C)
            .Select(x => new { x.GuildId, x.XpFormulaA, x.XpFormulaC })
            .ToListAsyncLinqToDB()
            .Pipe(list => list
                .ToDictionary(
                    x => x.GuildId,
                    x => new XpFormula(x.XpFormulaA, x.XpFormulaC))
                .ToConcurrent());
    }

    public XpFormula GetFormula(ulong guildId)
        => _formulas.TryGetValue(guildId, out var f) ? f : _default;

    public async Task<bool> SetFormulaAsync(ulong guildId, int a, int c)
    {
        if (a is < MIN_A or > MAX_A || c is < MIN_C or > MAX_C)
            return false;

        await using var ctx = db.GetDbContext();
        await ctx.GetTable<XpSettings>()
            .InsertOrUpdateAsync(
                () => new XpSettings
                {
                    GuildId = guildId,
                    XpFormulaA = a,
                    XpFormulaC = c,
                },
                old => new XpSettings
                {
                    XpFormulaA = a,
                    XpFormulaC = c,
                },
                () => new XpSettings
                {
                    GuildId = guildId,
                });

        var formula = new XpFormula(a, c);
        if (formula == _default)
            _formulas.TryRemove(guildId, out _);
        else
            _formulas[guildId] = formula;

        return true;
    }
}
