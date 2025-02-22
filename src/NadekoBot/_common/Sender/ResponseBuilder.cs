using System.Collections.ObjectModel;

namespace NadekoBot.Extensions;

public sealed partial class ResponseBuilder
{
    private ICommandContext? ctx;
    private IMessageChannel? channel;
    private IUser? user;
    private IUserMessage? msg;
    
    private string? plainText;
    private IReadOnlyCollection<EmbedBuilder>? embeds;
    private bool sanitizeMentions = true;
    private LocStr? locTxt;
    private object[] locParams = [];
    private bool shouldReply = true;
    private readonly IBotStrings _bs;
    private readonly IMessageSenderService _sender;
    private EmbedBuilder? embedBuilder;
    private NadekoInteractionBase? inter;
    private Stream? fileStream;
    private string? fileName;
    private EmbedColor color = EmbedColor.Ok;
    private LocStr? embedLocDesc;

    public DiscordSocketClient Client { get; set; }

    public ResponseBuilder(IBotStrings bs, IMessageSenderService sender, DiscordSocketClient client)
    {
        _bs = bs;
        _sender = sender;
        Client = client;
    }


    private MessageReference? CreateMessageReference(IMessageChannel targetChannel)
    {
        if (!shouldReply)
            return null;

        var replyTo = msg ?? ctx?.Message;
        // what message are we replying to
        if (replyTo is null)
            return null;

        // we have to have a channel where we are sending the message in order to know whether we can reply to it
        if (targetChannel.Id != replyTo.Channel.Id)
            return null;

        return new(replyTo.Id,
            replyTo.Channel.Id,
            (replyTo.Channel as ITextChannel)?.GuildId,
            failIfNotExists: false);
    }

    public async Task<ResponseMessageModel> BuildAsync(bool ephemeral)
    {
        var targetChannel = await InternalResolveChannel() ?? throw new ArgumentNullException(nameof(channel));
        var msgReference = CreateMessageReference(targetChannel);

        var txt = GetText(locTxt, targetChannel);

        if (embedLocDesc is LocStr ls)
        {
            InternalCreateEmbed(null, GetText(ls, targetChannel));
        }

        if (embedBuilder is not null)
            PaintEmbedInternal(embedBuilder);

        var finalEmbed = embedBuilder?.Build();


        var buildModel = new ResponseMessageModel()
        {
            TargetChannel = targetChannel,
            MessageReference = msgReference,
            Text = txt,
            User = user ?? ctx?.User,
            Embed = finalEmbed,
            Embeds = embeds?.Map(x => x.Build()),
            SanitizeMentions = sanitizeMentions ? new(AllowedMentionTypes.Users) : AllowedMentions.All,
            Ephemeral = ephemeral,
            Interaction = inter
        };

        return buildModel;
    }

    public async Task<IUserMessage> SendAsync(bool ephemeral = false)
    {
        var model = await BuildAsync(ephemeral);
        var sentMsg = await SendAsync(model);


        return sentMsg;
    }

    public async Task<IUserMessage> SendAsync(ResponseMessageModel model)
    {
        IUserMessage sentMsg;
        if (fileStream is Stream stream)
        {
            sentMsg = await model.TargetChannel.SendFileAsync(stream,
                filename: fileName,
                model.Text,
                embed: model.Embed,
                components: inter?.CreateComponent(),
                allowedMentions: model.SanitizeMentions,
                messageReference: model.MessageReference);
        }
        else
        {
            sentMsg = await model.TargetChannel.SendMessageAsync(
                model.Text,
                embed: model.Embed,
                embeds: model.Embeds,
                components: inter?.CreateComponent(),
                allowedMentions: model.SanitizeMentions,
                messageReference: model.MessageReference);
        }

        if (model.Interaction is not null)
        {
            await model.Interaction.RunAsync(sentMsg);
        }

        return sentMsg;
    }

    private EmbedBuilder PaintEmbedInternal(EmbedBuilder eb)
        => color switch
        {
            EmbedColor.Ok => eb.WithOkColor(),
            EmbedColor.Pending => eb.WithPendingColor(),
            EmbedColor.Error => eb.WithErrorColor(),
            _ => throw new NotSupportedException()
        };

    private ulong? InternalResolveGuildId(IMessageChannel? targetChannel)
        => ctx?.Guild?.Id ?? (targetChannel as ITextChannel)?.GuildId;

    private async Task<IMessageChannel?> InternalResolveChannel()
    {
        if (user is not null)
        {
            var ch = await user.CreateDMChannelAsync();

            if (ch is not null)
            {
                return ch;
            }
        }

        return channel ?? ctx?.Channel ?? msg?.Channel;
    }

