#nullable disable
using Newtonsoft.Json;
using OneOf.Types;
using SharpToken;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Games.Common.ChatterBot;

public partial class OpenAiApiSession : IChatterBotSession
{
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxHistory;
    private readonly int _maxTokens;
    private readonly int _minTokens;
    private readonly string _nadekoUsername;
    private readonly GptEncoding _encoding;
    private List<OpenAiApiMessage> messages = new();
    private readonly IHttpClientFactory _httpFactory;


    public OpenAiApiSession(
        string url,
        string apiKey,
        string model,
        int chatHistory,
        int maxTokens,
        int minTokens,
        string personality,
        string nadekoUsername,
        IHttpClientFactory factory)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Invalid OpenAi api url provided", nameof(url));
        }

        _baseUrl = url.TrimEnd('/');

        _apiKey = apiKey;
        _model = model;
        _httpFactory = factory;
        _maxHistory = chatHistory;
        _maxTokens = maxTokens;
        _minTokens = minTokens;
        _nadekoUsername = UsernameCleaner().Replace(nadekoUsername, "");
        _encoding = GptEncoding.GetEncodingForModel("gpt-4o");
        if (!string.IsNullOrWhiteSpace(personality))
        {
            messages.Add(new()
            {
                Role = "system",
                Content = personality,
                Name = _nadekoUsername
            });
        }
    }


    [GeneratedRegex("[^a-zA-Z0-9_-]")]
    private static partial Regex UsernameCleaner();

    public async Task<OneOf.OneOf<ThinkResult, Error<string>>> Think(string input, string username)
    {
        username = UsernameCleaner().Replace(username, "");

        messages.Add(new()
        {
            Role = "user",
            Content = input,
            Name = username
        });

        while (messages.Count > _maxHistory + 2)
        {
            messages.RemoveAt(1);
        }

        var tokensUsed = messages.Sum(message => _encoding.Encode(message.Content).Count);

        tokensUsed *= 2;

        //check if we have the minimum number of tokens available to use. Remove messages until we have enough, otherwise exit out and inform the user why.
        while (_maxTokens - tokensUsed <= _minTokens)
        {
            if (messages.Count > 2)
            {
                var tokens = _encoding.Encode(messages[1].Content).Count * 2;
                tokensUsed -= tokens;
                messages.RemoveAt(1);
            }
            else
            {
                return new Error<string>(
                    "Token count exceeded, please increase the number of tokens in the bot config and restart.");
            }
        }

        using var http = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", _apiKey);

        var data = await http.PostAsJsonAsync($"{_baseUrl}/v1/chat/completions",
            new OpenAiApiRequest()
            {
                Model = _model,
                Messages = messages,
                MaxTokens = _maxTokens - tokensUsed,
                Temperature = 1,
            });

        var dataString = await data.Content.ReadAsStringAsync();

        try
        {
            data.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get response from OpenAI: {Message}", ex.Message);
            return new Error<string>("Failed to get response from OpenAI");
        }
        
        try
        {
            
            var response = JsonConvert.DeserializeObject<OpenAiCompletionResponse>(dataString);

            // Log.Information("Received response: {Response} ", dataString);
            var res = response?.Choices?[0];
            var message = res?.Message?.Content;

            if (message is null)
            {
                return new Error<string>("ChatGpt: Received no response.");
            }

            messages.Add(new()
            {
                Role = "assistant",
                Content = message,
                Name = _nadekoUsername
            });

            return new ThinkResult()
            {
                Text = message,
                TokensIn = response.Usage.PromptTokens,
                TokensOut = response.Usage.CompletionTokens
            };
        }
        catch
        {
            Log.Warning("Unexpected response received from OpenAI: {ResponseString}", dataString);
            return new Error<string>("Unexpected response received");
        }
    }
}