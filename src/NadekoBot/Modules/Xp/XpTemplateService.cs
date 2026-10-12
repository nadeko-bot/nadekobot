#nullable disable warnings
using NadekoBot.Common.ModuleBehaviors;
using Newtonsoft.Json;

namespace NadekoBot.Modules.Xp.Services;

public sealed class XpTemplateService : INService, IReadyExecutor
{
    private const string XP_TEMPLATE_PATH = "./data/xp_template.json";
    private const string BACKUP_XP_TEMPLATE_PATH = "./data/OLD_xp_template.json";
    private const int MAX_IMAGE_SIDE = 2048;
    private const int MAX_FONT_SIZE = 512;

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

            ClampInternal(_template);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "xp_template.json is invalid. Loaded default values");
            _template = new();
        }
    }

    // Each xp card allocates images of these sizes, so a template must not ask for huge ones.
    private static void ClampInternal(XpTemplate t)
    {
        ClampSizeInternal(t.OutputSize);
        ClampSizeInternal(t.User?.Icon?.Size);
        ClampSizeInternal(t.Club?.Icon?.Size);

        ClampFontInternal(t.User?.Name);
        ClampFontInternal(t.User?.Level);
        ClampFontInternal(t.User?.Rank);
        ClampFontInternal(t.User?.Xp?.Guild);
        ClampFontInternal(t.Club?.Name);
    }

    private static void ClampSizeInternal(XpTemplatePos? size)
    {
        if (size is null)
            return;

        size.X = Math.Clamp(size.X, 1, MAX_IMAGE_SIDE);
        size.Y = Math.Clamp(size.Y, 1, MAX_IMAGE_SIDE);
    }

    private static void ClampFontInternal(XpTemplateText? text)
    {
        if (text is not null)
            text.FontSize = Math.Clamp(text.FontSize, 1, MAX_FONT_SIZE);
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