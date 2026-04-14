#nullable disable

namespace NadekoBot.Services;

public abstract class DbService
{
    public abstract Task SetupAsync();
    public abstract NadekoContext GetDbContext();
}