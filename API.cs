using System.Net.Http.Headers;
using System.Net.Mime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SimpleChat
{
    internal class API : IDisposable
    {
        private const string MODEL_EMBEDDINGS = "text-embedding-ada-002";
        private const string MODEL_COMPLETION = "text-davinci-003";
        private const string MODEL_CHATCOMPLETION = "gpt-4"; // gpt-3.5-turbo
        private const string MODEL_MODERATION = "text-moderation-latest";

        private const string TOKEN_DONE = "[DONE]";

        private const string BASE_URL = "https://api.openai.com/";
        private const string API_VERSION = "v1";

        private bool _disposed;
        private HttpClient _httpClient = new();

        public API(string key)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                    _httpClient.Dispose();

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private string GetResponseFromServerSideEvent(string response) => Regex.Match(response, @"(?<=data:\s).*").Value;

        public async Task<ListModelsResponse> ListModels()
        {
            using (var response = await _httpClient.GetAsync($"{BASE_URL}/{API_VERSION}/models"))
            {
                if (!response.IsSuccessStatusCode)
                    throw new Exception(response.ReasonPhrase);

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<ListModelsResponse>(json) ?? new();

            }
        }

        public async Task<CreateEmbeddingsResponse> CreateEmbeddings(string input)
        {
            var json = JsonSerializer.Serialize(new { model = MODEL_EMBEDDINGS, input });
            var content = new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json);

            using (var response = await _httpClient.PostAsync($"{BASE_URL}/{API_VERSION}/embeddings", content))
            {
                if (!response.IsSuccessStatusCode)
                    throw new Exception(response.ReasonPhrase);

                json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<CreateEmbeddingsResponse>(json) ?? new();
            }
        }

        public async IAsyncEnumerable<CreateCompletionResponse> CreateCompletion(string[] prompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(new { model = MODEL_COMPLETION, prompt, temperature = 0, stream = true });
            var content = new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json);

            using (var response = await _httpClient.PostAsStreamAsync($"{BASE_URL}/{API_VERSION}/completions", content, cancellationToken))
            {
                if (!response.IsSuccessStatusCode)
                    throw new Exception(response.ReasonPhrase);

                await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    using (var reader = new StreamReader(stream))
                    {
                        while (!reader.EndOfStream)
                        {
                            var line = await reader.ReadLineAsync() ?? String.Empty;
                            var data = GetResponseFromServerSideEvent(line);

                            if (String.IsNullOrEmpty(data) || data == TOKEN_DONE)
                                continue;

                            yield return JsonSerializer.Deserialize<CreateCompletionResponse>(data) ?? new();
                        }
                    }
                }
            }
        }

        public async IAsyncEnumerable<ChatCompletionResponse> CreateChatCompletion(
            List<ChatCompletionMessage> messages,
            double temperature = 0,
            string[]? stop = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)); // No SnakeCase...

            var json = JsonSerializer.Serialize(new { model = MODEL_CHATCOMPLETION, messages, temperature, stop, stream = true }, options);
            var content = new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json);

            using var response = await _httpClient.PostAsStreamAsync($"{BASE_URL}/{API_VERSION}/chat/completions", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new Exception(response.ReasonPhrase);

            await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                using (var reader = new StreamReader(stream))
                {
                    while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(cancellationToken) ?? String.Empty;
                        var data = GetResponseFromServerSideEvent(line);

                        if (String.IsNullOrEmpty(data) || data == TOKEN_DONE)
                            continue;

                        yield return JsonSerializer.Deserialize<ChatCompletionResponse>(data) ?? new();
                    }
                }
            }
        }

        public async Task<CreateModerationResponse> CreateModeration(params string[] input)
        {
            var json = JsonSerializer.Serialize(new { model = MODEL_MODERATION, input });
            var content = new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json);

            using (var response = await _httpClient.PostAsync($"{BASE_URL}/{API_VERSION}/moderations", content))
            {
                if (!response.IsSuccessStatusCode)
                    throw new Exception(response.ReasonPhrase);

                json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<CreateModerationResponse>(json) ?? new();
            }
        }
    }

    internal class UnixTimeJsonConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64()).DateTime.ToLocalTime();
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteNumberValue(value.ToUniversalTime().Subtract(DateTime.UnixEpoch).TotalSeconds);
    }

    internal class PermissionResponse
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("object")] public string? Object { get; set; }
        [JsonPropertyName("created"), JsonConverter(typeof(UnixTimeJsonConverter))] public DateTime Created { get; set; }
        [JsonPropertyName("allow_create_engine")] public bool AllowCreateEngine { get; set; }
        [JsonPropertyName("allow_sampling")] public bool AllowSampling { get; set; }
        [JsonPropertyName("allow_logprobs")] public bool AllowLogProbs { get; set; }
        [JsonPropertyName("allow_search_indices")] public bool AllowSearchIndices { get; set; }
        [JsonPropertyName("allow_view")] public bool AllowView { get; set; }
        [JsonPropertyName("allow_fine_tuning")] public bool AllowFineTuning { get; set; }
        [JsonPropertyName("organization")] public string? Organization { get; set; }
        [JsonPropertyName("group")] public string? Group { get; set; }
        [JsonPropertyName("is_blocking")] public bool IsBlocking { get; set; }
    }

    internal class ModelResponse
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("object")] public string? Object { get; set; }
        [JsonPropertyName("created"), JsonConverter(typeof(UnixTimeJsonConverter))] public DateTime Created { get; set; }
        [JsonPropertyName("owned_by")] public string? OwnedBy { get; set; }
        [JsonPropertyName("permission")] public List<PermissionResponse> Permission { get; set; } = new();
        [JsonPropertyName("root")] public string? Root { get; set; }
        [JsonPropertyName("parent")] public string? Parent { get; set; }
    }

    internal class ListModelsResponse
    {
        [JsonPropertyName("object")] public string? Object { get; set; }
        [JsonPropertyName("data")] public List<ModelResponse> Data { get; set; } = new();
    }

    internal class UsageResponse
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
        [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
    }

    internal class EmbeddingResponse
    {
        [JsonPropertyName("object")] public string? Object { get; set; }
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("embedding")] public List<double> Embedding { get; set; } = new();
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("usage")] public UsageResponse? Usage { get; set; }
    }

    internal class CreateEmbeddingsResponse
    {
        [JsonPropertyName("object")] public string? Object { get; set; }
        [JsonPropertyName("data")] public List<EmbeddingResponse> Data { get; set; } = new();
    }

    internal class CreateCompletionChoice
    {
        [JsonPropertyName("text")] public string Text { get; set; } = String.Empty;
        [JsonPropertyName("index")] public int? Index { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    internal class CreateCompletionResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = String.Empty;
        [JsonPropertyName("object")] public string Object { get; set; } = String.Empty;
        [JsonPropertyName("created"), JsonConverter(typeof(UnixTimeJsonConverter))] public DateTime Created { get; set; }
        [JsonPropertyName("model")] public string Model { get; set; } = String.Empty;
        [JsonPropertyName("choices")] public List<CreateCompletionChoice> Choices { get; set; } = new();
    }

    internal class ChatCompletionDelta
    {
        [JsonPropertyName("role")] public string? Role { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
    }

    internal class ChatCompletionChoice
    {
        [JsonPropertyName("delta")] public ChatCompletionDelta Delta { get; set; } = new();
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    internal class ChatCompletionResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = String.Empty;
        [JsonPropertyName("object")] public string Object { get; set; } = String.Empty;
        [JsonPropertyName("created"), JsonConverter(typeof(UnixTimeJsonConverter))] public DateTime Created { get; set; }
        [JsonPropertyName("model")] public string Model { get; set; } = String.Empty;
        [JsonPropertyName("choices")] public List<ChatCompletionChoice> Choices { get; set; } = new();
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChatCompletionRequestRole { System, User, Assistant }

    internal class ChatCompletionMessage
    {
        [JsonPropertyName("role")] public ChatCompletionRequestRole Role { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
    }

    internal class ModerationCategories
    {
        [JsonPropertyName("sexual")] public bool? Sexual { get; set; }
        [JsonPropertyName("hate")] public bool? Hate { get; set; }
        [JsonPropertyName("violence")] public bool? Violence { get; set; }
        [JsonPropertyName("self-harm")] public bool? SelfHarm { get; set; }
        [JsonPropertyName("sexual/minors")] public bool? SexualMinors { get; set; }
        [JsonPropertyName("hate/threatening")] public bool? HateThreatening { get; set; }
        [JsonPropertyName("violence/graphic")] public bool? ViolenceGraphic { get; set; }
    }

    internal class ModerationCategoryScores
    {
        [JsonPropertyName("sexual")] public double? Sexual { get; set; }
        [JsonPropertyName("hate")] public double? Hate { get; set; }
        [JsonPropertyName("violence")] public double? Violence { get; set; }
        [JsonPropertyName("self-harm")] public double? SelfHarm { get; set; }
        [JsonPropertyName("sexual/minors")] public double? SexualMinors { get; set; }
        [JsonPropertyName("hate/threatening")] public double? HateThreatening { get; set; }
        [JsonPropertyName("violence/graphic")] public double? ViolenceGraphic { get; set; }
    }

    internal class ModerationResult
    {
        [JsonPropertyName("flagged")] public bool? Flagged { get; set; }
        [JsonPropertyName("categories")] public ModerationCategories? Categories { get; set; }
        [JsonPropertyName("category_scores")] public ModerationCategoryScores? CategoriesScores { get; set; }
    }

    internal class CreateModerationResponse
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("results")] public List<ModerationResult> Results { get; set; } = new();
    }
}
