// using System.Globalization;
// using NadekoBot.Db.Models;
// using NadekoBot.Modules.Administration;
//
// namespace NadekoBot.Modules.Gambling;
//
// public readonly record struct BigWinNotifyModel(
//     string GuildName,
//     ulong ChannelId,
//     ulong UserId,
//     string Amount)
//     : INotifyModel<BigWinNotifyModel>
// {
//     public const string PH_USER = "user";
//     public const string PH_GUILD = "server";
//     public const string PH_AMOUNT = "amount";
//
//     public static string KeyName
//         => "notify.bigwin";
//
//     public static NotifyType NotifyType
//         => NotifyType.BigWin;
//
//     public static bool SupportsOriginTarget
//         => true;
//
//     public static IReadOnlyList<NotifyModelPlaceholderData<BigWinNotifyModel>> GetReplacements()
//         =>
//         [
//             new(PH_USER, static (data, g) => g.GetUser(data.UserId)?.ToString() ?? data.UserId.ToString()),
//             new(PH_AMOUNT, static (data, g) => data.Amount),
//             new(PH_GUILD, static (data, g) => data.GuildName)
//         ];
//
//     public bool TryGetChannelId(out ulong channelId)
//     {
//         channelId = ChannelId;
//         return true;
//     }
//
//     public bool TryGetUserId(out ulong userId)
//     {
//         userId = UserId;
//         return true;
//     }
// }