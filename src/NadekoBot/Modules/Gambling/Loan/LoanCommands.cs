// namespace NadekoBot.Modules.Gambling;
// public sealed class Loan
// {
//     public int Id { get; set; }
//     public ulong LenderId { get; set; }
//     public string LenderName { get; set; }
//     public ulong BorrowerId { get; set; }
//     public string BorrowerName { get; set; }
//     public long Amount { get; set; }
//     public decimal Interest { get; set; }
//     public DateTime DueDate { get; set; }
//     public bool Repaid { get; set; }
// }
//
// public sealed class LoanService : INService
// {
//     public async Task<IReadOnlyList<Loan>> GetLoans(ulong userId)
//     {
//     }
//
//     public async Task<object> RepayAsync(object loandId)
//     {
//     }
// }
//
// public partial class Gambling
// {
//     public partial class LoanCommands : NadekoModule<LoanService>
//     {
//         [Cmd]
//         public async Task Loan(
//             IUser lender,
//             long amount,
//             decimal interest = 0,
//             TimeSpan dueIn = default)
//         {
//             var eb = CreateEmbed()
//                             .WithOkColor()
//                             .WithDescription("User 0 Requests a loan from User {1}")
//                             .AddField("Amount", amount, true)
//                             .AddField("Interest", (interest * 0.01m).ToString("P2"), true);
//         }
//
//         public Task Loans()
//             => Loans(ctx.User);
//
//         public async Task Loans([Leftover] IUser user)
//         {
//             var loans = await _service.GetLoans(user.Id);
//
//             Response()
//                 .Paginated()
//                 .PageItems(loans)
//                 .Page((items, page) =>
//                 {
//                     var eb = CreateEmbed()
//                                     .WithOkColor()
//                                     .WithDescription("Current Loans");
//
//                     foreach (var item in items)
//                     {
//                         eb.AddField(new kwum(item.id).ToString(),
//                             $"""
//                              To: {item.LenderName}
//                              Amount: {}
//                              """,
//                             true);
//                     }
//
//                     return eb;
//                 });
//         }
//
//         [Cmd]
//         public async Task Repay(kwum loanId)
//         {
//             var res = await _service.RepayAsync(loandId);
//
//             if (res.TryPickT0(out var _, out var err))
//             {
//             }
//             else
//             {
//                 var errStr = err.Match(
//                     _ => "Not enough funds",
//                     _ => "Loan not found");
//
//                 await Response().Error(errStr).SendAsync();
//             }
//         }
//     }
// }