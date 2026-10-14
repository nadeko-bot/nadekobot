namespace NadekoBot.Hosted;

public enum HostMode
{
    Standalone,
    Coordinated
}

public sealed record HostContext(HostMode Mode, int ShardId, int TotalShards)
{
    public const string COORDINATED_ENV = "NADEKOBOT_IS_COORDINATED";

    private const int SHARD_ID_SHIFT = 22;

    public bool OwnsGuild(ulong guildId)
        => TotalShards <= 1 || (int)((guildId >> SHARD_ID_SHIFT) % (ulong)TotalShards) == ShardId;

    public int ApiPort(int basePort)
        => Mode == HostMode.Coordinated ? basePort + ShardId : basePort;
}

public sealed record HostPaths(string DataDir)
{
    public string ApiSettings => Path.Combine(DataDir, "api.yml");
    public string Creds => Path.Combine(DataDir, "creds.yml");
    public string BackupDir => Path.Combine(DataDir, "backups");
    public string RestoreMarker => Path.Combine(DataDir, ".restore-pending");
    public string RestoreStaging => Path.Combine(DataDir, ".restore-staging");
    public string PreRestoreFiles => Path.Combine(DataDir, ".pre-restore");
    public string BootAttempts => Path.Combine(DataDir, ".boot-attempts");
    public string UpdateDir => Path.Combine(DataDir, ".update");
    public string UpdateUpload => Path.Combine(UpdateDir, "upload.tmp");
    public string UpdateStaged => Path.Combine(UpdateDir, "staged");
    public string UpdatePending => Path.Combine(DataDir, ".update-pending");
    public string UpdateTrial => Path.Combine(DataDir, ".update-trial");
    public string UpdateResult => Path.Combine(DataDir, ".update-result");
    public string ServerImportDir => Path.Combine(DataDir, ".server-import");
    public string Activity => Path.Combine(DataDir, "activity.db");
}

public sealed record RestoreTarget(string? DbPath);
