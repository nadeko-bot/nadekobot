namespace NadekoBot.Services;

public sealed partial class GoogleApiService
{
    private const string SUPPORTED = """
        afrikaans af
        albanian sq
        amharic am
        arabic ar
        armenian hy
        assamese as
        aymara ay
        azerbaijani az
        bambara bm
        basque eu
        belarusian be
        bengali bn
        bhojpuri bho
        bosnian bs
        bulgarian bg
        catalan ca
        cebuano ceb
        chinese zh-CN
        chinese-trad zh-TW
        corsican co
        croatian hr
        czech cs
        danish da
        dhivehi dv
        dogri doi
        dutch nl
        english en
        esperanto eo
        estonian et
        ewe ee
        filipino fil
        finnish fi
        french fr
        frisian fy
        galician gl
        georgian ka
        german de
        greek el
        guarani gn
        gujarati gu
        haitian ht
        hausa ha
        hawaiian haw
        hebrew he 
        hindi hi
        hmong hmn
        hungarian hu
        icelandic is
        igbo ig
        ilocano ilo
        indonesian id
        irish ga
        italian it
        japanese ja
        javanese jv 
        kannada kn
        kazakh kk
        khmer km
        kinyarwanda rw
        konkani gom
        korean ko
        krio kri
        kurdish ku
        kurdish-sor ckb
        kyrgyz ky
        lao lo
        latin la
        latvian lv
        lingala ln
        lithuanian lt
        luganda lg
        luxembourgish lb
        macedonian mk
        maithili mai
        malagasy mg
        malay ms
        malayalam ml
        maltese mt
        maori mi
        marathi mr
        meiteilon mni-Mtei
        mizo lus
        mongolian mn
        myanmar my
        nepali ne
        norwegian no
        nyanja ny
        odia or
        oromo om
        pashto ps
        persian fa
        polish pl
        portuguese pt
        punjabi pa
        quechua qu
        romanian ro
        russian ru
        samoan sm
        sanskrit sa
        scots gd
        sepedi nso
        serbian sr
        sesotho st
        shona sn
        sindhi sd
        sinhala si
        slovak sk
        slovenian sl
        somali so
        spanish es
        sundanese su
        swahili sw
        swedish sv
        tagalog tl
        tajik tg
        tamil ta
        tatar tt
        telugu te
        thai th
        tigrinya ti
        tsonga ts
        turkish tr
        turkmen tk
        twi ak
        ukrainian uk
        urdu ur
        uyghur ug
        uzbek uz
        vietnamese vi
        welsh cy
        xhosa xh
        yiddish yi
        yoruba yo
        zulu zu
        """;

    
    public IReadOnlyDictionary<string, string> Languages { get; }
    
    private GoogleApiService()
    {
        var langs = SUPPORTED.Split("\n")
                             .Select(x => x.Split(' '))
                             .ToDictionary(x => x[0].Trim(), x => x[1].Trim());
        
        foreach (var (_, v) in langs.ToArray())
        {
            langs.Add(v, v);
        }

        Languages = langs;
    }

}