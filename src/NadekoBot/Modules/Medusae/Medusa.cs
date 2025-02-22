using Nadeko.Common.Medusa;

namespace NadekoBot.Modules;

[OwnerOnly]
[NoPublicBot]
public partial class Medusa : NadekoModule<IMedusaLoaderService>
{
    private readonly IMedusaeRepositoryService _repo;

    public Medusa(IMedusaeRepositoryService repo)
    {
        _repo = repo;
    }

    [Cmd]
    [OwnerOnly]
    public async Task MedusaLoad(string? name = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            var loaded = _service.GetLoadedMedusae()
                                 .Select(x => x.Name)
                                 .ToHashSet();

            var unloaded = _service.GetAllMedusae()
                                   .Where(x => !loaded.Contains(x))
                                   .Select(x => Format.Code(x.ToString()))
                                   .ToArray();

            if (unloaded.Length == 0)
            {
                await Response().Pending(strs.no_medusa_available).SendAsync();
                return;
            }

            await Response()
                  .Paginated()
                  .Items(unloaded)
                  .PageSize(10)
                  .Page((items, _) =>
                  {
                      return CreateEmbed()
                             .WithOkColor()
                             .WithTitle(GetText(strs.list_of_unloaded))
                             .WithDescription(items.Join('\n'));
                  })
                  .SendAsync();
            return;
        }

        var res = await _service.LoadMedusaAsync(name);
        if (res == MedusaLoadResult.Success)
            await Response().Confirm(strs.medusa_loaded(Format.Code(name))).SendAsync();
        else
        {
            var locStr = res switch
            {
                MedusaLoadResult.Empty => strs.medusa_empty,
                MedusaLoadResult.AlreadyLoaded => strs.medusa_already_loaded(Format.Code(name)),
                MedusaLoadResult.NotFound => strs.medusa_invalid_not_found,
                MedusaLoadResult.UnknownError => strs.error_occured,
                _ => strs.error_occured
            };

            await Response().Error(locStr).SendAsync();
        }
    }

    [Cmd]
    [OwnerOnly]
    public async Task MedusaUnload(string? name = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            var loaded = _service.GetLoadedMedusae();
            if (loaded.Count == 0)
            {
                await Response().Pending(strs.no_medusa_loaded).SendAsync();
                return;
            }

            await Response()
                  .Embed(CreateEmbed()
                         .WithOkColor()
                         .WithTitle(GetText(strs.loaded_medusae))
                         .WithDescription(loaded.Select(x => x.Name)
                                                .Join("\n")))
                  .SendAsync();

            return;
        }

        var res = await _service.UnloadMedusaAsync(name);
        if (res == MedusaUnloadResult.Success)
            await Response().Confirm(strs.medusa_unloaded(Format.Code(name))).SendAsync();
        else
        {
            var locStr = res switch
            {
                MedusaUnloadResult.NotLoaded => strs.medusa_not_loaded,
                MedusaUnloadResult.PossiblyUnable => strs.medusa_possibly_cant_unload,
                _ => strs.error_occured
            };

            await Response().Error(locStr).SendAsync();
        }
    }

    [Cmd]
    [OwnerOnly]
    public async Task MedusaList()
    {
        var all = _service.GetAllMedusae();

        if (all.Count == 0)
        {
            await Response().Pending(strs.no_medusa_available).SendAsync();
            return;
        }

        var loaded = _service.GetLoadedMedusae()
                             .Select(x => x.Name)
                             .ToHashSet();

        var output = all
                     .Select(m =>
                     {
                         var emoji = loaded.Contains(m) ? "`✅`" : "`🔴`";
                         return $"{emoji} `{m}`";
                     })
                     .ToArray();


        await Response()
              .Paginated()
              .Items(output)
              .PageSize(10)
              .Page((items, _) => CreateEmbed()
                                  .WithOkColor()
                                  .WithTitle(GetText(strs.list_of_medusae))
                                  .WithDescription(items.Join('\n')))
              .SendAsync();
    }

    [Cmd]
    [OwnerOnly]
    public async Task MedusaInfo(string? name = null)
    {
        var medusae = _service.GetLoadedMedusae();

        if (name is not null)
        {
            var found = medusae.FirstOrDefault(x => string.Equals(x.Name,
                name,
                StringComparison.InvariantCultureIgnoreCase));

            if (found is null)
            {
                await Response().Error(strs.medusa_name_not_found).SendAsync();
                return;
            }

            var cmdCount = found.Sneks.Sum(x => x.Commands.Count);
            var cmdNames = found.Sneks
                                .SelectMany(x => Format.Code(string.IsNullOrWhiteSpace(x.Prefix)
                                    ? x.Name
                                    : $"{x.Prefix} {x.Name}"))
                                .Join("\n");

            var eb = CreateEmbed()
                     .WithOkColor()
                     .WithAuthor(GetText(strs.medusa_info))
                     .WithTitle(found.Name)
                     .WithDescription(found.Description)
                     .AddField(GetText(strs.sneks_count(found.Sneks.Count)),
                         found.Sneks.Count == 0
                             ? "-"
                             : found.Sneks.Select(x => x.Name).Join('\n'),
                         true)
                     .AddField(GetText(strs.commands_count(cmdCount)),
                         string.IsNullOrWhiteSpace(cmdNames)
                             ? "-"
                             : cmdNames,
                         true);

            await Response().Embed(eb).SendAsync();
            return;
        }

        if (medusae.Count == 0)
        {
            await Response().Pending(strs.no_medusa_loaded).SendAsync();
            return;
        }

        await Response()
              .Paginated()
              .Items(medusae)
              .PageSize(9)
              .CurrentPage(0)
              .Page((items, _) =>
              {
                  var eb = CreateEmbed()
                      .WithOkColor();

                  foreach (var medusa in items)
                  {
                      eb.AddField(medusa.Name,
                          $"""
                           `Sneks:` {medusa.Sneks.Count}
                           `Commands:` {medusa.Sneks.Sum(x => x.Commands.Count)}
                           --
                           {medusa.Description}
                           """);
                  }

                  return eb;
              })
              .SendAsync();
    }

    [Cmd]
    [OwnerOnly]
    public async Task MedusaSearch()
    {
        var eb = CreateEmbed()
                 .WithTitle(GetText(strs.list_of_medusae))
                 .WithOkColor();

        foreach (var item in await _repo.GetModuleItemsAsync())
        {
            eb.AddField(item.Name,
                $"""
                 {item.Description}
                 `{item.Command}`
                 """,
                true);
        }

        await Response().Embed(eb).SendAsync();
    }
}