using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Serilog.Events;

namespace NadekoBot.Modules.Utility.AiAgent;

// One worker sends and edits the steps message, so edits never overlap.
public sealed class AgentActivityReporter : IAgentProgress
{
    private const int MAX_LINES = 64;
    public static readonly TimeSpan ShowDelay = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan _minEditInterval = TimeSpan.FromSeconds(2);

    private readonly IMessageSenderService _sender;
    private readonly IBotStrings _strings;
    private readonly AgentLimiter _limiter;
    private readonly ITextChannel _channel;
    private readonly MessageReference? _reference;
    private readonly ulong _guildId;
    private readonly string _prefix;
    private readonly bool _useEmbed;
    private readonly long _started = Stopwatch.GetTimestamp();

    private readonly Lock _lock = new();
    private readonly List<string> _lines = new(16);
    private int _dropped;
    private int _steps;

    private readonly Channel<byte> _signal = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private IUserMessage? _message;
    private bool _disabled;
    private bool _completed;

    public AgentActivityReporter(
        IMessageSenderService sender,
        IBotStrings strings,
        AgentLimiter limiter,
        ITextChannel channel,
        IUserMessage? replyTo,
        string prefix,
        bool useEmbed)
    {
        _sender = sender;
        _strings = strings;
        _limiter = limiter;
        _channel = channel;
        _guildId = channel.GuildId;
        _prefix = prefix;
        _useEmbed = useEmbed;
        _reference = replyTo is null
            ? null
            : new MessageReference(replyTo.Id, channel.Id, channel.GuildId, failIfNotExists: false);
    }

    // The message was in the channel at some point of the request.
    public bool IsShown { get; private set; }

    // Discord answered 403: the bot lost a permission in the channel, so a send or a delete fails too.
    public bool IsRefused { get; private set; }

    public ulong? MessageId
        => _message?.Id;

    public void OnNote(string text)
        => AddLineInternal(AgentActivityFormatter.FormatNote(text), false);

    public void OnToolCall(string name, JsonElement args, bool failed)
    {
        if (AgentActivityFormatter.FormatToolCall(name, args, failed, _prefix, _strings, _guildId) is { } line)
            AddLineInternal(line, true);
    }

    // After it, the message changes only through TryReplaceAsync or RemoveAsync.
    public async Task StopAsync()
    {
        if (_completed)
            return;

        _completed = true;
        _signal.Writer.TryComplete();
        await _stop.CancelAsync();

        // A failed worker must not keep RemoveAsync from deleting the message.
        if (_worker is not null)
        {
            try
            {
                await _worker;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AI agent activity worker failed in channel {ChannelId}", _channel.Id);
            }
        }

        _stop.Dispose();
    }

    public async Task<ulong?> TryReplaceAsync(string? text, Embed? embed, MessageComponent? components)
    {
        await StopAsync();
        if (_message is not { } message || _disabled || BotTimedOutInternal())
            return null;

        try
        {
            await message.ModifyAsync(p =>
            {
                p.Content = text ?? "";
                Embed[] embeds = embed is null ? [] : [embed];
                p.Embeds = embeds;
                p.AllowedMentions = AiAgentService.NoPing;
                if (components is not null)
                    p.Components = components;
            });
            _message = null;
            return message.Id;
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            OnRefusedInternal(ex);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not put the AI agent answer into the activity message in channel {ChannelId}", _channel.Id);
        }

        return null;
    }

    public async Task RemoveAsync()
    {
        await StopAsync();
        if (_message is not { } message || IsRefused || BotTimedOutInternal())
            return;

        // The message stays known until the delete worked, so the second call can try again.
        try
        {
            await message.DeleteAsync();
            _message = null;
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            _message = null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not delete the AI agent activity message in channel {ChannelId}", _channel.Id);
        }
    }

