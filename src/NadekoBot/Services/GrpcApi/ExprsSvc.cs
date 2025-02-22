using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using NadekoBot.Db.Models;
using NadekoBot.Modules.NadekoExpressions;
using NadekoBot.Modules.Utility;

namespace NadekoBot.GrpcApi;

public class ExprsSvc : GrpcExprs.GrpcExprsBase, IGrpcSvc, INService
{
    private readonly NadekoExpressionsService _svc;
    private readonly IQuoteService _qs;
    private readonly DiscordSocketClient _client;

    public ExprsSvc(NadekoExpressionsService svc, IQuoteService qs, DiscordSocketClient client)
    {
        _svc = svc;
        _qs = qs;
        _client = client;
    }

    public ServerServiceDefinition Bind()
        => GrpcExprs.BindService(this);
    
    public override async Task<AddExprReply> AddExpr(AddExprRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Expr.Trigger) || string.IsNullOrWhiteSpace(request.Expr.Response))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Trigger and response are required"));

        NadekoExpression expr;
        if (!string.IsNullOrWhiteSpace(request.Expr.Id))
        {
            expr = await _svc.EditAsync(request.GuildId,
                new kwum(request.Expr.Id),
                request.Expr.Response,
                request.Expr.Ca,
                request.Expr.Ad,
                request.Expr.Dm);
        }
        else
        {
            expr = await _svc.AddAsync(request.GuildId,
                request.Expr.Trigger,
                request.Expr.Response,
                request.Expr.Ca,
                request.Expr.Ad,
                request.Expr.Dm);
        }


        return new AddExprReply()
        {
            Id = new kwum(expr.Id).ToString(),
            Success = true,
        };
    }

    public override async Task<GetExprsReply> GetExprs(GetExprsRequest request, ServerCallContext context)
    {
        var (exprs, totalCount) = await _svc.FindExpressionsAsync(request.GuildId, request.Query, request.Page);

        var reply = new GetExprsReply();
        reply.TotalCount = totalCount;
        reply.Expressions.AddRange(exprs.Select(x => new ExprDto()
        {
            Ad = x.AutoDeleteTrigger,
            At = x.AllowTarget,
            Ca = x.ContainsAnywhere,
            Dm = x.DmResponse,
            Response = x.Response,
            Id = new kwum(x.Id).ToString(),
            Trigger = x.Trigger,
        }));

        return reply;
    }

    public override async Task<Empty> DeleteExpr(DeleteExprRequest request, ServerCallContext context)
    {
        if (kwum.TryParse(request.Id, out var id))
            await _svc.DeleteAsync(request.GuildId, id);

        return new Empty();
    }

    public override async Task<GetQuotesReply> GetQuotes(GetQuotesRequest request, ServerCallContext context)
    {
        if (request.Page < 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Page must be >= 0"));

        var (quotes, totalCount) = await _qs.FindQuotesAsync(request.GuildId, request.Query, request.Page);

        var reply = new GetQuotesReply();
        reply.TotalCount = totalCount;
        reply.Quotes.AddRange(quotes.Select(x => new QuoteDto()
        {
            Id = new kwum(x.Id).ToString(),
            Trigger = x.Keyword,
            Response = x.Text,
            AuthorId = x.AuthorId,
            AuthorName = x.AuthorName
        }));

        return reply;
    }

    public override async Task<AddQuoteReply> AddQuote(AddQuoteRequest request, ServerCallContext context)
    {
        var userId = context.RequestHeaders.GetUserId();

        if (string.IsNullOrWhiteSpace(request.Quote.Trigger) || string.IsNullOrWhiteSpace(request.Quote.Response))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Trigger and response are required"));

        if (string.IsNullOrWhiteSpace(request.Quote.Id))
        {
            var q = await _qs.AddQuoteAsync(request.GuildId,
                userId,
                (await _client.GetUserAsync(userId))?.Username ?? userId.ToString(),
                request.Quote.Trigger,
                request.Quote.Response);

            return new()
            {
                Id = new kwum(q.Id).ToString()
            };
        }

        if (!kwum.TryParse(request.Quote.Id, out var qid))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid quote id"));

        await _qs.EditQuoteAsync(
            request.GuildId,
            new kwum(request.Quote.Id),
            request.Quote.Trigger,
            request.Quote.Response);

        return new()
        {
            Id = new kwum(qid).ToString()
        };
    }


    public override async Task<Empty> DeleteQuote(DeleteQuoteRequest request, ServerCallContext context)
    {
        await _qs.DeleteQuoteAsync(request.GuildId, context.RequestHeaders.GetUserId(), true, new kwum(request.Id));
        return new Empty();
    }
}