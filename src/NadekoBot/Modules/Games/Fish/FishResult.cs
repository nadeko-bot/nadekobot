namespace NadekoBot.Modules.Games;

public sealed class FishResult
{
    public required FishData Fish { get; init; }
    public int Stars { get; init; }
    public bool IsSkillUp { get; set; }
    public int Skill { get; set; }
    public int MaxSkill { get; set; }
}
public readonly record struct AlreadyFishing;

