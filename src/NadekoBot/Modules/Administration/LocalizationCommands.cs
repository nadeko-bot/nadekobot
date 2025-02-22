#nullable disable
using System.Globalization;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class LocalizationCommands : NadekoModule
    {
        private static readonly IReadOnlyDictionary<string, string> _supportedLocales = new Dictionary<string, string>
        {
            { "ar", "العربية" },
            { "zh-TW", "繁體中文, 台灣" },
            { "zh-CN", "简体中文, 中华人民共和国" },
            { "nl-NL", "Nederlands, Nederland" },
            { "en-US", "English, United States" },
            { "fr-FR", "Français, France" },
            { "cs-CZ", "Čeština, Česká republika" },
            { "da-DK", "Dansk, Danmark" },
            { "de-DE", "Deutsch, Deutschland" },
            { "he-IL", "עברית, ישראל" },
            { "hu-HU", "Magyar, Magyarország" },
            { "id-ID", "Bahasa Indonesia, Indonesia" },
            { "it-IT", "Italiano, Italia" },
            { "ja-JP", "日本語, 日本" },
            { "ko-KR", "한국어, 대한민국" },
            { "nb-NO", "Norsk, Norge" },
            { "pl-PL", "Polski, Polska" },
            { "pt-BR", "Português Brasileiro, Brasil" },
            { "ro-RO", "Română, România" },
            { "ru-RU", "Русский, Россия" },
            { "sr-Cyrl-RS", "Српски, Србија" },
            { "es-ES", "Español, España" },
            { "sv-SE", "Svenska, Sverige" },
            { "tr-TR", "Türkçe, Türkiye" },
            { "ts-TS", "Tsundere, You Baka" },
            { "uk-UA", "Українська, Україна" }
        };

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(0)]
        public async Task LanguageSet()
            => await Response().Confirm(strs.lang_set_show(Format.Bold(Culture.ToString()),
                Format.Bold(Culture.NativeName))).SendAsync();

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [Priority(1)]
        public async Task LanguageSet(string name)
        {
            try
            {
                CultureInfo ci;
                if (name.Trim().ToLowerInvariant() == "default")
                {
                    _localization.RemoveGuildCulture(ctx.Guild);
                    ci = _localization.DefaultCultureInfo;
                }
                else
                {
                    ci = new CultureInfo(name);
                    if (!_supportedLocales.ContainsKey(ci.Name))
                    {
                        await LanguagesList();
                        return;
                    }
                    
                    _localization.SetGuildCulture(ctx.Guild, ci);
                }

                var nativeName = ci.NativeName;
                if (ci.Name == "ts-TS")
                    nativeName = _supportedLocales[ci.Name];
                await Response().Confirm(strs.lang_set(Format.Bold(ci.ToString()), Format.Bold(nativeName))).SendAsync();
            }
            catch (Exception)
            {
                await Response().Error(strs.lang_set_fail).SendAsync();
            }
        }

        [Cmd]
        public async Task LanguageSetDefault()
        {
            var cul = _localization.DefaultCultureInfo;
            await Response().Error(strs.lang_set_bot_show(cul, cul.NativeName)).SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        public async Task LanguageSetDefault(string name)
        {
            try
            {
                CultureInfo ci;
                if (name.Trim().ToLowerInvariant() == "default")
                {
                    _localization.ResetDefaultCulture();
                    ci = _localization.DefaultCultureInfo;
                }
                else
                {
                    ci = new CultureInfo(name);
                    if (!_supportedLocales.ContainsKey(ci.Name))
                    {
                        await LanguagesList();
                        return;
                    }
                    _localization.SetDefaultCulture(ci);
                }
                
                var nativeName = ci.NativeName;
                if (ci.Name == "ts-TS")
                    nativeName = _supportedLocales[ci.Name];

                await Response().Confirm(strs.lang_set_bot(Format.Bold(ci.ToString()),
                    Format.Bold(nativeName))).SendAsync();
            }
            catch (Exception)
            {
                await Response().Error(strs.lang_set_fail).SendAsync();
            }
        }

        [Cmd]
        public async Task LanguagesList()
            => await Response().Embed(CreateEmbed()
                                               .WithOkColor()
                                               .WithTitle(GetText(strs.lang_list))
                                               .WithDescription(string.Join("\n",
                                                   _supportedLocales.Select(
                                                       x => $"{Format.Code(x.Key),-10} => {x.Value}")))).SendAsync();
    }
}
/* list of language codes for reference. 
 * taken from https://github.com/dotnet/coreclr/blob/ee5862c6a257e60e263537d975ab6c513179d47f/src/mscorlib/src/System/Globalization/CultureData.cs#L192
            { "029", "en-029" },
            { "AE",  "ar-AE" },
            { "AF",  "prs-AF" },
            { "AL",  "sq-AL" },
            { "AM",  "hy-AM" },
            { "AR",  "es-AR" },
            { "AT",  "de-AT" },
            { "AU",  "en-AU" },
            { "AZ",  "az-Cyrl-AZ" },
            { "BA",  "bs-Latn-BA" },
            { "BD",  "bn-BD" },
            { "BE",  "nl-BE" },
            { "BG",  "bg-BG" },
            { "BH",  "ar-BH" },
            { "BN",  "ms-BN" },
            { "BO",  "es-BO" },
            { "BR",  "pt-BR" },
            { "BY",  "be-BY" },
            { "BZ",  "en-BZ" },
            { "CA",  "en-CA" },
            { "CH",  "it-CH" },
            { "CL",  "es-CL" },
            { "CN",  "zh-CN" },
            { "CO",  "es-CO" },
            { "CR",  "es-CR" },
            { "CS",  "sr-Cyrl-CS" },
            { "CZ",  "cs-CZ" },
            { "DE",  "de-DE" },
            { "DK",  "da-DK" },
            { "DO",  "es-DO" },
            { "DZ",  "ar-DZ" },
            { "EC",  "es-EC" },
            { "EE",  "et-EE" },
            { "EG",  "ar-EG" },
            { "ES",  "es-ES" },
            { "ET",  "am-ET" },
            { "FI",  "fi-FI" },
            { "FO",  "fo-FO" },
            { "FR",  "fr-FR" },
            { "GB",  "en-GB" },
            { "GE",  "ka-GE" },
            { "GL",  "kl-GL" },
            { "GR",  "el-GR" },
            { "GT",  "es-GT" },
            { "HK",  "zh-HK" },
            { "HN",  "es-HN" },
            { "HR",  "hr-HR" },
            { "HU",  "hu-HU" },
            { "ID",  "id-ID" },
            { "IE",  "en-IE" },
            { "IL",  "he-IL" },
            { "IN",  "hi-IN" },
            { "IQ",  "ar-IQ" },
            { "IR",  "fa-IR" },
            { "IS",  "is-IS" },
            { "IT",  "it-IT" },
            { "IV",  "" },
            { "JM",  "en-JM" },
            { "JO",  "ar-JO" },
            { "JP",  "ja-JP" },
            { "KE",  "sw-KE" },
            { "KG",  "ky-KG" },
            { "KH",  "km-KH" },
            { "KR",  "ko-KR" },
            { "KW",  "ar-KW" },
            { "KZ",  "kk-KZ" },
            { "LA",  "lo-LA" },
            { "LB",  "ar-LB" },
            { "LI",  "de-LI" },
            { "LK",  "si-LK" },
            { "LT",  "lt-LT" },
            { "LU",  "lb-LU" },
            { "LV",  "lv-LV" },
            { "LY",  "ar-LY" },
            { "MA",  "ar-MA" },
            { "MC",  "fr-MC" },
            { "ME",  "sr-Latn-ME" },
            { "MK",  "mk-MK" },
            { "MN",  "mn-MN" },
            { "MO",  "zh-MO" },
            { "MT",  "mt-MT" },
            { "MV",  "dv-MV" },
            { "MX",  "es-MX" },
            { "MY",  "ms-MY" },
            { "NG",  "ig-NG" },
            { "NI",  "es-NI" },
            { "NL",  "nl-NL" },
            { "NO",  "nn-NO" },
            { "NP",  "ne-NP" },
            { "NZ",  "en-NZ" },
            { "OM",  "ar-OM" },
            { "PA",  "es-PA" },
            { "PE",  "es-PE" },
            { "PH",  "en-PH" },
            { "PK",  "ur-PK" },
            { "PL",  "pl-PL" },
            { "PR",  "es-PR" },
            { "PT",  "pt-PT" },
            { "PY",  "es-PY" },
            { "QA",  "ar-QA" },
            { "RO",  "ro-RO" },
            { "RS",  "sr-Latn-RS" },
            { "RU",  "ru-RU" },
            { "RW",  "rw-RW" },
            { "SA",  "ar-SA" },
            { "SE",  "sv-SE" },
            { "SG",  "zh-SG" },
            { "SI",  "sl-SI" },
            { "SK",  "sk-SK" },
            { "SN",  "wo-SN" },
            { "SV",  "es-SV" },
            { "SY",  "ar-SY" },
            { "TH",  "th-TH" },
            { "TJ",  "tg-Cyrl-TJ" },
            { "TM",  "tk-TM" },
            { "TN",  "ar-TN" },
            { "TR",  "tr-TR" },
            { "TT",  "en-TT" },
            { "TW",  "zh-TW" },
            { "UA",  "uk-UA" },
            { "US",  "en-US" },
            { "UY",  "es-UY" },
            { "UZ",  "uz-Cyrl-UZ" },
            { "VE",  "es-VE" },
            { "VN",  "vi-VN" },
            { "YE",  "ar-YE" },
            { "ZA",  "af-ZA" },
            { "ZW",  "en-ZW" }
 */