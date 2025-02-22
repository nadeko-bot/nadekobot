using System.Security.Cryptography;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;

namespace NadekoBot.Modules.Games.Fish;

public sealed class FishService(FishConfigService fcs, IBotCache cache, DbService db) : INService
{
    private const double MAX_SKILL = 100;

    private readonly Random _rng = new Random();

    private static TypedKey<bool> FishingKey(ulong userId)
        => new($"fishing:{userId}");

    public async Task<OneOf.OneOf<Task<FishResult?>, AlreadyFishing>> FishAsync(ulong userId, ulong channelId)
    {
        var duration = _rng.Next(5, 9);

        if (!await cache.AddAsync(FishingKey(userId), true, TimeSpan.FromSeconds(duration), overwrite: false))
        {
            return new AlreadyFishing();
        }

        return TryFishAsync(userId, channelId, duration);
    }

    private async Task<FishResult?> TryFishAsync(ulong userId, ulong channelId, int duration)
    {
        var conf = fcs.Data;
        await Task.Delay(TimeSpan.FromSeconds(duration));

        var (playerSkill, _) = await GetSkill(userId);
        var fishChanceMultiplier = Math.Clamp((playerSkill + 20) / MAX_SKILL, 0, 1);
        var trashChanceMultiplier = Math.Clamp(((2 * MAX_SKILL) - playerSkill) / MAX_SKILL, 1, 2);

        var nothingChance = conf.Chance.Nothing;
        var fishChance = conf.Chance.Fish * fishChanceMultiplier;
        var trashChance = conf.Chance.Trash * trashChanceMultiplier;

        // first roll whether it's fish, trash or nothing
        var totalChance = fishChance + trashChance + conf.Chance.Nothing;

        var typeRoll = _rng.NextDouble() * totalChance;

        if (typeRoll < nothingChance)
        {
            return null;
        }

        var items = typeRoll < nothingChance + fishChance
            ? conf.Fish
            : conf.Trash;


        var result = await FishAsyncInternal(userId, channelId, items);

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

    private async Task<FishResult?> FishAsyncInternal(ulong userId, ulong channelId, List<FishData> items)
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

        var maxSum = filteredItems.Sum(x => x.Chance * 100);


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
                    Stars = GetRandomStars(i.Stars),
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
    /// <returns>Random number of stars</returns>
    private int GetRandomStars(int maxStars)
    {
        if (maxStars == 1)
            return 1;

        if (maxStars == 2)
        {
            // 15% chance of 1 star, 85% chance of 2 stars
            return _rng.NextDouble() < 0.85 ? 1 : 2;
        }

        if (maxStars == 3)
        {
            // 65% chance of 1 star, 30% chance of 2 stars, 5% chance of 3 stars
            var r = _rng.NextDouble();
            if (r < 0.65)
                return 1;
            if (r < 0.95)
                return 2;
            return 3;
        }

        if (maxStars == 4)
        {
            // this should never happen
            // 50% chance of 1 star, 25% chance of 2 stars, 18% chance of 3 stars, 7% chance of 4 stars
            var r = _rng.NextDouble();
            if (r < 0.55)
                return 1;
            if (r < 0.80)
                return 2;
            if (r < 0.98)
                return 3;
            return 4;
        }

        if (maxStars == 5)
        {
            // 40% chance of 1 star, 30% chance of 2 stars, 15% chance of 3 stars, 10% chance of 4 stars, 5% chance of 5 stars
            var r = _rng.NextDouble();
            if (r < 0.4)
                return 1;
            if (r < 0.7)
                return 2;
            if (r < 0.9)
                return 3;
            if (r < 0.98)
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
}