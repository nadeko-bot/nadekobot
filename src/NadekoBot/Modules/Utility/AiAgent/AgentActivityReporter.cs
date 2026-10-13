using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;

namespace NadekoBot.Modules.Utility.AiAgent;

public enum AgentActivityStatus
{
    Working,
    Done,
    Waiting,
    Cancelled,
    Failed
}

// One live message per agent session. A single worker sends and edits it, so edits never overlap
// and at most one edit goes out every two seconds, however many steps the agent takes.
public sealed class AgentActivityReporter : IAgentProgress
{
    private const int MAX_LINES = 64;
    private static readonly TimeSpan _minEditInterval = TimeSpan.FromSeconds(2);

    // Without MentionRepliedUser = false, the reply pings the member who triggered the agent.
    private static readonly AllowedMentions _noMentions = new(AllowedMentionTypes.None) { MentionRepliedUser = false };

    private readonly IMessageSenderService _sender;
    private readonly IBotStrings _strings;
    private readonly ITextChannel _channel;
    private readonly MessageReference? _reference;
    private readonly ulong _guildId;
    private readonly string _prefix;
    private readonly bool _useEmbed;

    private readonly Lock _lock = new();
    private readonly List<string> _lines = new(16);
    private int _dropped;
    private int _steps;

    private readonly Channel<byte> _signal = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = true
    });

    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private IUserMessage? _message;
    private bool _disabled;
    private bool _completed;

    public AgentActivityReporter(
        IMessageSenderService sender,
        IBotStrings strings,
        ITextChannel channel,
        IUserMessage? replyTo,
        string prefix,
        bool useEmbed)
    {
        _sender = sender;
        _strings = strings;
        _channel = channel;
        _guildId = channel.GuildId;
        _prefix = prefix;
        _useEmbed = useEmbed;
        _reference = replyTo is null
            ? null
            : new MessageReference(replyTo.Id, channel.Id, channel.GuildId, failIfNotExists: false);
    }

    public bool IsShown
        => _message is not null;

    public void OnNote(string text)
        => AddLineInternal(AgentActivityFormatter.FormatNote(text), false);

    public void OnToolCall(string name, JsonElement args, bool failed)
    {
        if (AgentActivityFormatter.FormatToolCall(name, args, failed, _prefix, _strings, _guildId) is { } line)
            AddLineInternal(line, true);
    }

    // Safe to call more than once. The first call wins.
    public async Task CompleteAsync(AgentActivityStatus status)
    {
        if (_completed)
            return;

        _completed = true;
        _signal.Writer.TryComplete();
        await _stop.CancelAsync();

        if (_worker is not null)
            await _worker;

        _stop.Dispose();

        if (_steps == 0)
            return;

        await PushInternalAsync(status);
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
            while (await _signal.Reader.WaitToReadAsync(_stop.Token))
            {
                _signal.Reader.TryRead(out _);

                if (lastPush != 0)
                {
                    var wait = _minEditInterval - Stopwatch.GetElapsedTime(lastPush);
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, _stop.Token);
                }

                await PushInternalAsync(AgentActivityStatus.Working);
                lastPush = Stopwatch.GetTimestamp();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PushInternalAsync(AgentActivityStatus status)
    {
        if (_disabled)
            return;

        string body;
        int steps;
        lock (_lock)
        {
            steps = _steps;
            body = AgentActivityFormatter.Render(
                _lines,
                _dropped,
                _useEmbed ? EmbedBuilder.MaxDescriptionLength : DiscordConfig.MaxMessageSize,
                _useEmbed ? null : PlainHeaderInternal(status, steps),
                subtext: !_useEmbed,
                _strings,
                _guildId);
        }

        Embed? embed = null;
        string? text = null;
        if (_useEmbed)
            embed = BuildEmbedInternal(status, steps, body);
        else
            text = body;

        try
        {
            if (_message is null)
            {
                _message = await _channel.SendMessageAsync(
                    text,
                    embed: embed,
                    allowedMentions: _noMentions,
                    messageReference: _reference);
            }
            else
            {
                await _message.ModifyAsync(p =>
                {
                    if (embed is not null)
                        p.Embed = embed;
                    else
                        p.Content = text;
                    p.AllowedMentions = _noMentions;
                });
            }
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            // The bot lost a permission or someone deleted the message. Each later call would fail the same way.
            _disabled = true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not update the AI agent activity message in channel {ChannelId}", _channel.Id);
        }
    }

    private Embed BuildEmbedInternal(AgentActivityStatus status, int steps, string body)
    {
        var eb = _sender.CreateEmbed(_guildId);
        switch (status)
        {
            case AgentActivityStatus.Done:
                eb.WithOkColor();
                break;
            case AgentActivityStatus.Failed:
                eb.WithErrorColor();
                break;
            default:
                eb.WithPendingColor();
                break;
        }

        eb.WithTitle(GetText(StatusText(status)))
          .WithDescription(body);

        if (status != AgentActivityStatus.Working)
            eb.WithFooter(GetText(strs.agent_activity_steps(steps)));

        return eb.Build();
    }

    private string PlainHeaderInternal(AgentActivityStatus status, int steps)
    {
        var header = "**" + GetText(StatusText(status)) + "**";
        return status == AgentActivityStatus.Working
            ? header
            : header + " - " + GetText(strs.agent_activity_steps(steps));
    }

    private static LocStr StatusText(AgentActivityStatus status)
        => status switch
        {
            AgentActivityStatus.Done => strs.agent_activity_done,
            AgentActivityStatus.Waiting => strs.agent_activity_waiting,
            AgentActivityStatus.Cancelled => strs.agent_activity_cancelled,
            AgentActivityStatus.Failed => strs.agent_activity_failed,
            _ => strs.agent_activity_working
        };

    private string GetText(LocStr loc)
        => _strings.GetText(loc.Key, _guildId, loc.Params);
}
