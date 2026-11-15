using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FolderScope.Desktop;

internal sealed partial record ModelSettings
{
    public string Provider { get; init; } = "deepseek";
    public string ClientType { get; init; } = "openai-chat";
    public string? DefaultProfileKey { get; init; }
    public List<ModelProfile>? ModelProfiles { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    internal ModelSettings Normalize()
    {
        var saved = (ModelProfiles ?? []).Select(profile => profile with
        {
            Key = string.IsNullOrWhiteSpace(profile.Key) ? profile.ProviderId + ":" + profile.Model : profile.Key,
            ClientType = string.IsNullOrWhiteSpace(profile.ClientType) ? "openai-chat" : profile.ClientType
        }).ToList();
        if (saved.GroupBy(profile => profile.Key, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidDataException("模型配置有重复标识，原文件已保留。");
        var profiles = ModelCatalog.Merge(saved);
        var active = profiles.FirstOrDefault(profile => profile.Key == DefaultProfileKey);
        if (DefaultProfileKey is not null && active is null)
            throw new InvalidDataException("默认模型不存在，请检查保存的模型配置。");
        if (active is null)
        {
            active = profiles.FirstOrDefault(profile => profile.Model == Model
                && profile.BaseUrl.TrimEnd('/') == BaseUrl.TrimEnd('/') && profile.ClientType == ClientType);
            if (active is null)
            {
                active = new ModelProfile("legacy:" + Model, Provider, "已有配置", Model,
                    BaseUrl, Model, ClientType, ProtectedKey, IsCustom: true);
                profiles.Add(active);
            }
            else
            {
                active = active with { ProtectedKey = ProtectedKey };
                profiles[profiles.FindIndex(profile => profile.Key == active.Key)] = active;
            }
        }
        var migrated = ModelGrouping.MigrateLegacy(profiles, active.Key);
        profiles = ModelGrouping.ShareKeys(migrated.Profiles, migrated.DefaultKey);
        return WithActive(profiles, profiles.Single(profile => profile.Key == migrated.DefaultKey));
    }

    internal ModelProfile EffectiveProfile => (ModelProfiles ?? []).FirstOrDefault(profile => profile.Key == DefaultProfileKey)
        ?? new ModelProfile("legacy:" + Model, Provider, "已有配置", Model, BaseUrl, Model, ClientType, ProtectedKey);

    internal ModelSettings WithProfiles(List<ModelProfile> profiles, string? requestedDefault = null)
    {
        var key = requestedDefault ?? DefaultProfileKey;
        var active = profiles.FirstOrDefault(profile => profile.Key == key)
            ?? throw new ArgumentException("请选择有效的默认模型。");
        var migrated = ModelGrouping.MigrateLegacy(profiles, active.Key);
        profiles = ModelGrouping.ShareKeys(migrated.Profiles, migrated.DefaultKey);
        return WithActive(profiles, profiles.Single(profile => profile.Key == migrated.DefaultKey));
    }

    private ModelSettings WithActive(List<ModelProfile> profiles, ModelProfile active) => this with
    {
        ModelProfiles = profiles, DefaultProfileKey = active.Key, Provider = active.ProviderId,
        BaseUrl = active.BaseUrl, Model = active.Model, ClientType = active.ClientType, ProtectedKey = active.ProtectedKey
    };

    internal ModelSettings Save(string? filePath = null)
    {
        var path = filePath ?? SettingsPath;
        var normalized = Normalize();
        foreach (var profile in normalized.ModelProfiles!)
            if (profile.IsCustom || profile.ProtectedKey.Length > 0 || profile.Key == normalized.DefaultProfileKey) profile.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(pending, JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(pending, path, overwrite: true);
            return normalized;
        }
        finally
        {
            if (File.Exists(pending)) File.Delete(pending);
        }
    }

    internal static ModelSettings Read(string filePath) => (File.Exists(filePath)
        ? JsonSerializer.Deserialize<ModelSettings>(File.ReadAllText(filePath)) ?? throw new InvalidDataException("模型设置为空。")
        : new ModelSettings()).Normalize();

    internal static string EncryptKey(string key) => key.Length == 0 ? ""
        : Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(key), true));

    internal static string RevealKey(string encrypted)
    {
        var bytes = Protect(Convert.FromBase64String(encrypted), false);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
