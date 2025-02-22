using Microsoft.EntityFrameworkCore;

namespace NadekoBot.Db;

public sealed class PostgreSqlContext : NadekoContext
{
    private readonly string _connStr;

    protected override string CurrencyTransactionOtherIdDefaultValue
        => "NULL";

    public PostgreSqlContext(string connStr = "Host=localhost")
    {
        _connStr = connStr;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        
        base.OnConfiguring(optionsBuilder);
        optionsBuilder
            .UseLowerCaseNamingConvention()
            .UseNpgsql(_connStr);
    }
}