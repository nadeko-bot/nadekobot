namespace NadekoBot.Extensions;

public partial class ResponseBuilder
{
    public class PaginationSender<T>
    {
        private const string BUTTON_LEFT = "BUTTON_LEFT";
        private const string BUTTON_RIGHT = "BUTTON_RIGHT";

        private readonly SourcedPaginatedResponseBuilder<T> _paginationBuilder;
        private readonly ResponseBuilder _builder;
        private readonly DiscordSocketClient _client;
        private int currentPage;
        
        private NadekoButtonInteractionHandler? left;
        private NadekoButtonInteractionHandler? right;
        private NadekoInteractionBase? extra;

        public PaginationSender(
            SourcedPaginatedResponseBuilder<T> paginationBuilder,
            ResponseBuilder builder)
        {
            _paginationBuilder = paginationBuilder;
            _builder = builder;

            _client = builder.Client;
            currentPage = paginationBuilder.InitialPage;
        }

        public async Task SendAsync(bool ephemeral = false)
        {
            var lastPage = (_paginationBuilder.Elems - 1)
                           / _paginationBuilder.ItemsPerPage;

            var items = (await _paginationBuilder.ItemsFunc(currentPage)).ToArray();
            var embed = await _paginationBuilder.PageFunc(items, currentPage);

            if (_paginationBuilder.AddPaginatedFooter)
                embed.AddPaginatedFooter(currentPage, lastPage);

            NadekoInteractionBase? maybeInter = null;

            var model = await _builder.BuildAsync(ephemeral);

            async Task<(NadekoButtonInteractionHandler left, NadekoInteractionBase? extra, NadekoButtonInteractionHandler right)>
                GetInteractions()
            {
                var leftButton = new ButtonBuilder()
                                 .WithStyle(ButtonStyle.Primary)
                                 .WithCustomId(BUTTON_LEFT)
                                 .WithEmote(InteractionHelpers.ArrowLeft)
                                 .WithDisabled(lastPage == 0 || currentPage <= 0);

                var leftBtnInter = new NadekoButtonInteractionHandler(_client,
                    model.User?.Id ?? 0,
                    leftButton,
                    (smc) =>
                    {
                        try
                        {
                            if (currentPage > 0)
                                currentPage--;

                            _ = UpdatePageAsync(smc);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Error in pagination: {ErrorMessage}", ex.Message);
                        }

                        return Task.CompletedTask;
                    },
                    true,
                    singleUse: false,
                    clearAfter: false);

                if (_paginationBuilder.InteractionFunc is not null)
                {
                    maybeInter = await _paginationBuilder.InteractionFunc(currentPage);
                }

                var rightButton = new ButtonBuilder()
                                  .WithStyle(ButtonStyle.Primary)
                                  .WithCustomId(BUTTON_RIGHT)
                                  .WithEmote(InteractionHelpers.ArrowRight)
                                  .WithDisabled(lastPage == 0 || currentPage >= lastPage);

                var rightBtnInter = new NadekoButtonInteractionHandler(_client,
                    model.User?.Id ?? 0,
                    rightButton,
                    (smc) =>
                    {
                        try
                        {
                            if (currentPage >= lastPage)
                                return Task.CompletedTask;

                            currentPage++;

                            _ = UpdatePageAsync(smc);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Error in pagination: {ErrorMessage}", ex.Message);
                        }

                        return Task.CompletedTask;
                    },
                    true,
                    singleUse: false,
                    clearAfter: false);

                return (leftBtnInter, maybeInter, rightBtnInter);
            }
            
            (left, extra, right) = await GetInteractions();

            async Task UpdatePageAsync(SocketMessageComponent smc)
            {
                var pageItems = (await _paginationBuilder.ItemsFunc(currentPage)).ToArray();
                var toSend = await _paginationBuilder.PageFunc(pageItems, currentPage);
                if (_paginationBuilder.AddPaginatedFooter)
                    toSend.AddPaginatedFooter(currentPage, lastPage);

                left?.SetCompleted();
                right?.SetCompleted();
                extra?.SetCompleted();
                (left, extra, right) = (await GetInteractions());

                var cb = new ComponentBuilder();
                left.AddTo(cb);
                right.AddTo(cb);
                extra?.AddTo(cb);
                
                await smc.ModifyOriginalResponseAsync(x =>
                {
                    x.Embed = toSend.Build();
                    x.Components = cb.Build();
                });
                
                await Task.WhenAll(left.RunAsync(smc.Message), extra?.RunAsync(smc.Message) ?? Task.CompletedTask, right.RunAsync(smc.Message));
            }


            var cb = new ComponentBuilder();
            left.AddTo(cb);
            right.AddTo(cb);
            extra?.AddTo(cb);

            var msg = await model.TargetChannel
                                 .SendMessageAsync(model.Text,
                                     embed: embed.Build(),
                                     components: cb.Build(),
                                     allowedMentions: model.SanitizeMentions,
                                     messageReference: model.MessageReference);

            if (lastPage == 0 && _paginationBuilder.InteractionFunc is null)
                return;
            
            await Task.WhenAll(left.RunAsync(msg), extra?.RunAsync(msg) ?? Task.CompletedTask, right.RunAsync(msg));

            await Task.Delay(30_000);
            
            await msg.ModifyAsync(mp => mp.Components = new ComponentBuilder().Build());
        }
    }
}