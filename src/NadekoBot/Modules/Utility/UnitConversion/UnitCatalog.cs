using System.Collections.Frozen;

namespace NadekoBot.Modules.Utility.UnitConversion;

public static class UnitCatalog
{
    private const int AMBIGUOUS = -1;
    private const int MAX_SUGGEST_LENGTH = 32;

    private static readonly Dimension _len = new(length: 1);
    private static readonly Dimension _area = new(length: 2);
    private static readonly Dimension _vol = new(length: 3);
    private static readonly Dimension _mass = new(mass: 1);
    private static readonly Dimension _time = new(time: 1);
    private static readonly Dimension _temp = new(temperature: 1);
    private static readonly Dimension _speed = new(length: 1, time: -1);
    private static readonly Dimension _accel = new(length: 1, time: -2);
    private static readonly Dimension _pressure = new(length: -1, mass: 1, time: -2);
    private static readonly Dimension _energy = new(length: 2, mass: 1, time: -2);
    private static readonly Dimension _power = new(length: 2, mass: 1, time: -3);
    private static readonly Dimension _force = new(length: 1, mass: 1, time: -2);
    private static readonly Dimension _freq = new(time: -1);
    private static readonly Dimension _data = new(information: 1);
    private static readonly Dimension _dataRate = new(information: 1, time: -1);
    private static readonly Dimension _angle = new(angle: 1);
    private static readonly Dimension _fuel = new(length: -2);
    private static readonly Dimension _current = new(current: 1);
    private static readonly Dimension _charge = new(current: 1, time: 1);
    private static readonly Dimension _voltage = new(length: 2, mass: 1, time: -3, current: -1);
    private static readonly Dimension _resistance = new(length: 2, mass: 1, time: -3, current: -2);

    public static readonly Dimension CurrencyDimension = new(currency: 1);

    // Longest symbols first, so "da" wins over "d".
    public static readonly UnitPrefix[] Prefixes =
    [
        new("da", "deca", 1e1, PrefixSet.SiLarge),
        new("Ki", "kibi", 1024d, PrefixSet.Binary),
        new("Mi", "mebi", 1048576d, PrefixSet.Binary),
        new("Gi", "gibi", 1073741824d, PrefixSet.Binary),
        new("Ti", "tebi", 1099511627776d, PrefixSet.Binary),
        new("Pi", "pebi", 1125899906842624d, PrefixSet.Binary),
        new("Ei", "exbi", 1152921504606846976d, PrefixSet.Binary),
        new("Q", "quetta", 1e30, PrefixSet.SiLarge),
        new("R", "ronna", 1e27, PrefixSet.SiLarge),
        new("Y", "yotta", 1e24, PrefixSet.SiLarge),
        new("Z", "zetta", 1e21, PrefixSet.SiLarge),
        new("E", "exa", 1e18, PrefixSet.SiLarge),
        new("P", "peta", 1e15, PrefixSet.SiLarge),
        new("T", "tera", 1e12, PrefixSet.SiLarge),
        new("G", "giga", 1e9, PrefixSet.SiLarge),
        new("M", "mega", 1e6, PrefixSet.SiLarge),
        new("k", "kilo", 1e3, PrefixSet.SiLarge),
        new("h", "hecto", 1e2, PrefixSet.SiLarge),
        new("d", "deci", 1e-1, PrefixSet.SiSmall),
        new("c", "centi", 1e-2, PrefixSet.SiSmall),
        new("m", "milli", 1e-3, PrefixSet.SiSmall),
        new("µ", "micro", 1e-6, PrefixSet.SiSmall),
        new("μ", "micro", 1e-6, PrefixSet.SiSmall),
        new("u", "micro", 1e-6, PrefixSet.SiSmall),
        new("n", "nano", 1e-9, PrefixSet.SiSmall),
        new("p", "pico", 1e-12, PrefixSet.SiSmall),
        new("f", "femto", 1e-15, PrefixSet.SiSmall),
        new("a", "atto", 1e-18, PrefixSet.SiSmall),
        new("z", "zepto", 1e-21, PrefixSet.SiSmall),
        new("y", "yocto", 1e-24, PrefixSet.SiSmall),
        new("r", "ronto", 1e-27, PrefixSet.SiSmall),
        new("q", "quecto", 1e-30, PrefixSet.SiSmall),
    ];

