using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Gambling.Bank;
using NadekoBot.Modules.NadekoExpressions;
using NadekoBot.Modules.Utility;

namespace NadekoBot.GrpcApi;

public class FinSvc : GrpcFin.GrpcFinBase, IGrpcSvc, INService
{
    private readonly ICurrencyService _cs;
    private readonly IBankService _bank;

    public FinSvc(ICurrencyService cs, IBankService bank)
    {
        _cs = cs;
        _bank = bank;
    }

    public ServerServiceDefinition Bind()
        => GrpcFin.BindService(this);

    [GrpcNoAuthRequired]
    public override async Task<DepositReply> Deposit(DepositRequest request, ServerCallContext context)
    {
        if (request.Amount <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Amount must be greater than 0"));

        var succ = await _bank.DepositAsync(request.UserId, request.Amount);

        return new DepositReply
        {
            Success = succ
        };
    }

    [GrpcNoAuthRequired]
    public override async Task<WithdrawReply> Withdraw(WithdrawRequest request, ServerCallContext context)
    {
        if (request.Amount <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Amount must be greater than 0"));

        var succ = await _bank.WithdrawAsync(request.UserId, request.Amount);

        return new WithdrawReply
        {
            Success = succ
        };
    }

    [GrpcNoAuthRequired]
    public override async Task<GetHoldingsReply> GetHoldings(GetHoldingsRequest request, ServerCallContext context)
    {
        return new GetHoldingsReply
        {
            Bank = await _bank.GetBalanceAsync(request.UserId),
            Cash = await _cs.GetBalanceAsync(request.UserId)
        };
    }

    [GrpcNoAuthRequired]
    public override async Task<GetTransactionsReply> GetTransactions(
        GetTransactionsRequest request,
        ServerCallContext context)
    {
        if (request.Page < 1)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Page must be greater than 0"));
        
        var trs = await _cs.GetTransactionsAsync(request.UserId, request.Page - 1);

        var reply = new GetTransactionsReply
        {
            Total = await _cs.GetTransactionsCountAsync(request.UserId)
        };

        reply.Transactions.AddRange(trs.Select(x => new TransactionReply()
        {
            Id = new kwum(x.Id).ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Amount = x.Amount,
            Extra = x.Extra ?? string.Empty,
            Note = x.Note ?? string.Empty,
            Type = x.Type ?? string.Empty,
        }));

        return reply;
    }
}