    private void AddLineInternal(string line, bool isStep)
    {
        if (_completed)
            return;

        lock (_lock)
        {
            if (_lines.Count == MAX_LINES)
            {
                _lines.RemoveAt(0);
                _dropped++;
            }

            _lines.Add(line);
            if (isStep)
                _steps++;
        }

        // A note alone does not create the message, because a hidden tool such as ask_user may follow it.
        if (_steps == 0)
            return;

        _worker ??= Task.Run(WorkerInternalAsync);
        _signal.Writer.TryWrite(0);
    }

    private async Task WorkerInternalAsync()
    {
        var lastPush = 0L;
        try
        {
            var early = ShowDelay - Stopwatch.GetElapsedTime(_started);
            if (early > TimeSpan.Zero)
                await Task.Delay(early, _stop.Token);

            while (await _signal.Reader.WaitToReadAsync(_stop.Token))
            {
                if (lastPush != 0)
                {
                    var wait = _minEditInterval - Stopwatch.GetElapsedTime(lastPush);
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, _stop.Token);
                }

                // Read after the pause, so the steps which came during it do not cause one more identical edit.
                _signal.Reader.TryRead(out _);

                lastPush = Stopwatch.GetTimestamp();
                if (!await PushInternalAsync())
                {
                    // Over the edit budget: the newest steps go out with the next edit.
                    _signal.Writer.TryWrite(0);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<bool> PushInternalAsync()
    {
        if (_disabled || BotTimedOutInternal())
            return true;

        // The request ended since the worker woke up; a message made now would only be deleted.
        if (_message is null && (_stop.IsCancellationRequested || !CanSendInternal()))
            return true;

        if (!_limiter.TryTakeStatusEdit(_guildId))
            return false;

        string body;
        lock (_lock)
        {
            body = AgentActivityFormatter.Render(
                _lines,
                _dropped,
                _useEmbed ? EmbedBuilder.MaxDescriptionLength : DiscordConfig.MaxMessageSize,
                _useEmbed ? null : "**" + GetText(strs.agent_activity_working) + "**",
                subtext: !_useEmbed,
                _strings,
                _guildId);
        }

        Embed? embed = null;
        string? text = null;
        if (_useEmbed)
        {
            embed = _sender.CreateEmbed(_guildId)
                .WithPendingColor()
                .WithTitle(GetText(strs.agent_activity_working))
                .WithDescription(body)
                .Build();
        }
        else
            text = body;

        try
        {
            if (_message is null)
            {
                _message = await _channel.SendMessageAsync(
                    text,
                    embed: embed,
                    allowedMentions: AiAgentService.NoPing,
                    messageReference: _reference);
                IsShown = true;
            }
            else
            {
                await _message.ModifyAsync(p =>
                {
                    if (embed is not null)
                        p.Embed = embed;
                    else
                        p.Content = text;
                    p.AllowedMentions = AiAgentService.NoPing;
                });
            }
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            OnRefusedInternal(ex);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not update the AI agent activity message in channel {ChannelId}", _channel.Id);
        }

        return true;
    }

    // The bot lost a permission or someone deleted the message; each later call would fail the same way.
    private void OnRefusedInternal(HttpException ex)
    {
        Log.Write(
            ex.HttpCode == HttpStatusCode.NotFound ? LogEventLevel.Information : LogEventLevel.Warning,
            "AI agent activity message was refused in channel {ChannelId}: {Error}",
            _channel.Id,
            ex.Message);
        _disabled = true;
        if (ex.HttpCode == HttpStatusCode.NotFound)
            _message = null;
        else
            IsRefused = true;
    }

    private bool BotTimedOutInternal()
        => _channel.Guild is SocketGuild { CurrentUser: { } bot } && AgentPermissions.IsTimedOut(bot);

    private bool CanSendInternal()
        => _channel.Guild is not SocketGuild guild || (guild.CurrentUser is { } bot && AgentPermissions.CanSend(bot, _channel));

    private string GetText(LocStr loc)
        => _strings.GetText(loc.Key, _guildId, loc.Params);
}
