using System.Text.Json;
namespace UniDownloader;

public class AppSettings
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UniDownloader");

    static string FilePath => Path.Combine(DataDir, "settings.json");

    public string DownloadFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "UniDownloader");
    public int Connections { get; set; } = 16;
    public int MaxParallel { get; set; } = 3;
    public int SpeedLimitKB { get; set; } = 0;
    public bool Categorize { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool WatchClipboard { get; set; } = false;
    public string UpdateRepo { get; set; } = "";
    public bool FirstRunDone { get; set; } = false;
    public DateTime LastYtDlpUpdate { get; set; } = DateTime.MinValue;

    public static AppSettings Current = Load();

    static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