    private string? GetText(LocStr? locStr, IMessageChannel targetChannel)
    {
        var guildId = InternalResolveGuildId(targetChannel);
        return locStr is LocStr ls ? _bs.GetText(ls.Key, guildId, locParams) : plainText;
    }

    private string GetText(LocStr locStr, IMessageChannel targetChannel)
    {
        var guildId = InternalResolveGuildId(targetChannel);
        return _bs.GetText(locStr.Key, guildId, locStr.Params);
    }

    public ResponseBuilder Text(LocStr str)
    {
        locTxt = str;
        return this;
    }

    public ResponseBuilder Text(SmartText text)
    {
        if (text is SmartPlainText spt)
            plainText = spt.Text;
        else if (text is SmartEmbedText set)
        {
            plainText = set.PlainText ?? plainText;
            embedBuilder = set.GetEmbed();
        }
        else if (text is SmartEmbedTextArray ser)
        {
            plainText = ser.Content ?? plainText;
            embeds = ser.GetEmbedBuilders();
        }

        return this;
    }

    private void InternalCreateEmbed(
        string? title,
        string text,
        string? url = null,
        string? footer = null)
    {
        var eb = _sender.CreateEmbed(ctx?.Guild?.Id ??  (channel as ITextChannel)?.GuildId ?? (user as IGuildUser)?.GuildId)
            .WithDescription(text);

        if (!string.IsNullOrWhiteSpace(title))
            eb.WithTitle(title);

        if (!string.IsNullOrWhiteSpace(url))
            eb = eb.WithUrl(url);

        if (!string.IsNullOrWhiteSpace(footer))
            eb = eb.WithFooter(footer);

        embedBuilder = eb;
    }

    public ResponseBuilder Confirm(
        string? title,
        string text,
        string? url = null,
        string? footer = null)
    {
        InternalCreateEmbed(title, text, url, footer);
        color = EmbedColor.Ok;
        return this;
    }

    public ResponseBuilder Error(
        string? title,
        string text,
        string? url = null,
        string? footer = null)
    {
        InternalCreateEmbed(title, text, url, footer);
        color = EmbedColor.Error;
        return this;
    }

    public ResponseBuilder Pending(
        string? title,
        string text,
        string? url = null,
        string? footer = null)
    {
        InternalCreateEmbed(title, text, url, footer);
        color = EmbedColor.Pending;
        return this;
    }

    public ResponseBuilder Confirm(string text)
    {
        InternalCreateEmbed(null, text);
        color = EmbedColor.Ok;
        return this;
    }

    public ResponseBuilder Confirm(LocStr str)
    {
        embedLocDesc = str;
        color = EmbedColor.Ok;
        return this;
    }

    public ResponseBuilder Pending(string text)
    {
        InternalCreateEmbed(null, text);
        color = EmbedColor.Pending;
        return this;
    }

    public ResponseBuilder Pending(LocStr str)
    {
        embedLocDesc = str;
        color = EmbedColor.Pending;
        return this;
    }

    public ResponseBuilder Error(string text)
    {
        InternalCreateEmbed(null, text);
        color = EmbedColor.Error;
        return this;
    }

    public ResponseBuilder Error(LocStr str)
    {
        embedLocDesc = str;
        color = EmbedColor.Error;
        return this;
    }

    public ResponseBuilder UserBasedMentions(IGuildUser? permUser = null)
    {
        sanitizeMentions = !((InternalResolveUser() as IGuildUser ?? permUser)?.GuildPermissions.MentionEveryone ?? false);
        return this;
    }

    private IUser? InternalResolveUser()
        => ctx?.User ?? user ?? msg?.Author;

    public ResponseBuilder Embed(EmbedBuilder eb)
    {
        embedBuilder = eb;
        return this;
    }

    public ResponseBuilder Channel(IMessageChannel ch)
    {
        channel = ch;
        return this;
    }

    public ResponseBuilder Sanitize(bool shouldSantize = true)
    {
        sanitizeMentions = shouldSantize;
        return this;
    }

    public ResponseBuilder Context(ICommandContext context)
    {
        ctx = context;
        return this;
    }

    public ResponseBuilder Message(IUserMessage message)
    {
        msg = message;
        return this;
    }

    public ResponseBuilder User(IUser usr)
    {
        user = usr;
        return this;
    }

    public ResponseBuilder NoReply()
    {
        shouldReply = false;
        return this;
    }

