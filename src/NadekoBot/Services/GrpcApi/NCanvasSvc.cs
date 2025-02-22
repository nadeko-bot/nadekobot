using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Games;
using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.GrpcApi;

public class NCanvasSvc : GrpcNCanvas.GrpcNCanvasBase, IGrpcSvc, INService
{
    private readonly INCanvasService _nCanvas;
    private readonly DiscordSocketClient _client;

    public NCanvasSvc(INCanvasService nCanvas, DiscordSocketClient client)
    {
        _nCanvas = nCanvas;
        _client = client;
    }

    public ServerServiceDefinition Bind()
        => GrpcNCanvas.BindService(this);

    [GrpcNoAuthRequired]
    public override async Task<CanvasReply> GetCanvas(Empty request, ServerCallContext context)
    {
        var pixels = await _nCanvas.GetCanvas();
        var reply = new CanvasReply()
        {
            Width = _nCanvas.GetWidth(),
            Height = _nCanvas.GetHeight()
        };
        reply.Pixels.AddRange(pixels);
        return reply;
    }

    [GrpcNoAuthRequired]
    public override async Task<GetPixelReply> GetPixel(GetPixelRequest request, ServerCallContext context)
    {
        var pixel = await _nCanvas.GetPixel(request.X, request.Y);
        if (pixel is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Pixel not found"));

        var reply = MapPixelToGrpcPixel(pixel);
        return reply;
    }

    private GetPixelReply MapPixelToGrpcPixel(NCPixel pixel)
    {
        var reply = new GetPixelReply
        {
            Color = "#" + new Rgba32(pixel.Color).ToHex(),
            PackedColor = pixel.Color,
            Position = new kwum(pixel.Position).ToString(),
            PositionX = pixel.Position % _nCanvas.GetWidth(),
            PositionY = pixel.Position / _nCanvas.GetWidth(),
            // Owner = await ((IDiscordClient)_client).GetUserAsync(pixel.OwnerId)?.ToString() ?? string.Empty,
            // OwnerId = pixel.OwnerId.ToString(),
            Price = pixel.Price,
            Text = pixel.Text
        };
        return reply;
    }

    [GrpcNoAuthRequired]
    public override async Task<SetPixelReply> SetPixel(SetPixelRequest request, ServerCallContext context)
    {
        if (!kwum.TryParse(request.Position, out var pos))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Position is invalid"));

        if (!Rgba32.TryParseHex(request.Color, out var clr))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Color is invalid"));

        var userId = context.RequestHeaders.GetUserId();
        var result = await _nCanvas.SetPixel(pos, clr.PackedValue, request.Text, userId, request.Price);
        var reply = new SetPixelReply()
        {
            Success = result == SetPixelResult.Success,
            Error = result switch
            {
                SetPixelResult.Success => string.Empty,
                SetPixelResult.InsufficientPayment => "You have to pay equal or more than the price.",
                SetPixelResult.NotEnoughMoney => "You don't have enough currency. ",
                SetPixelResult.InvalidInput =>
                    $"Invalid input. Position has to be >= 0 and < {_nCanvas.GetWidth()}x{_nCanvas.GetHeight()}",
                _ => throw new ArgumentOutOfRangeException()
            }
        };

        var pixel = await _nCanvas.GetPixel(pos);
        if (pixel is not null)
            reply.Pixel = MapPixelToGrpcPixel(pixel);

        return reply;
    }
}