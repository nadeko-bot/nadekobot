using Grpc.Core;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Xp;
using NadekoBot.Modules.Xp.Services;

namespace NadekoBot.GrpcApi;

public class XpShopSvc(XpService xp, XpConfigService xpConfig) : GrpcXpShop.GrpcXpShopBase, IGrpcSvc, INService
{
    public ServerServiceDefinition Bind()
        => GrpcXpShop.BindService(this);

    public override async Task<BuyShopItemReply> BuyShopItem(BuyShopItemRequest request, ServerCallContext context)
    {
        var result = await xp.BuyShopItemAsync(request.UserId, (XpShopItemType)request.ItemType, request.UniqueName);

        var res = new BuyShopItemReply();

        if (result == BuyResult.Success)
        {
            res.Success = true;
            return res;
        }

        res.Error = result switch
        {
            BuyResult.AlreadyOwned => BuyShopItemError.AlreadyOwned,
            BuyResult.InsufficientFunds => BuyShopItemError.NotEnough,
            _ => BuyShopItemError.Unknown
        };

        return res;
    }

    public override async Task<UseShopItemReply> UseShopItem(UseShopItemRequest request, ServerCallContext context)
    {
        var result = await xp.UseShopItemAsync(request.UserId, (XpShopItemType)request.ItemType, request.UniqueName);

        var res = new UseShopItemReply
        {
            Success = result
        };

        return res;
    }

    public override async Task<GetShopItemsReply> GetShopItems(GetShopItemsRequest request, ServerCallContext context)
    {
        var bgsTask = Task.Run(async () => await xp.GetShopBgs());
        var frsTask = Task.Run(async () => await xp.GetShopFrames());

        var bgs = await bgsTask.Fmap(x => x?.Map(y => MapItemToGrpcItem(y.Value, y.Key)) ?? []);
        var frs = await frsTask.Fmap(z => z?.Map(y => MapItemToGrpcItem(y.Value, y.Key)) ?? []);

        var res = new GetShopItemsReply();

        res.Bgs.AddRange(bgs);
        res.Frames.AddRange(frs);

        return res;

        static XpShopItem MapItemToGrpcItem(XpConfig.ShopItemInfo item, string uniqueName)
        {
            return new XpShopItem()
            {
                Name = item.Name,
                Price = item.Price,
                Description = item.Desc,
                FullUrl = item.Url,
                PreviewUrl = item.Preview,
            };
        }
    }

    public override async Task<AddXpShopItemReply> AddXpShopItem(AddXpShopItemRequest request,
        ServerCallContext context)
    {
        var result = await xpConfig.AddItemAsync(request.UniqueName, (XpShopItemType)request.ItemType,
            new XpConfig.ShopItemInfo()
            {
                Name = request.Item.Name,
                Price = 3000,
                Desc = request.Item.Description,
                Url = request.Item.FullUrl,
                Preview = request.Item.PreviewUrl,
            });

        return new AddXpShopItemReply()
        {
            Success = result,
        };
    }
}