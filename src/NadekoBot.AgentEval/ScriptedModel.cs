using System.Net;
using System.Text;
using System.Text.Json;

namespace NadekoBot.AgentEval;

public sealed class ModelTurn
{
    public required int RequestIndex { get; init; }

    // The number of assistant messages in the request, so the same step of a retried request has the same number.
    public required int Step { get; init; }

    public required IReadOnlyList<string> ToolResults { get; init; }

    public required string UserText { get; init; }

    public required string? ToolChoice { get; init; }

    public required bool HasTools { get; init; }

    public required string Body { get; init; }

    public bool ResultHas(string text)
    {
        foreach (var r in ToolResults)
        {
            if (r.Contains(text, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // The old "Command executed" result has none of these words.
    private static readonly string[] _failureWords =
        ["refused", "failed", "not_found", "not found", "wrong arguments", "wrong_arguments", "bad_args", "error", "missing", "unknown",
         "did not run", "could not", "denied", "invalid", "not run", "no command", "no tool"];

    public bool SawFailure()
    {
        foreach (var w in _failureWords)
        {
            if (ResultHas(w))
                return true;
        }

        return false;
    }
}

public sealed class ModelReply
{
    public int Status { get; private init; } = 200;
    public string? ErrorBody { get; private init; }
    public string? Text { get; private init; }
    public IReadOnlyList<(string Name, string Args)> Calls { get; private init; } = [];
    public string FinishReason { get; private init; } = "stop";

    public bool IsCall
        => Calls.Count > 0;

    public static ModelReply Say(string? text)
        => new() { Text = text };

    public static ModelReply Call(string name, string args)
        => new() { Calls = [(name, args)], FinishReason = "tool_calls" };

    public static ModelReply CallMany(IReadOnlyList<(string Name, string Args)> calls)
        => new() { Calls = calls, FinishReason = "tool_calls" };

    public static ModelReply Http(int status, string? body = null)
        => new() { Status = status, ErrorBody = body };

    public ModelReply CutOff()
        => new() { Status = Status, Text = Text, Calls = Calls, FinishReason = "length" };
}

public sealed class ScriptedModel(Func<ModelTurn, ModelReply> script, string toolsOffReply) : HttpMessageHandler
{
    private readonly Lock _lock = new();
    private readonly List<string> _bodies = [];
    private List<string>? _prevMessages;
    private string? _prevTools;

    public int Requests
    {
        get
        {
            lock (_lock)
                return _bodies.Count;
        }
    }

    // Each request starts with the exact messages of the one before, so the provider can reuse its prompt cache.
    public bool PrefixStable { get; private set; } = true;

    public int ToolsOffRequests { get; private set; }

    // A new request of the member starts a new message list, so the prefix check starts again.
    public void StartRequest()
    {
        lock (_lock)
        {
            _prevMessages = null;
            _prevTools = null;
        }
    }

    public IReadOnlyList<string> Bodies
    {
        get
        {
            lock (_lock)
                return _bodies.ToArray();
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        ModelTurn turn;
        lock (_lock)
        {
            _bodies.Add(body);
            turn = ParseInternal(body, _bodies.Count - 1);
        }

        var reply = script(turn);
        if (turn.ToolChoice == "none" || !turn.HasTools)
        {
            ToolsOffRequests++;
            if (reply.IsCall)
                reply = ModelReply.Say(toolsOffReply);
        }

        if (reply.Status != 200)
        {
            return new HttpResponseMessage((HttpStatusCode)reply.Status)
            {
                Content = new StringContent(reply.ErrorBody ?? """{"error":{"message":"scripted failure"}}""", Encoding.UTF8, "application/json")
            };
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(BuildJsonInternal(reply, turn), Encoding.UTF8, "application/json")
        };
    }

    private ModelTurn ParseInternal(string body, int index)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var raw = new List<string>();
        var users = new StringBuilder();
        var results = new List<string>();
        var assistants = 0;

        foreach (var m in root.GetProperty("messages").EnumerateArray())
        {
            raw.Add(m.GetRawText());
            var role = m.GetProperty("role").GetString();
            var content = m.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
            switch (role)
            {
                case "assistant":
                    assistants++;
                    results.Clear();
                    break;
                case "tool":
                    results.Add(content);
                    break;
                case "user":
                    users.AppendLine(content);
                    break;
            }
        }

        var tools = root.TryGetProperty("tools", out var t) ? t.GetRawText() : "";
        string? toolChoice = null;
        if (root.TryGetProperty("tool_choice", out var tc))
            toolChoice = tc.ValueKind == JsonValueKind.String ? tc.GetString() : tc.GetRawText();

        if (_prevMessages is not null)
        {
            if (raw.Count < _prevMessages.Count || !string.Equals(tools, _prevTools, StringComparison.Ordinal))
                PrefixStable = false;
            else
            {
                for (var i = 0; i < _prevMessages.Count; i++)
                {
                    if (!string.Equals(raw[i], _prevMessages[i], StringComparison.Ordinal))
                    {
                        PrefixStable = false;
                        break;
                    }
                }
            }
        }

        _prevMessages = raw;
        _prevTools = tools;

        return new()
        {
            RequestIndex = index,
            Step = assistants,
            ToolResults = results,
            UserText = users.ToString(),
            ToolChoice = toolChoice,
            HasTools = root.TryGetProperty("tools", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0,
            Body = body
        };
    }

    private static string BuildJsonInternal(ModelReply reply, ModelTurn turn)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("id", $"eval-{turn.RequestIndex}");
            w.WriteString("object", "chat.completion");
            w.WriteStartArray("choices");
            w.WriteStartObject();
            w.WriteNumber("index", 0);
            w.WriteStartObject("message");
            w.WriteString("role", "assistant");
            if (reply.Text is null)
                w.WriteNull("content");
            else
                w.WriteString("content", reply.Text);

            if (reply.IsCall)
            {
                w.WriteStartArray("tool_calls");
                for (var i = 0; i < reply.Calls.Count; i++)
                {
                    w.WriteStartObject();
                    w.WriteString("id", $"call_{turn.RequestIndex}_{i}");
                    w.WriteString("type", "function");
                    w.WriteStartObject("function");
                    w.WriteString("name", reply.Calls[i].Name);
                    w.WriteString("arguments", reply.Calls[i].Args);
                    w.WriteEndObject();
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            w.WriteEndObject();
            w.WriteString("finish_reason", reply.FinishReason);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteStartObject("usage");
            w.WriteNumber("prompt_tokens", 1000);
            w.WriteNumber("completion_tokens", 20);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}

public sealed class FakeHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
        => new(handler, disposeHandler: false);
}

// The evaluation must not reach the network, so the embedding model must already be on disk.
public sealed class NoNetworkHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => throw new InvalidOperationException($"The evaluation does not use the network ({request.RequestUri}).");
}
