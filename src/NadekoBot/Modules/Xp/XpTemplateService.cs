#nullable disable warnings
using NadekoBot.Common.ModuleBehaviors;
using Newtonsoft.Json;

namespace NadekoBot.Modules.Xp.Services;

public sealed class XpTemplateService : INService, IReadyExecutor
{
    private const string XP_TEMPLATE_PATH = "./data/xp_template.json";
    private const string BACKUP_XP_TEMPLATE_PATH = "./data/OLD_xp_template.json";

    private readonly IPubSub _pubSub;
    private XpTemplate _template = new();
    private readonly TypedKey<bool> _xpTemplateReloadKey = new("xp.template.reload");

    public XpTemplateService(IPubSub pubSub)
    {
        _pubSub = pubSub;
    }

    private void InternalReloadXpTemplate()
    {
        try
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new RequireObjectPropertiesContractResolver()
            };

            if (!File.Exists(XP_TEMPLATE_PATH))
            {
                var newTemp = new XpTemplate();
                newTemp.Version = 3;
                File.WriteAllText(XP_TEMPLATE_PATH, JsonConvert.SerializeObject(newTemp, Formatting.Indented));
            }

            _template = JsonConvert.DeserializeObject<XpTemplate>(
                File.ReadAllText(XP_TEMPLATE_PATH),
                settings)!;

            if (_template.Version < 3)
            {
                if (File.Exists(XP_TEMPLATE_PATH))
                    File.Move(XP_TEMPLATE_PATH, BACKUP_XP_TEMPLATE_PATH);

                _template = new();
                File.WriteAllText(XP_TEMPLATE_PATH, JsonConvert.SerializeObject(_template, Formatting.Indented));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "xp_template.json is invalid. Loaded default values");
            _template = new();
        }
    }

    public void ReloadXpTemplate()
        => _pubSub.Pub(_xpTemplateReloadKey, true);

    public async Task OnReadyAsync()
    {
        InternalReloadXpTemplate();
        await _pubSub.Sub(_xpTemplateReloadKey,
            _ =>
            {
                InternalReloadXpTemplate();
                return default;
            });
    }

    public XpTemplate GetTemplate()
        => _template;
}