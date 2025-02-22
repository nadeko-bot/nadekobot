using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace NadekoBot.Db;

public sealed class SqliteContext : NadekoContext
{
    private readonly string _connectionString;

    protected override string CurrencyTransactionOtherIdDefaultValue
        => "NULL";

    public SqliteContext(string connectionString = "Data Source=data/NadekoBot.db", int commandTimeout = 60)
    {
        _connectionString = connectionString;
        Database.SetCommandTimeout(commandTimeout);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        builder.DataSource = Path.Combine(AppContext.BaseDirectory, builder.DataSource);
        optionsBuilder.UseSqlite(builder.ToString());
    }
}