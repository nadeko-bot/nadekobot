#nullable disable
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class TimeZoneCommands : NadekoModule<GuildTimezoneService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Timezones(int page = 1)
        {
            page--;

            if (page is < 0 or > 20)
                return;

            var timezones = TimeZoneInfo.GetSystemTimeZones().OrderBy(x => x.BaseUtcOffset).ToArray();
            var timezonesPerPage = 20;

            var curTime = DateTimeOffset.UtcNow;

            var i = 0;
            var timezoneStrings = timezones.Select(x => (x, ++i % 2 == 0))
                                           .Select(data =>
                                           {
                                               var (tzInfo, flip) = data;
                                               var nameStr = $"{tzInfo.Id,-30}";
                                               var offset = curTime.ToOffset(tzInfo.GetUtcOffset(curTime))
                                                                   .ToString("zzz");
                                               if (flip)
                                                   return $"{offset} {Format.Code(nameStr)}";
                                               return $"{Format.Code(offset)} {nameStr}";
                                           })
                                           .ToList();


            await Response()
                  .Paginated()
                  .Items(timezoneStrings)
                  .PageSize(timezonesPerPage)
                  .CurrentPage(page)
                  .Page((items, _) => CreateEmbed()
                                      .WithOkColor()
                                      .WithTitle(GetText(strs.timezones_available))
                                      .WithDescription(string.Join("\n", items)))
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Timezone()
            => await Response().Confirm(strs.timezone_guild(_service.GetTimeZoneOrUtc(ctx.Guild.Id))).SendAsync();

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task Timezone([Leftover] string id)
        {
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { tz = null; }


            if (tz is null)
            {
                await Response().Error(strs.timezone_not_found).SendAsync();
                return;
            }

            _service.SetTimeZone(ctx.Guild.Id, tz);

            await Response().Confirm(tz.ToString()).SendAsync();
        }
    }
}