using System.Security.Cryptography;
using System.Text;
using AngleSharp.Common;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Modules.Administration;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Modules.Games.Quests;

namespace NadekoBot.Modules.Games.Fish;

public sealed class FishService(
    FishConfigService fcs,
    IBotCache cache,
    DbService db,
    INotifySubscriber notify,
    QuestService quests,
    FishItemService itemService
)
    : INService
{
    private const double MAX_SKILL = 100;

    private readonly Random _rng = new Random();

    private static TypedKey<bool> FishingKey(ulong userId)
        => new($"fishing:{userId}");

    public async Task<OneOf.OneOf<Task<FishResult?>, AlreadyFishing>> FishAsync(ulong userId, ulong channelId,
        FishMultipliers multipliers)
    {
        var duration = _rng.Next(3, 6) / multipliers.FishingSpeedMultiplier;

        if (!await cache.AddAsync(FishingKey(userId), true, TimeSpan.FromSeconds(duration), overwrite: false))
        {
            return new AlreadyFishing();
        }

        return TryFishAsync(userId, channelId, duration, multipliers);
    }

    private async Task<FishResult?> TryFishAsync(
        ulong userId,
        ulong channelId,
        double duration,
        FishMultipliers multipliers)
    {
        var conf = fcs.Data;
        await Task.Delay(TimeSpan.FromSeconds(duration));

        var (playerSkill, _) = await GetSkill(userId);
        var fishChanceMultiplier = Math.Clamp((playerSkill + 20) / MAX_SKILL, 0, 1);
        var trashChanceMultiplier = Math.Clamp(((2 * MAX_SKILL) - playerSkill) / MAX_SKILL, 1, 2);

        var nothingChance = conf.Chance.Nothing;
        var fishChance = conf.Chance.Fish * fishChanceMultiplier * multipliers.FishMultiplier;
        var trashChance = conf.Chance.Trash * trashChanceMultiplier * multipliers.TrashMultiplier;

        // first roll whether it's fish, trash or nothing
        var totalChance = fishChance + trashChance + conf.Chance.Nothing;

        var typeRoll = _rng.NextDouble() * totalChance;

        if (typeRoll < nothingChance)
        {
            return null;
        }

        var isFish = typeRoll < nothingChance + fishChance;

        var items = isFish
            ? conf.Fish
            : conf.Trash;

        var result = await FishAsyncInternal(userId, channelId, items, multipliers);
        
        // use bait
        if (result is not null)
        {
            await itemService.UseBaitAsync(userId);
        }

        // skill
        if (result is not null)
        {
            var isSkillUp = await TrySkillUpAsync(userId, playerSkill);

            result.IsSkillUp = isSkillUp;
            result.MaxSkill = (int)MAX_SKILL;
            result.Skill = playerSkill;

            if (isSkillUp)
            {
                result.Skill += 1;
            }
        }

        // notification system
        if (result is not null)
        {
            if (result.IsMaxStar() || result.IsRare())
            {
                await notify.NotifyAsync(new NiceCatchNotifyModel(
                    userId,
                    result.Fish,
                    GetStarText(result.Stars, result.Fish.Stars)
                ));
            }

            await quests.ReportActionAsync(userId,
                QuestEventType.FishCaught,
                new()
                {
                    { "fish", result.Fish.Name },
                    { "type", typeRoll < nothingChance + fishChance ? "fish" : "trash" },
                    { "stars", result.Stars.ToString() }
                });
        }

        return result;
    }

    private async Task<bool> TrySkillUpAsync(ulong userId, int playerSkill)
    {
        var skillUpProb = GetSkillUpProb(playerSkill);

        var rng = _rng.NextDouble();

        if (rng < skillUpProb)
        {
            await using var ctx = db.GetDbContext();

            var maxSkill = (int)MAX_SKILL;
            await ctx.GetTable<UserFishStats>()
                .InsertOrUpdateAsync(() => new()
                    {
                        UserId = userId,
                        Skill = 1,
                    },
                    (old) => new()
                    {
                        UserId = userId,
                        Skill = old.Skill > maxSkill ? maxSkill : old.Skill + 1
                    },
                    () => new()
                    {
                        UserId = userId,
                        Skill = playerSkill
                    });

            return true;
        }

        return false;
    }

    private double GetSkillUpProb(int playerSkill)
    {
        if (playerSkill < 0)
            playerSkill = 0;

        if (playerSkill >= 100)
            return 0;

        return 1 / (Math.Pow(Math.E, playerSkill / 22d));
    }

    public async Task<(int skill, int maxSkill)> GetSkill(ulong userId)
    {
        await using var ctx = db.GetDbContext();

        var skill = await ctx.GetTable<UserFishStats>()
            .Where(x => x.UserId == userId)
            .Select(x => x.Skill)
            .FirstOrDefaultAsyncLinqToDB();

        return (skill, (int)MAX_SKILL);
    }

    private async Task<FishResult?> FishAsyncInternal(
        ulong userId,
        ulong channelId,
        List<FishData> items,
        FishMultipliers multipliers)
    {
        var filteredItems = new List<FishData>();

        var loc = GetSpot(channelId);
        var time = GetTime();
        var w = GetWeather(DateTime.UtcNow);

        foreach (var item in items)
        {
            if (item.Condition is { Count: > 0 })
            {
                if (!item.Condition.Any(x => channelId.ToString().EndsWith(x)))
                {
                    continue;
                }
            }

            if (item.Spot is not null && item.Spot != loc)
                continue;

            if (item.Time is not null && item.Time != time)
                continue;

            if (item.Weather is not null && item.Weather != w)
                continue;

            filteredItems.Add(item);
        }


        var maxSum = filteredItems
            .Select(x => (x.Id, x.Chance, x.Stars))
            .Select(x =>
            {
                if (x.Chance <= 15)
                    return x with
                    {
                        Chance = x.Chance *= multipliers.RareMultiplier
                    };

                return x;
            })
            .Sum(x => { return x.Chance * 100; });


        var roll = _rng.NextDouble() * maxSum;

        FishResult? caught = null;

        var curSum = 0d;
        foreach (var i in filteredItems)
        {
            curSum += i.Chance * 100;

            if (roll < curSum)
            {
                caught = new FishResult()
                {
                    Fish = i,
                    Stars = GetRandomStars(i.Stars, multipliers),
                };
                break;
            }
        }

        if (caught is not null)
        {
            await using var uow = db.GetDbContext();

            await uow.GetTable<FishCatch>()
                .InsertOrUpdateAsync(() => new FishCatch()
                    {
                        UserId = userId,
                        FishId = caught.Fish.Id,
                        MaxStars = caught.Stars,
                        Count = 1
                    },
                    (old) => new FishCatch()
                    {
                        Count = old.Count + 1,
                        MaxStars = Math.Max(old.MaxStars, caught.Stars),
                    },
                    () => new()
                    {
                        FishId = caught.Fish.Id,
                        UserId = userId
                    });

            return caught;
        }

        Log.Error(
            "Something went wrong in the fish command, no fish with sufficient chance was found, Roll: {Roll}, MaxSum: {MaxSum}",
            roll,
            maxSum);

        return null;
    }

    public FishingSpot GetSpot(ulong channelId)
    {
        var cid = (channelId >> 22 >> 29) % 10;

        return cid switch
        {
            < 1 => FishingSpot.Reef,
            < 3 => FishingSpot.River,
            < 5 => FishingSpot.Lake,
            < 7 => FishingSpot.Swamp,
            _ => FishingSpot.Ocean,
        };
    }

    public FishingTime GetTime()
    {
        var hour = DateTime.UtcNow.Hour % 12;

        if (hour < 3)
            return FishingTime.Night;

        if (hour < 4)
            return FishingTime.Dawn;

        if (hour < 11)
            return FishingTime.Day;

        return FishingTime.Dusk;
    }

    private const int WEATHER_PERIODS_PER_DAY = 12;

    public IReadOnlyList<FishingWeather> GetWeatherForPeriods(int periods)
    {
        var now = DateTime.UtcNow;
        var result = new FishingWeather[periods];

        for (var i = 0; i < periods; i++)
        {
            result[i] = GetWeather(now.AddHours(i * GetWeatherPeriodDuration()));
        }

        return result;
    }

    public FishingWeather GetCurrentWeather()
        => GetWeather(DateTime.UtcNow);

    public FishingWeather GetWeather(DateTime time)
        => GetWeather(time, fcs.Data.WeatherSeed);

    private FishingWeather GetWeather(DateTime time, string seed)
    {
        var year = time.Year;
        var dayOfYear = time.DayOfYear;
        var hour = time.Hour;

        var num = (year * 100_000) + (dayOfYear * 100) + (hour / GetWeatherPeriodDuration());

        Span<byte> dataArray = stackalloc byte[4];
        BitConverter.TryWriteBytes(dataArray, num);

        Span<byte> seedArray = stackalloc byte[seed.Length];
        for (var index = 0; index < seed.Length; index++)
        {
            var c = seed[index];
            seedArray[index] = (byte)c;
        }

        Span<byte> arr = stackalloc byte[dataArray.Length + seedArray.Length];

        dataArray.CopyTo(arr);
        seedArray.CopyTo(arr[dataArray.Length..]);

        using var algo = SHA512.Create();

        Span<byte> hash = stackalloc byte[64];
        algo.TryComputeHash(arr, hash, out _);

        byte reduced = 0;
        foreach (var u in hash)
            reduced ^= u;

        var r = reduced % 16;

        // return (FishingWeather)r;
        return r switch
        {
            < 5 => FishingWeather.Clear,
            < 9 => FishingWeather.Rain,
            < 13 => FishingWeather.Storm,
            _ => FishingWeather.Snow
        };
    }


    /// <summary>
    /// Returns a random number of stars between 1 and maxStars
    /// if maxStars == 1, returns 1
    /// if maxStars == 2, returns 1 (66%) or 2 (33%)
    /// if maxStars == 3, returns 1 (65%) or 2 (25%) or 3 (10%)
    /// if maxStars == 5, returns 1 (40%) or 2 (30%) or 3 (15%) or 4 (10%) or 5 (5%)
    /// </summary>
    /// <param name="maxStars">Max Number of stars to generate</param>
    /// <param name="multipliers"></param>
    /// <returns>Random number of stars</returns>
    private int GetRandomStars(int maxStars, FishMultipliers multipliers)
    {
        if (maxStars == 1)
            return 1;

        var maxStarMulti = multipliers.StarMultiplier;
        double baseChance;
        if (maxStars == 2)
        {
            // 15% chance of 1 star, 85% chance of 2 stars
            baseChance = Math.Clamp(0.15 * multipliers.StarMultiplier, 0, 1);
            return _rng.NextDouble() < (1 - baseChance) ? 1 : 2;
        }

        if (maxStars == 3)
        {
            // 65% chance of 1 star, 30% chance of 2 stars, 5% chance of 3 stars
            baseChance = 0.05 * multipliers.StarMultiplier;
            var r = _rng.NextDouble();
            if (r < (1 - baseChance - 0.3))
                return 1;
            if (r < (1 - baseChance))
                return 2;
            return 3;
        }

        if (maxStars == 4)
        {
            // this should never happen
            // 50% chance of 1 star, 25% chance of 2 stars, 18% chance of 3 stars, 7% chance of 4 stars
            var r = _rng.NextDouble();
            baseChance = 0.02 * multipliers.StarMultiplier;
            if (r < (1 - baseChance - 0.45))
                return 1;
            if (r < (1 - baseChance - 0.15))
                return 2;
            if (r < (1 - baseChance))
                return 3;
            return 4;
        }

        if (maxStars == 5)
        {
            // 40% chance of 1 star, 30% chance of 2 stars, 15% chance of 3 stars, 10% chance of 4 stars, 2% chance of 5 stars
            var r = _rng.NextDouble();
            baseChance = 0.02 * multipliers.StarMultiplier;
            if (r < (1 - baseChance - 0.6))
                return 1;
            if (r < (1 - baseChance - 0.3))
                return 2;
            if (r < (1 - baseChance - 0.1))
                return 3;
            if (r < (1 - baseChance))
                return 4;
            return 5;
        }

        return 1;
    }

    public int GetWeatherPeriodDuration()
        => 24 / WEATHER_PERIODS_PER_DAY;

    public async Task<List<FishData>> GetAllFish()
    {
        await Task.Yield();

        var conf = fcs.Data;
        return conf.Fish.Concat(conf.Trash).ToList();
    }

    public async Task<List<FishCatch>> GetUserCatches(ulong userId)
    {
        await using var ctx = db.GetDbContext();

        var catches = await ctx.GetTable<FishCatch>()
            .Where(x => x.UserId == userId)
            .ToListAsyncLinqToDB();

        return catches;
    }

    public async Task<IReadOnlyCollection<(ulong UserId, int Catches)>> GetFishLbAsync(int page)
    {
        await using var ctx = db.GetDbContext();

        var result = await ctx.GetTable<FishCatch>()
            .GroupBy(x => x.UserId)
            .OrderByDescending(x => x.Sum(x => x.Count))
            .Skip(page * 10)
            .Take(10)
            .Select(x => new
            {
                UserId = x.Key,
                Catches = x.Sum(x => x.Count)
            })
            .ToListAsyncLinqToDB()
            .Fmap(x => x.Map(y => (y.UserId, y.Catches)));

        return result;
    }

    public string GetStarText(int resStars, int fishStars)
    {
        if (resStars == fishStars)
        {
            return MultiplyStars(fcs.Data.StarEmojis[^1], fishStars);
        }

        var c = fcs.Data;
        var starsp1 = MultiplyStars(c.StarEmojis[resStars], resStars);
        var starsp2 = MultiplyStars(c.StarEmojis[0], fishStars - resStars);

        return starsp1 + starsp2;
    }

    private string MultiplyStars(string starEmoji, int count)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < count; i++)
        {
            sb.Append(starEmoji);
        }

        return sb.ToString();
    }
}

public sealed class IUserFishCatch
{
    public ulong UserId { get; set; }
    public int Count { get; set; }
}