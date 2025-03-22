#nullable disable
using System.Globalization;
using System.Text;
using Nadeko.Common.Medusa;
using NadekoBot.Common.ModuleBehaviors;
using Newtonsoft.Json;

namespace NadekoBot.Modules.Help;

public sealed partial class CommandListGenerator(
    CommandService cmds,
    IMedusaLoaderService medusae,
    IBotStrings strings
) : INService, IReadyExecutor
{
    public async Task OnReadyAsync()
    {
        await Task.Delay(10_000);

#if DEBUG
        await GenerateCommandListAsync(".", CultureInfo.InvariantCulture);
#endif
    }

    public async Task<Stream> GenerateCommandListAsync(string prefix, CultureInfo culture)
    {
        // order commands by top level module name
        // and make a dictionary of <ModuleName, Array<JsonCommandData>>
        var cmdData = cmds.Commands.GroupBy(x => x.Module.GetTopLevelModule().Name)
            .OrderBy(x => x.Key)
            .ToDictionary(x => x.Key,
                x => x.DistinctBy(c => c.Aliases.First())
                    .Select(com =>
                    {
                        List<string> optHelpStr = null;

                        var opt = CommandsUtilityService.GetNadekoOptionType(com.Attributes);
                        if (opt is not null)
                            optHelpStr = CommandsUtilityService.GetCommandOptionHelpList(opt);

                        return new CommandJsonObject
                        {
                            Aliases = com.Aliases.Select(alias => prefix + alias).ToArray(),
                            Description = com.RealSummary(strings, medusae, culture, prefix),
                            Usage = com.RealRemarksArr(strings, medusae, culture, prefix),
                            Submodule = com.Module.Name,
                            Module = com.Module.GetTopLevelModule().Name,
                            Options = optHelpStr,
                            Requirements = CommandsUtilityService.GetCommandRequirements(com)
                        };
                    })
                    .ToList());

        var readableData = JsonConvert.SerializeObject(cmdData, Formatting.Indented);

        // send the indented file to chat
        var rDataStream = new MemoryStream(Encoding.ASCII.GetBytes(readableData));
        await File.WriteAllTextAsync("data/commandlist.json", readableData);

        return rDataStream;
    }
}