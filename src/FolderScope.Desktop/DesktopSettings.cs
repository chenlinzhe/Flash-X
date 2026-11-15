using System.IO;
using System.Text.Json;

namespace FolderScope.Desktop;

public sealed record DesktopSettings
{
    public double Width { get; init; } = 1120;
    public double Height { get; init; } = 730;
    public string LastPath { get; init; } = "";
    public string NameMasks { get; init; } = "";
    public bool Exclude { get; init; }
    public bool Bars { get; init; }
    public bool ShowChart { get; init; } = false;
    public int MemoryBudgetGiB { get; init; } = 8;
    public bool RetainFileNodes { get; init; } = true;
    public FolderScope.Core.NodeFilterOptions? AdvancedFilter { get; init; }
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "folderscope.settings.json");

    public static DesktopSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        var value = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(FilePath)) ?? new();
        if (!double.IsFinite(value.Width) || !double.IsFinite(value.Height))
            throw new InvalidDataException("窗口尺寸无效");
        if (value.MemoryBudgetGiB is not (2 or 4 or 8 or 16)) throw new InvalidDataException("扫描内存预算必须是2、4、8或16 GiB");
        return value with { Width = Math.Clamp(value.Width, 800, 3840), Height = Math.Clamp(value.Height, 530, 2160) };
    }

    public void Save()
    {
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
