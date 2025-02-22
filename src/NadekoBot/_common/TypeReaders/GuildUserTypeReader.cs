namespace NadekoBot.Common.TypeReaders;

public sealed class GuildUserTypeReader : NadekoTypeReader<IGuildUser>
{
    public override async ValueTask<TypeReaderResult<IGuildUser>> ReadAsync(ICommandContext ctx, string input)
    {
        if (ctx.Guild is null)
            return TypeReaderResult.FromError<IGuildUser>(CommandError.Unsuccessful, "Must be in a guild.");

        input = input.Trim();
        IGuildUser? user = null;
        if (MentionUtils.TryParseUser(input, out var id))
            user = await ctx.Guild.GetUserAsync(id, CacheMode.AllowDownload);
        
        if (ulong.TryParse(input, out id))
            user = await ctx.Guild.GetUserAsync(id, CacheMode.AllowDownload);

        if (user is null)
        {
            var users = await ctx.Guild.GetUsersAsync(CacheMode.CacheOnly);
            user = users.FirstOrDefault(x => x.Username == input)
                   ?? users.FirstOrDefault(x =>
                       string.Equals(x.ToString(), input, StringComparison.InvariantCultureIgnoreCase))
                   ?? users.FirstOrDefault(x =>
                       string.Equals(x.Username, input, StringComparison.InvariantCultureIgnoreCase));
        }

        if (user is null)
            return TypeReaderResult.FromError<IGuildUser>(CommandError.ObjectNotFound, "User not found.");
        
        return TypeReaderResult.FromSuccess(user);
    }
}