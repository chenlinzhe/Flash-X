using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FolderScope.Desktop;

internal static class TextModelClient
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(85)
    };

    internal static async Task<string> SendAsync(ModelProfile profile, string system,
        IReadOnlyList<AnalysisChat.Turn> turns, CancellationToken token, HttpClient? client = null)
    {
        profile.Validate(requireKey: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, profile.Endpoint());
        var key = ModelSettings.RevealKey(profile.ProtectedKey);
        if (profile.ClientType == "ant-messages")
        {
            request.Headers.Add("x-api-key", key);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = JsonContent.Create(new
            {
                model = profile.Model, system, max_tokens = 1800,
                messages = turns.Select(turn => new { role = turn.Role, content = turn.Content })
            });
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            var messages = new[] { new AnalysisChat.Turn("system", system) }.Concat(turns)
                .Select(turn => new { role = turn.Role, content = turn.Content }).ToArray();
            request.Content = profile.ClientType == "openai-responses"
                ? JsonContent.Create(new { model = profile.Model, max_output_tokens = 1800, input = messages })
                : JsonContent.Create(new { model = profile.Model, max_tokens = 1800, messages });
        }
        try
        {
            using var response = await (client ?? Client).SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"模型服务返回 HTTP {(int)response.StatusCode}，请核对地址、密钥、模型及协议。");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var root = json.RootElement;
            if (root.TryGetProperty("error", out _) || root.TryGetProperty("type", out var type) && type.GetString() == "error")
                throw new InvalidOperationException("模型服务返回错误，请核对连接配置。");
            if (root.TryGetProperty("status", out var status) && status.GetString() is "failed" or "incomplete" or "cancelled")
                throw new InvalidOperationException("模型未完成回复，请检查模型配置或重试。");
            var text = Extract(root, profile.ClientType);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("接口已响应，但没有返回正文，请核对模型 ID 和协议。");
            return text;
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("无法连接模型服务，请检查网络和 API 地址。");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("模型服务返回了无法识别的数据，请检查 API 地址和协议。");
        }
    }

    private static string Extract(JsonElement root, string protocol)
    {
        if (protocol == "ant-messages")
            return root.TryGetProperty("content", out var content) ? TextBlocks(content, "text") : "";
        if (protocol == "openai-responses")
        {
            if (root.TryGetProperty("output_text", out var direct) && direct.ValueKind == JsonValueKind.String)
                return direct.GetString() ?? "";
            if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return "";
            return string.Join("\n", output.EnumerateArray()
                .Where(item => item.TryGetProperty("type", out var kind) && kind.GetString() == "message")
                .Select(item => item.TryGetProperty("content", out var blocks) ? TextBlocks(blocks, "output_text") : "")
                .Where(text => text.Length > 0));
        }
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0
            || !choices[0].TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var value))
            return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : TextBlocks(value, "text");
    }

    private static string TextBlocks(JsonElement blocks, string kind)
    {
        if (blocks.ValueKind != JsonValueKind.Array) return "";
        return string.Join("\n", blocks.EnumerateArray().Where(block =>
            block.TryGetProperty("type", out var type) && type.GetString() == kind)
            .Select(block => block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() ?? "" : "")
            .Where(text => text.Length > 0));
    }
}