    public ResponseBuilder Interaction(NadekoInteractionBase? interaction)
    {
        inter = interaction;
        return this;
    }

    public ResponseBuilder Embeds(IReadOnlyCollection<EmbedBuilder> inputEmbeds)
    {
        embeds = inputEmbeds;
        return this;
    }

    public ResponseBuilder File(Stream stream, string name)
    {
        fileStream = stream;
        fileName = name;
        return this;
    }

    public PaginatedResponseBuilder Paginated()
        => new(this);
}

public class PaginatedResponseBuilder
{
    protected readonly ResponseBuilder _builder;

    public PaginatedResponseBuilder(ResponseBuilder builder)
    {
        _builder = builder;
    }

    public SourcedPaginatedResponseBuilder<T> Items<T>(IReadOnlyCollection<T> items)
        => new SourcedPaginatedResponseBuilder<T>(_builder)
            .Items(items);

    public SourcedPaginatedResponseBuilder<T> PageItems<T>(Func<int, Task<IReadOnlyCollection<T>>> items)
        => new SourcedPaginatedResponseBuilder<T>(_builder)
            .PageItems(items);
}

public sealed class SourcedPaginatedResponseBuilder<T> : PaginatedResponseBuilder
{
    private IReadOnlyCollection<T>? items;

    public Func<IReadOnlyList<T>, int, Task<EmbedBuilder>> PageFunc { get; private set; } = static delegate
    {
        return Task.FromResult<EmbedBuilder>(new());
    };

    public Func<int, Task<IReadOnlyCollection<T>>> ItemsFunc { get; set; } = static delegate
    {
        return Task.FromResult<IReadOnlyCollection<T>>(ReadOnlyCollection<T>.Empty);
    };

    public Func<int, Task<NadekoInteractionBase>>? InteractionFunc { get; private set; }

    public int? Elems { get; private set; } = 1;
    public int ItemsPerPage { get; private set; } = 9;
    public bool AddPaginatedFooter { get; private set; } = true;
    public bool IsEphemeral { get; private set; }

    public int InitialPage { get; set; }

    public SourcedPaginatedResponseBuilder(ResponseBuilder builder)
        : base(builder)
    {
    }

    public SourcedPaginatedResponseBuilder<T> Items(IReadOnlyCollection<T> col)
    {
        items = col;
        Elems = col.Count;
        ItemsFunc = (i) => Task.FromResult(items.Skip(i * ItemsPerPage).Take(ItemsPerPage).ToArray() as IReadOnlyCollection<T>);
        return this;
    }

    public SourcedPaginatedResponseBuilder<T> TotalElements(int i)
    {
        Elems = i;
        return this;
    }

    public SourcedPaginatedResponseBuilder<T> PageItems(Func<int, Task<IReadOnlyCollection<T>>> func)
    {
        Elems = null;
        ItemsFunc = func;
        return this;
    }


    public SourcedPaginatedResponseBuilder<T> PageSize(int i)
    {
        ItemsPerPage = i;
        return this;
    }

    public SourcedPaginatedResponseBuilder<T> CurrentPage(int i)
    {
        InitialPage = i;
        return this;
    }


    public SourcedPaginatedResponseBuilder<T> Page(Func<IReadOnlyList<T>, int, EmbedBuilder> pageFunc)
    {
        PageFunc = (xs, x) => Task.FromResult(pageFunc(xs, x));
        return this;
    }

    public SourcedPaginatedResponseBuilder<T> Page(Func<IReadOnlyList<T>, int, Task<EmbedBuilder>> pageFunc)
    {
        PageFunc = pageFunc;
        return this;
    }

    public SourcedPaginatedResponseBuilder<T> AddFooter(bool addFooter = true)
    {
        AddPaginatedFooter = addFooter;
        return this;
    }

    public SourcedPaginatedResponseBuilder<T> Ephemeral()
    {
        IsEphemeral = true;
        return this;
    }


    public Task SendAsync()
    {
        var paginationSender = new ResponseBuilder.PaginationSender<T>(
            this,
            _builder);

        return paginationSender.SendAsync(IsEphemeral);
    }

    public SourcedPaginatedResponseBuilder<T> Interaction(Func<int, Task<NadekoInteractionBase>> func)
    {
        InteractionFunc = func; //async (i) => await func(i);
        return this;
    }

    public SourcedPaginatedResponseBuilder<T> Interaction(NadekoInteractionBase inter)
    {
        InteractionFunc = _ => Task.FromResult(inter);
        return this;
    }
}