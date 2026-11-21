using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
namespace UniDownloader;

public static class Updater
{
    static void Msg(string t) => MessageBox.Show(t, "UniDownloader", MessageBoxButtons.OK, MessageBoxIcon.Information,
        MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

    public static async Task CheckAsync(bool manual)
    {
        var repo = AppSettings.Current.UpdateRepo.Trim();
        if (repo.Length == 0)
        {
            if (manual) Msg("اكتب اسم المستودع في الإعدادات بالشكل: اسم_حسابك/UniDownloader (المستودع يجب أن يكون عاماً Public).");
            return;
        }
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
            req.Headers.TryAddWithoutValidation("User-Agent", "UniDownloader");
            using var resp = await DownloadEngine.Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                if (manual) Msg("تعذر فحص التحديث (تأكد أن المستودع عام وأن الاسم صحيح).");
                return;
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var tag = doc.RootElement.GetProperty("tag_name").GetString().TrimStart('v');
            if (!Version.TryParse(tag, out var nv)) return;
            var cv = Assembly.GetExecutingAssembly().GetName().Version;
            var newV = new Version(nv.Major, nv.Minor, Math.Max(0, nv.Build));
            var curV = new Version(cv.Major, cv.Minor, Math.Max(0, cv.Build));
            if (newV <= curV)
            {
                if (manual) Msg("أنت تستخدم أحدث نسخة.");
                return;
            }
            string url = null;
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
                if (a.GetProperty("name").GetString() == "UniDownloader.exe")
                    url = a.GetProperty("browser_download_url").GetString();
            if (url == null) return;

            var ask = MessageBox.Show("توجد نسخة جديدة (" + tag + "). هل تريد التحديث الآن؟", "UniDownloader",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            if (ask != DialogResult.Yes) return;

            var exe = Setup.InstalledExe;
            if (!File.Exists(exe)) exe = Environment.ProcessPath;
            var neu = exe + ".new";
            using (var r = await DownloadEngine.Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                r.EnsureSuccessStatusCode();
                using var s = await r.Content.ReadAsStreamAsync();
                using var f = File.Create(neu);
                await s.CopyToAsync(f);
            }
            var bat = Path.Combine(AppSettings.DataDir, "update.bat");
            File.WriteAllText(bat,
                "@echo off\r\ntimeout /t 3 /nobreak >nul\r\nmove /y \"" + neu + "\" \"" + exe + "\" >nul\r\nstart \"\" \"" + exe + "\"\r\ndel \"%~f0\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"") { CreateNoWindow = true, UseShellExecute = false });
            Manager.PauseAll();
            Application.Exit();
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            if (manual) Msg("خطأ أثناء التحديث: " + ex.Message);
        }
    }
}