    public static readonly UnitDef[] Units =
    [
        U("m", "meter", "meters", UnitCategory.Length, _len, 1, PrefixSet.Si, names: ["metre", "metres"]),
        U("in", "inch", "inches", UnitCategory.Length, _len, 0.0254),
        U("ft", "foot", "feet", UnitCategory.Length, _len, 0.3048),
        U("yd", "yard", "yards", UnitCategory.Length, _len, 0.9144),
        U("mi", "mile", "miles", UnitCategory.Length, _len, 1609.344),
        U("mil", "mil", "mils", UnitCategory.Length, _len, 2.54e-5, names: ["thou"]),
        U("nmi", "nautical mile", "nautical miles", UnitCategory.Length, _len, 1852,
            names: ["nauticalmile", "nauticalmiles"]),
        U("au", "astronomical unit", "astronomical units", UnitCategory.Length, _len, 149597870700, syms: ["AU"]),
        U("ly", "light-year", "light-years", UnitCategory.Length, _len, 9460730472580800,
            names: ["lightyear", "lightyears"]),
        U("pc", "parsec", "parsecs", UnitCategory.Length, _len, 3.0856775814913673e16, PrefixSet.SiLarge),
        U("Å", "angstrom", "angstroms", UnitCategory.Length, _len, 1e-10, syms: ["Å"], names: ["ångström"]),

        U("ha", "hectare", "hectares", UnitCategory.Area, _area, 1e4),
        U("a", "are", "ares", UnitCategory.Area, _area, 100),
        U("ac", "acre", "acres", UnitCategory.Area, _area, 4046.8564224),
        U("rood", "rood", "roods", UnitCategory.Area, _area, 1011.7141056),
        U("perch", "perch", "perches", UnitCategory.Area, _area, 25.29285264),
        U("cent", "cent", "cents", UnitCategory.Area, _area, 40.468564224),

        U("L", "liter", "liters", UnitCategory.Volume, _vol, 1e-3, PrefixSet.Si,
            syms: ["l", "ℓ"], names: ["litre", "litres"]),
        U("cc", "cubic centimeter", "cubic centimeters", UnitCategory.Volume, _vol, 1e-6),
        U("gal", "US gallon", "US gallons", UnitCategory.Volume, _vol, 0.003785411784,
            syms: ["usgal"], names: ["gallon", "gallons"]),
        U("impgal", "imperial gallon", "imperial gallons", UnitCategory.Volume, _vol, 0.00454609, syms: ["ukgal"]),
        U("qt", "US quart", "US quarts", UnitCategory.Volume, _vol, 0.000946352946,
            syms: ["usqt"], names: ["quart", "quarts"]),
        U("impqt", "imperial quart", "imperial quarts", UnitCategory.Volume, _vol, 0.0011365225, syms: ["ukqt"]),
        U("pt", "US pint", "US pints", UnitCategory.Volume, _vol, 0.000473176473,
            syms: ["uspt"], names: ["pint", "pints"]),
        U("imppt", "imperial pint", "imperial pints", UnitCategory.Volume, _vol, 0.00056826125, syms: ["ukpt"]),
        U("cup", "US cup", "US cups", UnitCategory.Volume, _vol, 0.0002365882365, names: ["cups"]),
        U("floz", "US fluid ounce", "US fluid ounces", UnitCategory.Volume, _vol, 2.95735295625e-5,
            syms: ["usfloz", "fl.oz"], names: ["fluid ounce", "fluid ounces"]),
        U("impfloz", "imperial fluid ounce", "imperial fluid ounces", UnitCategory.Volume, _vol, 2.84130625e-5,
            syms: ["ukfloz"]),
        U("tbsp", "tablespoon", "tablespoons", UnitCategory.Volume, _vol, 1.478676478125e-5, syms: ["tbs"]),
        U("tsp", "teaspoon", "teaspoons", UnitCategory.Volume, _vol, 4.92892159375e-6, syms: ["tspn"]),
        U("bbl", "barrel", "barrels", UnitCategory.Volume, _vol, 0.158987294928),

        U("g", "gram", "grams", UnitCategory.Mass, _mass, 1e-3, PrefixSet.Si, names: ["gramme", "grammes"]),
        U("t", "tonne", "tonnes", UnitCategory.Mass, _mass, 1000, PrefixSet.SiLarge,
            names: ["metric ton", "metric tons"]),
        U("lb", "pound", "pounds", UnitCategory.Mass, _mass, 0.45359237, syms: ["lbs"]),
        U("oz", "ounce", "ounces", UnitCategory.Mass, _mass, 0.028349523125),
        U("ozt", "troy ounce", "troy ounces", UnitCategory.Mass, _mass, 0.0311034768),
        U("st", "stone", "stones", UnitCategory.Mass, _mass, 6.35029318),
        U("gr", "grain", "grains", UnitCategory.Mass, _mass, 6.479891e-5),
        U("ct", "carat", "carats", UnitCategory.Mass, _mass, 2e-4),
        U("dwt", "pennyweight", "pennyweights", UnitCategory.Mass, _mass, 0.00155517384),
        U("slug", "slug", "slugs", UnitCategory.Mass, _mass, 14.593902937206364),
        U("shortton", "short ton", "short tons", UnitCategory.Mass, _mass, 907.18474, syms: ["uston"]),
        U("longton", "long ton", "long tons", UnitCategory.Mass, _mass, 1016.0469088, syms: ["ukton"]),
        U("Da", "dalton", "daltons", UnitCategory.Mass, _mass, 1.66053906892e-27, PrefixSet.SiLarge),

        U("s", "second", "seconds", UnitCategory.Time, _time, 1, PrefixSet.Si, syms: ["sec", "secs"]),
        U("min", "minute", "minutes", UnitCategory.Time, _time, 60, syms: ["mins"]),
        U("h", "hour", "hours", UnitCategory.Time, _time, 3600, syms: ["hr", "hrs"]),
        U("d", "day", "days", UnitCategory.Time, _time, 86400),
        U("wk", "week", "weeks", UnitCategory.Time, _time, 604800),
        U("mo", "month", "months", UnitCategory.Time, _time, 2629746),
        U("yr", "year", "years", UnitCategory.Time, _time, 31556952),
        U("decade", "decade", "decades", UnitCategory.Time, _time, 315569520),
        U("century", "century", "centuries", UnitCategory.Time, _time, 3155695200),

        U("K", "kelvin", "kelvins", UnitCategory.Temperature, _temp, 1, PrefixSet.Si),
        U("°C", "degree Celsius", "degrees Celsius", UnitCategory.Temperature, _temp, 1, offset: 273.15,
            syms: ["C", "degC", "℃"], names: ["celsius", "centigrade", "celcius"]),
        U("°F", "degree Fahrenheit", "degrees Fahrenheit", UnitCategory.Temperature, _temp, 5d / 9,
            offset: 459.67 * 5 / 9, syms: ["F", "degF", "℉"], names: ["fahrenheit"]),
        U("°R", "degree Rankine", "degrees Rankine", UnitCategory.Temperature, _temp, 5d / 9,
            syms: ["R", "degR"], names: ["rankine"]),

        U("km/h", "kilometer per hour", "kilometers per hour", UnitCategory.Speed, _speed, 1 / 3.6,
            syms: ["kph", "kmh", "kmph"]),
        U("mph", "mile per hour", "miles per hour", UnitCategory.Speed, _speed, 0.44704),
        U("kn", "knot", "knots", UnitCategory.Speed, _speed, 1852d / 3600),

        U("Pa", "pascal", "pascals", UnitCategory.Pressure, _pressure, 1, PrefixSet.Si),
        U("bar", "bar", "bars", UnitCategory.Pressure, _pressure, 1e5, PrefixSet.Si),
        U("mbar", "millibar", "millibars", UnitCategory.Pressure, _pressure, 100, syms: ["mb"]),
        U("atm", "atmosphere", "atmospheres", UnitCategory.Pressure, _pressure, 101325),
        U("Torr", "torr", "torrs", UnitCategory.Pressure, _pressure, 101325d / 760, PrefixSet.SiSmall),
        U("mmHg", "millimeter of mercury", "millimeters of mercury", UnitCategory.Pressure, _pressure,
            133.322387415),
        U("inHg", "inch of mercury", "inches of mercury", UnitCategory.Pressure, _pressure, 3386.389),
        U("psi", "psi", "psi", UnitCategory.Pressure, _pressure, 6894.757293168361, PrefixSet.SiLarge,
            syms: ["lbf/in2"]),

        U("J", "joule", "joules", UnitCategory.Energy, _energy, 1, PrefixSet.Si),
        U("cal", "calorie", "calories", UnitCategory.Energy, _energy, 4.184, PrefixSet.Si),
        U("kcal", "kilocalorie", "kilocalories", UnitCategory.Energy, _energy, 4184, syms: ["Cal"]),
        U("Wh", "watt-hour", "watt-hours", UnitCategory.Energy, _energy, 3600, PrefixSet.Si,
            names: ["watt hour", "watt hours"]),
        U("eV", "electronvolt", "electronvolts", UnitCategory.Energy, _energy, 1.602176634e-19, PrefixSet.Si),
        U("BTU", "British thermal unit", "British thermal units", UnitCategory.Energy, _energy, 1055.05585262,
            syms: ["Btu", "btu"]),
        U("thm", "therm", "therms", UnitCategory.Energy, _energy, 105506000),
        U("erg", "erg", "ergs", UnitCategory.Energy, _energy, 1e-7),
        U("ftlbf", "foot-pound", "foot-pounds", UnitCategory.Energy, _energy, 1.3558179483314004,
            syms: ["ftlb"]),

        U("W", "watt", "watts", UnitCategory.Power, _power, 1, PrefixSet.Si),
        U("hp", "horsepower", "horsepower", UnitCategory.Power, _power, 745.69987158227022),
        U("PS", "metric horsepower", "metric horsepower", UnitCategory.Power, _power, 735.49875),

        U("N", "newton", "newtons", UnitCategory.Force, _force, 1, PrefixSet.Si),
        U("lbf", "pound-force", "pounds-force", UnitCategory.Force, _force, 4.4482216152605),
        U("kgf", "kilogram-force", "kilograms-force", UnitCategory.Force, _force, 9.80665),
        U("dyn", "dyne", "dynes", UnitCategory.Force, _force, 1e-5),

        U("Hz", "hertz", "hertz", UnitCategory.Frequency, _freq, 1, PrefixSet.Si),
        U("rpm", "revolution per minute", "revolutions per minute", UnitCategory.Frequency, _freq, 1d / 60),

        U("b", "bit", "bits", UnitCategory.Data, _data, 1, PrefixSet.Data, syms: ["bit"]),
        U("B", "byte", "bytes", UnitCategory.Data, _data, 8, PrefixSet.Data),
        U("bps", "bit per second", "bits per second", UnitCategory.DataRate, _dataRate, 1, PrefixSet.SiLarge),

        U("°", "degree", "degrees", UnitCategory.Angle, _angle, Math.PI / 180, syms: ["deg"]),
        U("rad", "radian", "radians", UnitCategory.Angle, _angle, 1, PrefixSet.SiSmall),
        U("grad", "gradian", "gradians", UnitCategory.Angle, _angle, Math.PI / 200, syms: ["gon"]),
        U("arcmin", "arcminute", "arcminutes", UnitCategory.Angle, _angle, Math.PI / 10800, syms: ["′"]),
        U("arcsec", "arcsecond", "arcseconds", UnitCategory.Angle, _angle, Math.PI / 648000, syms: ["″"]),
        U("turn", "turn", "turns", UnitCategory.Angle, _angle, Math.Tau, syms: ["rev"],
            names: ["revolution", "revolutions"]),

        U("mpg", "mile per US gallon", "miles per US gallon", UnitCategory.FuelEconomy, _fuel,
            1609.344 / 0.003785411784, syms: ["usmpg"]),
        U("impmpg", "mile per imperial gallon", "miles per imperial gallon", UnitCategory.FuelEconomy, _fuel,
            1609.344 / 0.00454609, syms: ["ukmpg"]),
        U("km/L", "kilometer per liter", "kilometers per liter", UnitCategory.FuelEconomy, _fuel, 1e6,
            syms: ["kmpl", "km/l"]),
        // Volume per distance is an area, so this must stay after the area units to keep m2 as Area.
        U("L/100km", "liter per 100 kilometers", "liters per 100 kilometers", UnitCategory.FuelEconomy, _area,
            1e-8, syms: ["l/100km"]),

        U("A", "ampere", "amperes", UnitCategory.Current, _current, 1, PrefixSet.Si, names: ["amp", "amps"]),
        U("Ah", "ampere-hour", "ampere-hours", UnitCategory.Charge, _charge, 3600, PrefixSet.Si),
        U("V", "volt", "volts", UnitCategory.Voltage, _voltage, 1, PrefixSet.Si),
        U("Ω", "ohm", "ohms", UnitCategory.Resistance, _resistance, 1, PrefixSet.Si, syms: ["Ω", "ohm"]),
    ];

