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
            var units = await _service.GetUnitsAsync();
            var originUnit = units.FirstOrDefault(x
                => x.Triggers.Select(y => y.ToUpperInvariant()).Contains(origin.ToUpperInvariant()));
            var targetUnit = units.FirstOrDefault(x
                => x.Triggers.Select(y => y.ToUpperInvariant()).Contains(target.ToUpperInvariant()));
            if (originUnit is null || targetUnit is null)
            {
                await Response().Error(strs.convert_not_found(Format.Bold(origin), Format.Bold(target))).SendAsync();
                return;
            }

            if (originUnit.UnitType != targetUnit.UnitType)
            {
                await Response()
                      .Error(strs.convert_type_error(Format.Bold(originUnit.Triggers.First()),
                          Format.Bold(targetUnit.Triggers.First())))
                      .SendAsync();
                return;
            }

            decimal res;
            if (originUnit.Triggers == targetUnit.Triggers)
                res = value;
            else if (originUnit.UnitType == "temperature")
            {
                //don't really care too much about efficiency, so just convert to Kelvin, then to target
                switch (originUnit.Triggers.First().ToUpperInvariant())
                {
                    case "C":
                        res = value + 273.15m; //celcius!
                        break;
                    case "F":
                        res = (value + 459.67m) * (5m / 9m);
                        break;
                    default:
                        res = value;
                        break;
                }

                //from Kelvin to target
                switch (targetUnit.Triggers.First().ToUpperInvariant())
                {
                    case "C":
                        res -= 273.15m; //celcius!
                        break;
                    case "F":
                        res = (res * (9m / 5m)) - 459.67m;
                        break;
                }
            }
            else
            {
                if (originUnit.UnitType == "currency")
                    res = value * targetUnit.Modifier / originUnit.Modifier;
                else
                    res = value * originUnit.Modifier / targetUnit.Modifier;
            }

            res = Math.Round(res, 4);

            await Response()
                  .Confirm(strs.convert(value,
                      originUnit.Triggers.Last(),
                      res,
                      targetUnit.Triggers.Last()))
                  .SendAsync();
        }
    }
}