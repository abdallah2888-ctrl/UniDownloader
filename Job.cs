using System.Text.Json.Serialization;
namespace UniDownloader;

public enum JobState { Queued, Running, Paused, Done, Error, Canceled }

public class Job
{
    public int Id { get; set; }
    public string Url { get; set; } = "";
    public string Referrer { get; set; } = "";
    public string Cookie { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public bool IsVideo { get; set; }
    public string Quality { get; set; } = "best";
    public bool Playlist { get; set; }
    public string FileName { get; set; } = "";
    public string SavePath { get; set; } = "";
    public long Total { get; set; } = -1;
    public long Downloaded { get; set; }
    public double Percent { get; set; }
    public JobState State { get; set; } = JobState.Queued;
    public string Error { get; set; } = "";

    [JsonIgnore] public double Speed { get; set; }
    [JsonIgnore] public string Eta { get; set; } = "";
    [JsonIgnore] public string Note { get; set; } = "";
    [JsonIgnore] public CancellationTokenSource Cts { get; set; }
}