    public static readonly FrozenDictionary<UnitCategory, string[]> CommonTargets
        = new Dictionary<UnitCategory, string[]>
        {
            [UnitCategory.Length] = ["m", "km", "cm", "mm", "in", "ft", "yd", "mi"],
            [UnitCategory.Area] = ["m2", "km2", "ha", "ac", "ft2"],
            [UnitCategory.Volume] = ["L", "mL", "m3", "gal", "impgal", "floz", "cup"],
            [UnitCategory.Mass] = ["kg", "g", "lb", "oz", "st", "t"],
            [UnitCategory.Time] = ["s", "min", "h", "d", "wk", "yr"],
            [UnitCategory.Temperature] = ["°C", "°F", "K"],
            [UnitCategory.Speed] = ["km/h", "mph", "m/s", "kn"],
            [UnitCategory.Acceleration] = ["m/s2", "ft/s2"],
            [UnitCategory.Pressure] = ["Pa", "kPa", "bar", "psi", "atm"],
            [UnitCategory.Energy] = ["J", "kJ", "kcal", "kWh", "BTU"],
            [UnitCategory.Power] = ["W", "kW", "hp", "PS"],
            [UnitCategory.Force] = ["N", "kN", "lbf", "kgf"],
            [UnitCategory.Frequency] = ["Hz", "kHz", "MHz", "rpm"],
            [UnitCategory.Data] = ["B", "kB", "MB", "GB", "KiB", "MiB", "GiB"],
            [UnitCategory.DataRate] = ["kbps", "Mbps", "Gbps", "MB/s"],
            [UnitCategory.Angle] = ["°", "rad", "turn", "grad"],
            [UnitCategory.FuelEconomy] = ["mpg", "impmpg", "km/L", "L/100km"],
            [UnitCategory.Current] = ["A", "mA"],
            [UnitCategory.Charge] = ["Ah", "mAh"],
            [UnitCategory.Voltage] = ["V", "mV", "kV"],
            [UnitCategory.Resistance] = ["Ω", "kΩ", "MΩ"],
            [UnitCategory.Currency] = ["USD", "EUR", "GBP", "JPY", "CNY", "INR", "CAD", "AUD"],
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _symbols;
    private static readonly FrozenDictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _symbolsCi;
    private static readonly FrozenDictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _names;
    private static readonly FrozenDictionary<Dimension, UnitCategory> _categoryByDim;
    private static readonly string[] _suggestionKeys;

    static UnitCatalog()
    {
        var symbols = new Dictionary<string, int>(StringComparer.Ordinal);
        // Unit keys contain non-ASCII symbols, but ordinal ignore-case is the only comparer with span lookups.
        var symbolsCi = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var keys = new List<string>(Units.Length * 4);

        for (var i = 0; i < Units.Length; i++)
        {
            ref readonly var u = ref Units[i];
            AddSymbol(u.Symbol, i);
            foreach (var s in u.SymbolAliases ?? [])
                AddSymbol(s, i);

            AddName(u.Name, i);
            AddName(u.Plural, i);
            foreach (var n in u.NameAliases ?? [])
                AddName(n, i);
        }

        _symbols = symbols.ToFrozenDictionary(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>();
        _symbolsCi = symbolsCi.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
                              .GetAlternateLookup<ReadOnlySpan<char>>();
        _names = names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase).GetAlternateLookup<ReadOnlySpan<char>>();
        _suggestionKeys = [.. keys];

        var byDim = new Dictionary<Dimension, UnitCategory>
        {
            [_accel] = UnitCategory.Acceleration,
            [CurrencyDimension] = UnitCategory.Currency
        };
        foreach (ref readonly var u in Units.AsSpan())
            byDim.TryAdd(u.Dim, u.Category);
        _categoryByDim = byDim.ToFrozenDictionary();

        void AddSymbol(string s, int i)
        {
            if (!symbols.TryAdd(s, i))
                throw new InvalidOperationException($"Duplicate unit symbol '{s}'");

            AddMaybeAmbiguous(symbolsCi, s, i);
            keys.Add(s);
        }

        void AddName(string n, int i)
        {
            if (AddMaybeAmbiguous(names, n, i))
                keys.Add(n);
        }
    }

    private static bool AddMaybeAmbiguous(Dictionary<string, int> dict, string key, int index)
    {
        if (dict.TryAdd(key, index))
            return true;

        if (dict[key] != index)
            dict[key] = AMBIGUOUS;

        return false;
    }

    private static UnitDef U(
        string symbol,
        string name,
        string plural,
        UnitCategory category,
        Dimension dim,
        double factor,
        PrefixSet prefixes = PrefixSet.None,
        double offset = 0,
        string[]? syms = null,
        string[]? names = null)
        => new(symbol, name, plural, category, dim, factor, prefixes, offset, syms, names);

    public static bool TryGetCategory(in Dimension dim, out UnitCategory category)
        => _categoryByDim.TryGetValue(dim, out category);

    public static string GetCategoryName(UnitCategory category)
        => category switch
        {
            UnitCategory.FuelEconomy => "Fuel economy",
            UnitCategory.DataRate => "Data rate",
            _ => category.ToString()
        };

    // Order matters: exact symbols and currency codes beat prefix guesses, and case-insensitive matches
    // are only taken when they have one meaning.
    public static ResolveStatus Resolve(ReadOnlySpan<char> token, CurrencyRates rates, out ResolvedUnit unit)
    {
        unit = default;
        if (token.IsEmpty)
            return ResolveStatus.Invalid;

        if (_symbols.TryGetValue(token, out var idx))
        {
            unit = FromUnit(idx, -1);
            return ResolveStatus.Ok;
        }

        if (CurrencyRates.IsUpperCode(token) && rates.TryGet(token, out var currency))
        {
            unit = FromCurrency(currency);
            return ResolveStatus.Ok;
        }

        if (TryPrefixedSymbol(token, out unit))
            return ResolveStatus.Ok;

        if ((_symbolsCi.TryGetValue(token, out idx) || _names.TryGetValue(token, out idx)) && idx != AMBIGUOUS)
        {
            unit = FromUnit(idx, -1);
            return ResolveStatus.Ok;
        }

        if (rates.TryGet(token, out currency))
        {
            unit = FromCurrency(currency);
            return ResolveStatus.Ok;
        }

        if (TryPrefixedName(token, out unit) || TryLoosePrefixedSymbol(token, out unit))
            return ResolveStatus.Ok;

        return rates.IsEmpty && CurrencyRates.IsKnownCode(token)
            ? ResolveStatus.RatesUnavailable
            : ResolveStatus.Unknown;
    }

    private static bool TryPrefixedSymbol(ReadOnlySpan<char> token, out ResolvedUnit unit)
    {
        for (var p = 0; p < Prefixes.Length; p++)
        {
            var prefix = Prefixes[p];
            if (token.Length <= prefix.Symbol.Length || !token.StartsWith(prefix.Symbol, StringComparison.Ordinal))
                continue;

            if (_symbols.TryGetValue(token[prefix.Symbol.Length..], out var idx)
                && (Units[idx].Prefixes & prefix.Set) != 0)
            {
                unit = FromUnit(idx, p);
                return true;
            }
        }

        unit = default;
        return false;
    }

    private static bool TryPrefixedName(ReadOnlySpan<char> token, out ResolvedUnit unit)
    {
        for (var p = 0; p < Prefixes.Length; p++)
        {
            var prefix = Prefixes[p];
            if (token.Length <= prefix.Name.Length
                || !token.StartsWith(prefix.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (_names.TryGetValue(token[prefix.Name.Length..], out var idx)
                && idx != AMBIGUOUS
                && (Units[idx].Prefixes & prefix.Set) != 0)
            {
                unit = FromUnit(idx, p);
                return true;
            }
        }

        unit = default;
        return false;
    }

    // Accepts forms like "KM" or "Kg" only when exactly one prefix and unit pair fits.
    private static bool TryLoosePrefixedSymbol(ReadOnlySpan<char> token, out ResolvedUnit unit)
    {
        unit = default;
        for (var pass = 0; pass < 2; pass++)
        {
            var found = 0;
            int foundIdx = 0, foundPrefix = 0;
            for (var p = 0; p < Prefixes.Length; p++)
            {
                var prefix = Prefixes[p];
                if (token.Length <= prefix.Symbol.Length
                    || !token.StartsWith(prefix.Symbol, StringComparison.OrdinalIgnoreCase))
                    continue;

                var rest = token[prefix.Symbol.Length..];
                var ok = pass == 0
                    ? _symbols.TryGetValue(rest, out var idx)
                    : _symbolsCi.TryGetValue(rest, out idx) && idx != AMBIGUOUS;

                if (!ok || (Units[idx].Prefixes & prefix.Set) == 0)
                    continue;

                if (found > 0 && foundIdx == idx && Prefixes[foundPrefix].Factor == prefix.Factor)
                    continue;

                found++;
                foundIdx = idx;
                foundPrefix = p;
            }

            if (found == 1)
            {
                unit = FromUnit(foundIdx, foundPrefix);
                return true;
            }

            if (found > 1)
                return false;
        }

        return false;
    }

    private static ResolvedUnit FromUnit(int idx, int prefix)
    {
        ref readonly var u = ref Units[idx];
        var factor = prefix < 0 ? u.Factor : u.Factor * Prefixes[prefix].Factor;
        return new(factor, u.Offset, u.Dim, UnitKind.Unit, (short)idx, (sbyte)prefix);
    }

    private static ResolvedUnit FromCurrency(in CurrencyInfo c)
        => new(c.Factor, 0, CurrencyDimension, UnitKind.Currency, currencyCode: c.Code);

    public static string GetSymbol(in ResolvedUnit u)
        => u.Kind switch
        {
            UnitKind.Unit when u.PrefixIndex >= 0 => string.Concat(Prefixes[u.PrefixIndex].Symbol,
                Units[u.UnitIndex].Symbol),
            UnitKind.Unit => Units[u.UnitIndex].Symbol,
            UnitKind.Currency => u.CurrencyCode!,
            _ => string.Empty
        };

    public static string? GetName(in ResolvedUnit u, bool plural)
    {
        switch (u.Kind)
        {
            case UnitKind.Unit:
                ref readonly var def = ref Units[u.UnitIndex];
                var name = plural ? def.Plural : def.Name;
                return u.PrefixIndex >= 0 ? string.Concat(Prefixes[u.PrefixIndex].Name, name) : name;
            case UnitKind.Currency:
                return CurrencyRates.GetName(u.CurrencyCode!);
            default:
                return null;
        }
    }

    public static string? Suggest(ReadOnlySpan<char> token, CurrencyRates rates)
    {
        if (token.Length is < 2 or > MAX_SUGGEST_LENGTH)
            return null;

        var maxDistance = token.Length <= 3 ? 1 : 2;
        Span<int> prev = stackalloc int[MAX_SUGGEST_LENGTH * 2 + 1];
        Span<int> curr = stackalloc int[MAX_SUGGEST_LENGTH * 2 + 1];

        string? best = null;
        var bestDistance = maxDistance + 1;

        foreach (var key in _suggestionKeys)
            Consider(token, key, prev, curr, ref best, ref bestDistance);

        foreach (var code in rates.Codes)
            Consider(token, code, prev, curr, ref best, ref bestDistance);

        return best;
    }

    private static void Consider(
        ReadOnlySpan<char> token,
        string key,
        Span<int> prev,
        Span<int> curr,
        ref string? best,
        ref int bestDistance)
    {
        if (Math.Abs(key.Length - token.Length) >= bestDistance || key.Length > MAX_SUGGEST_LENGTH * 2)
            return;

        var d = Distance(token, key, prev, curr, bestDistance);
        if (d < bestDistance)
        {
            bestDistance = d;
            best = key;
        }
    }

    // Case-insensitive Levenshtein distance which stops early once every cell exceeds the bound.
    private static int Distance(ReadOnlySpan<char> a, ReadOnlySpan<char> b, Span<int> prev, Span<int> curr, int bound)
    {
        for (var j = 0; j <= b.Length; j++)
            prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            var rowMin = i;
            var ca = char.ToLowerInvariant(a[i - 1]);
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = ca == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                var v = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
                curr[j] = v;
                if (v < rowMin)
                    rowMin = v;
            }

            if (rowMin >= bound)
                return bound;

            var tmp = prev;
            prev = curr;
            curr = tmp;
        }

        return prev[b.Length];
    }
}
