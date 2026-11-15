using System.Text.Json;
using System.Text.Json.Serialization;

namespace FolderScope.Desktop;

internal sealed record ModelProfile(
    string Key, string ProviderId, string ProviderName, string DisplayName, string BaseUrl,
    string Model, string ClientType = "openai-chat", string ProtectedKey = "", bool IsCustom = false, string CodexCommand = "codex")
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    [JsonIgnore]
    public string Label => ProviderName + " · " + (string.IsNullOrWhiteSpace(DisplayName) ? Model : DisplayName);

    [JsonIgnore]
    public bool IsConfigured => ClientType == "codex-cli"
        ? LocalCodexClient.FindExecutable(CodexCommand) is not null : ProtectedKey.Length > 0;

    public void Validate(bool requireKey = false)
    {
        if (ClientType == "codex-cli")
        {
            if (LocalCodexClient.FindExecutable(CodexCommand) is null)
                throw new ArgumentException("未找到本机 codex.exe，请填写完整路径。");
            return;
        }
        _ = Endpoint();
        if (string.IsNullOrWhiteSpace(Key) || string.IsNullOrWhiteSpace(Model))
            throw new ArgumentException("请输入模型 ID。");
        if (requireKey && string.IsNullOrWhiteSpace(ProtectedKey))
            throw new ArgumentException("请填写该模型的 API 密钥。");
    }

    public Uri Endpoint()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("请输入有效的 HTTPS API 地址，不带查询参数或片段。");
        var route = ClientType switch
        {
            "openai-chat" => "chat/completions",
            "openai-responses" => "responses",
            "ant-messages" => "messages",
            _ => throw new ArgumentException("请选择支持的文字模型协议。")
        };
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/" + route);
    }

    public override string ToString() => Label;
}
