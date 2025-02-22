#nullable disable
namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    public partial class ConfigCommands : NadekoModule
    {
        private readonly IEnumerable<IConfigService> _settingServices;

        public ConfigCommands(IEnumerable<IConfigService> settingServices)
            => _settingServices = settingServices.Where(x => x.Name != "medusa");

        [Cmd]
        [OwnerOnly]
        public async Task ConfigReload(string name)
        {
            var setting = _settingServices.FirstOrDefault(x
                => x.Name.StartsWith(name, StringComparison.InvariantCultureIgnoreCase));

            if (setting is null)
            {
                var configNames = _settingServices.Select(x => x.Name);
                var embed = CreateEmbed()
                               .WithErrorColor()
                               .WithDescription(GetText(strs.config_not_found(Format.Code(name))))
                               .AddField(GetText(strs.config_list), string.Join("\n", configNames));

                await Response().Embed(embed).SendAsync();
                return;
            }

            setting.Reload();
            await ctx.OkAsync();
        }

        [Cmd]
        [OwnerOnly]
        public async Task Config(string name = null, string prop = null, [Leftover] string value = null)
        {
            var configNames = _settingServices.Select(x => x.Name);

            // if name is not provided, print available configs
            name = name?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name))
            {
                var embed = CreateEmbed()
                               .WithOkColor()
                               .WithTitle(GetText(strs.config_list))
                               .WithDescription(string.Join("\n", configNames));

                await Response().Embed(embed).SendAsync();
                return;
            }

            var setting = _settingServices.FirstOrDefault(x
                => x.Name.StartsWith(name, StringComparison.InvariantCultureIgnoreCase));

            // if config name is not found, print error and the list of configs
            if (setting is null)
            {
                var embed = CreateEmbed()
                               .WithErrorColor()
                               .WithDescription(GetText(strs.config_not_found(Format.Code(name))))
                               .AddField(GetText(strs.config_list), string.Join("\n", configNames));

                await Response().Embed(embed).SendAsync();
                return;
            }

            name = setting.Name;

            // if prop is not sent, then print the list of all props and values in that config
            prop = prop?.ToLowerInvariant();
            var propNames = setting.GetSettableProps();
            if (string.IsNullOrWhiteSpace(prop))
            {
                var propStrings = GetPropsAndValuesString(setting, propNames);
                var embed = CreateEmbed().WithOkColor().WithTitle($"⚙️ {setting.Name}").WithDescription(propStrings);


                await Response().Embed(embed).SendAsync();
                return;
            }
            // if the prop is invalid -> print error and list of 

            var exists = propNames.Any(x => x == prop);

            if (!exists)
            {
                var propStrings = GetPropsAndValuesString(setting, propNames);
                var propErrorEmbed = CreateEmbed()
                                        .WithErrorColor()
                                        .WithDescription(GetText(
                                            strs.config_prop_not_found(Format.Code(prop), Format.Code(name))))
                                        .AddField($"⚙️ {setting.Name}", propStrings);

                await Response().Embed(propErrorEmbed).SendAsync();
                return;
            }

            // if prop is sent, but value is not, then we have to check
            // if prop is valid -> 
            if (string.IsNullOrWhiteSpace(value))
            {
                value = setting.GetSetting(prop);

                if (string.IsNullOrWhiteSpace(value))
                    value = "-";

                if (prop != "currency.sign")
                    value = Format.Code(Format.Sanitize(value.TrimTo(1000)), "json");

                var embed = CreateEmbed()
                               .WithOkColor()
                               .AddField("Config", Format.Code(setting.Name), true)
                               .AddField("Prop", Format.Code(prop), true)
                               .AddField("Value", value);

                var comment = setting.GetComment(prop);
                if (!string.IsNullOrWhiteSpace(comment))
                    embed.AddField("Comment", comment);

                await Response().Embed(embed).SendAsync();
                return;
            }

            var success = setting.SetSetting(prop, value);

            if (!success)
            {
                await Response().Error(strs.config_edit_fail(Format.Code(prop), Format.Code(value))).SendAsync();
                return;
            }

            await ctx.OkAsync();
        }

        private string GetPropsAndValuesString(IConfigService config, IReadOnlyCollection<string> names)
        {
            var propValues = names.Select(pr =>
                                  {
                                      var val = config.GetSetting(pr);
                                      if (pr != "currency.sign")
                                          val = val?.TrimTo(28);
                                      return val?.Replace("\n", "") ?? "-";
                                  })
                                  .ToList();

            var strings = names.Zip(propValues, (name, value) => $"{name,-25} = {value}\n");

            return Format.Code(string.Concat(strings), "hs");
        }
    }
}