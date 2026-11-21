using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;
namespace UniDownloader;

public static class Setup
{
    public static string InstalledExe => Path.Combine(AppSettings.DataDir, "UniDownloader.exe");

    // أول تشغيل: ينسخ البرنامج إلى مكان ثابت ويصنع اختصارات سطح المكتب وقائمة ابدأ ويشغّله مع ويندوز.
    // يرجع true إذا شغّل النسخة المثبتة ويجب إغلاق هذه النسخة.
    public static bool FirstRun()
    {
        var cfg = AppSettings.Current;
        if (cfg.FirstRunDone) return false;
        try
        {
            var me = Environment.ProcessPath;
            Directory.CreateDirectory(AppSettings.DataDir);
            bool same = string.Equals(Path.GetFullPath(me), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);
            if (!same) File.Copy(me, InstalledExe, true);
            cfg.FirstRunDone = true;
            cfg.Save();
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "UniDownloader.lnk"));
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "UniDownloader.lnk"));
            SetAutoStart(cfg.StartWithWindows);
            if (!same)
            {
                Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true });
                return true;
            }
        }
        catch { }
        return false;
    }

    static string TargetExe() => File.Exists(InstalledExe) ? InstalledExe : Environment.ProcessPath;

    public static void CreateShortcut(string lnkPath)
    {
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic sh = Activator.CreateInstance(t);
            var lnk = sh.CreateShortcut(lnkPath);
            lnk.TargetPath = TargetExe();
            lnk.WorkingDirectory = AppSettings.DataDir;
            lnk.Description = "UniDownloader";
            lnk.Save();
        }
        catch { }
    }

    public static void SetAutoStart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (k == null) return;
            if (on) k.SetValue("UniDownloader", "\"" + TargetExe() + "\" --hidden");
            else k.DeleteValue("UniDownloader", false);
        }
        catch { }
    }

    public static string ExtractExtension()
    {
        var asm = Assembly.GetExecutingAssembly();
        var dir = Path.Combine(AppSettings.DataDir, "extension");
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("ext/")))
        {
            var rel = name.Substring(4).Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
            var target = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using var s = asm.GetManifestResourceStream(name);
            using var f = File.Create(target);
            s.CopyTo(f);
        }
        return dir;
    }

    public static void InstallExtension()
    {
        string dir;
        try { dir = ExtractExtension(); }
        catch (Exception ex) { MessageBox.Show("تعذر تجهيز الإضافة: " + ex.Message); return; }

        try { Clipboard.SetText(dir); } catch { }
        try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
        try { Process.Start(new ProcessStartInfo("chrome.exe", "chrome://extensions") { UseShellExecute = true }); }
        catch
        {
            try { Process.Start(new ProcessStartInfo("msedge.exe", "edge://extensions") { UseShellExecute = true }); } catch { }
        }

        MessageBox.Show(
            "تم تجهيز الإضافة في المجلد:\n" + dir + "\n(المسار منسوخ في الحافظة)\n\n" +
            "الخطوات في صفحة الإضافات التي فُتحت:\n" +
            "1) فعّل «وضع المطوّر» (Developer mode) من أعلى الصفحة.\n" +
            "2) اضغط «تحميل الإضافة غير المعبأة» (Load unpacked).\n" +
            "3) اختر المجلد المذكور أعلاه.\n\n" +
            "بعدها: أي تحميل في المتصفح يذهب تلقائياً إلى UniDownloader، وتظهر أيقونة التحميل الخضراء أعلى يوتيوب.",
            "تثبيت الإضافة", MessageBoxButtons.OK, MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
    }
}
