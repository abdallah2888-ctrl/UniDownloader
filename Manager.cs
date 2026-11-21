using System.Diagnostics;
using System.Text.Json;
namespace UniDownloader;

public static class Manager
{
    public static readonly List<Job> Jobs = new();
    public static event Action Changed;
    public static event Action<Job> Finished;
    static int nextId = 1;
    static readonly object L = new();
    static string JobsFile => Path.Combine(AppSettings.DataDir, "jobs.json");

    public static List<Job> Snapshot() { lock (L) return Jobs.ToList(); }

    public static void Load()
    {
        try
        {
            if (!File.Exists(JobsFile)) return;
            var list = JsonSerializer.Deserialize<List<Job>>(File.ReadAllText(JobsFile));
            if (list == null) return;
            foreach (var j in list)
            {
                if (j.State == JobState.Running || j.State == JobState.Queued) j.State = JobState.Paused;
                Jobs.Add(j);
                nextId = Math.Max(nextId, j.Id + 1);
            }
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            string json;
            lock (L) json = JsonSerializer.Serialize(Jobs);
            Directory.CreateDirectory(AppSettings.DataDir);
            File.WriteAllText(JobsFile, json);
        }
        catch { }
    }

    public static Job Add(string url, string referrer, string cookie, string ua, bool video, string quality, bool playlist)
    {
        Job j;
        lock (L)
        {
            var dup = Jobs.FirstOrDefault(x => x.Url == url && (x.State == JobState.Running || x.State == JobState.Queued));
            if (dup != null) return dup;
            j = new Job
            {
                Id = nextId++, Url = url, Referrer = referrer ?? "", Cookie = cookie ?? "",
                UserAgent = ua ?? "", IsVideo = video, Quality = quality ?? "best", Playlist = playlist
            };
            Jobs.Add(j);
        }
        Save(); Changed?.Invoke(); Pump();
        return j;
    }

    static void Pump()
    {
        lock (L)
        {
            int running = Jobs.Count(x => x.State == JobState.Running);
            foreach (var j in Jobs.ToList())
            {
                if (running >= Math.Max(1, AppSettings.Current.MaxParallel)) break;
                if (j.State == JobState.Queued) { Start(j); running++; }
            }
        }
    }

    static void Start(Job j)
    {
        j.State = JobState.Running; j.Error = ""; j.Speed = 0;
        var cts = new CancellationTokenSource();
        j.Cts = cts;
        var ct = cts.Token;
        Task.Run(async () =>
        {
            try
            {
                if (j.IsVideo) await VideoEngine.RunAsync(j, ct);
                else await DownloadEngine.RunAsync(j, ct);
                j.State = JobState.Done; j.Percent = 100; j.Speed = 0; j.Eta = ""; j.Note = "";
                try { if (File.Exists(j.SavePath)) j.Total = new FileInfo(j.SavePath).Length; } catch { }
                Save();
                try { Finished?.Invoke(j); } catch { }
            }
            catch (OperationCanceledException)
            {
                if (j.State == JobState.Running) j.State = JobState.Paused;
                j.Speed = 0; j.Eta = "";
            }
            catch (Exception ex)
            {
                j.State = JobState.Error; j.Error = ex.Message; j.Speed = 0; j.Eta = "";
            }
            Save(); Changed?.Invoke(); Pump();
        });
    }

    public static void Pause(Job j)
    {
        if (j.State == JobState.Running) { j.State = JobState.Paused; try { j.Cts?.Cancel(); } catch { } }
        else if (j.State == JobState.Queued) j.State = JobState.Paused;
        Save(); Changed?.Invoke();
    }

    public static void Resume(Job j)
    {
        if (j.State == JobState.Paused || j.State == JobState.Error || j.State == JobState.Canceled)
        {
            j.State = JobState.Queued; j.Error = "";
            Save(); Changed?.Invoke(); Pump();
        }
    }

    public static void PauseAll()
    {
        foreach (var j in Snapshot())
            if (j.State == JobState.Running || j.State == JobState.Queued) Pause(j);
        Save();
    }

    public static void Remove(Job j, bool deleteFiles)
    {
        try { j.State = JobState.Canceled; j.Cts?.Cancel(); } catch { }
        lock (L) Jobs.Remove(j);
        if (deleteFiles && !string.IsNullOrEmpty(j.SavePath))
        {
            Task.Run(async () =>
            {
                await Task.Delay(800);
                foreach (var p in new[] { j.SavePath, j.SavePath + ".part", j.SavePath + ".ud" })
                    try { File.Delete(p); } catch { }
            });
        }
        Save(); Changed?.Invoke();
    }
}
