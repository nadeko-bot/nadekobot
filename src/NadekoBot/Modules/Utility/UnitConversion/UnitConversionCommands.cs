#nullable disable
using NadekoBot.Modules.Utility.Services;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class UnitConverterCommands : NadekoModule<ConverterService>
    {
        [Cmd]
        public async Task ConvertList()
        {
            var units = await _service.GetUnitsAsync();

            var embed = CreateEmbed().WithTitle(GetText(strs.convertlist)).WithOkColor();


            foreach (var g in units.GroupBy(x => x.UnitType))
            {
                embed.AddField(g.Key.ToTitleCase(),
                    string.Join(", ", g.Select(x => x.Triggers.FirstOrDefault()).OrderBy(x => x)));
            }

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        [Priority(0)]
        public async Task Convert(string origin, string target, decimal value)
        {
            var result = await _service.ConvertAsync(origin, target, value);
            switch (result.Status)
            {
                case ConvertStatus.NotFound:
                    await Response().Error(strs.convert_not_found(Format.Bold(origin), Format.Bold(target))).SendAsync();
                    return;
                case ConvertStatus.TypeMismatch:
                    await Response()
                          .Error(strs.convert_type_error(Format.Bold(result.From.Triggers[0]),
                              Format.Bold(result.To.Triggers[0])))
                          .SendAsync();
                    return;
                case ConvertStatus.Overflow:
                    await Response().Error(strs.convert_overflow).SendAsync();
                    return;
            }

            await Response()
                  .Confirm(strs.convert(value,
                      result.From.Triggers[^1],
                      result.Value,
                      result.To.Triggers[^1]))
                  .SendAsync();
        }
    }
}