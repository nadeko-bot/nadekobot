namespace NadekoBot.Db;

[Flags]
public enum GuildPerm : ulong
{
    CreateInstantInvite = 1,
    KickMembers = 2,
    BanMembers = 4,
    Administrator = 8,
    ManageChannels = 16, // 0x0000000000000010
    ManageGuild = 32, // 0x0000000000000020
    ViewGuildInsights = 524288, // 0x0000000000080000
    AddReactions = 64, // 0x0000000000000040
    ViewAuditLog = 128, // 0x0000000000000080
    ViewChannel = 1024, // 0x0000000000000400
    SendMessages = 2048, // 0x0000000000000800
    SendTTSMessages = 4096, // 0x0000000000001000
    ManageMessages = 8192, // 0x0000000000002000
    EmbedLinks = 16384, // 0x0000000000004000
    AttachFiles = 32768, // 0x0000000000008000
    ReadMessageHistory = 65536, // 0x0000000000010000
    MentionEveryone = 131072, // 0x0000000000020000
    UseExternalEmojis = 262144, // 0x0000000000040000
    Connect = 1048576, // 0x0000000000100000
    Speak = 2097152, // 0x0000000000200000
    MuteMembers = 4194304, // 0x0000000000400000
    DeafenMembers = 8388608, // 0x0000000000800000
    MoveMembers = 16777216, // 0x0000000001000000
    UseVAD = 33554432, // 0x0000000002000000
    PrioritySpeaker = 256, // 0x0000000000000100
    Stream = 512, // 0x0000000000000200
    ChangeNickname = 67108864, // 0x0000000004000000
    ManageNicknames = 134217728, // 0x0000000008000000
    ManageRoles = 268435456, // 0x0000000010000000
    ManageWebhooks = 536870912, // 0x0000000020000000
    ManageEmojisAndStickers = 1073741824, // 0x0000000040000000
    UseApplicationCommands = 2147483648, // 0x0000000080000000
    RequestToSpeak = 4294967296, // 0x0000000100000000
    ManageEvents = 8589934592, // 0x0000000200000000
    ManageThreads = 17179869184, // 0x0000000400000000
    CreatePublicThreads = 34359738368, // 0x0000000800000000
    CreatePrivateThreads = 68719476736, // 0x0000001000000000
    UseExternalStickers = 137438953472, // 0x0000002000000000
    SendMessagesInThreads = 274877906944, // 0x0000004000000000
    StartEmbeddedActivities = 549755813888, // 0x0000008000000000
    ModerateMembers = 1099511627776, // 0x0000010000000000
}