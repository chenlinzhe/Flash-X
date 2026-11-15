using System.Text.Json;

namespace FolderScope.Desktop;

internal static class ModelCatalog
{
    private sealed record Provider(string Id, string Name, string BaseUrl, string ClientType, Model[] Models);
    private sealed record Model(string Id, string Name, string? ClientType);

    internal static IReadOnlyList<ModelProfile> Presets()
    {
        using var resource = typeof(ModelCatalog).Assembly.GetManifestResourceStream("FolderScope.ModelPresets.json")
            ?? throw new InvalidOperationException("模型预设目录未打包。");
        var providers = JsonSerializer.Deserialize<Provider[]>(resource, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("模型预设目录为空。");
        var profiles = providers.SelectMany(provider => provider.Models.Select(model => new ModelProfile(
            provider.Id + ":" + model.Id, provider.Id, provider.Name, model.Name, provider.BaseUrl,
            model.Id, model.ClientType ?? provider.ClientType))).ToList();
        var position = profiles.FindLastIndex(profile => profile.ProviderId == "deepseek") + 1;
        profiles.Insert(position, new ModelProfile("codex-cli:default", "codex-cli", "本机 Codex CLI",
            "本机默认模型", "", "", "codex-cli"));
        return profiles;
    }

    internal static List<ModelProfile> Merge(IEnumerable<ModelProfile> saved)
    {
        var items = Presets().ToDictionary(profile => profile.Key, StringComparer.Ordinal);
        foreach (var profile in saved) items[profile.Key] = profile;
        return items.Values.ToList();
    }
}
