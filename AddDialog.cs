namespace UniDownloader;

public class AddDialog : Form
{
    readonly TextBox box = new() { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    readonly CheckBox video = new() { Text = "تحميل فيديو/صوت (yt-dlp)", AutoSize = true };
    readonly CheckBox pl = new() { Text = "قائمة التشغيل كاملة", AutoSize = true };
    readonly ComboBox qual = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    static readonly string[] Names = { "أعلى جودة", "2160p (4K)", "1440p", "1080p", "720p", "480p", "360p", "صوت MP3" };
    static readonly string[] Vals = { "best", "2160", "1440", "1080", "720", "480", "360", "mp3" };

    public string[] Urls => box.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim()).Where(s => s.StartsWith("http", StringComparison.OrdinalIgnoreCase)).ToArray();
    public bool ForceVideo => video.Checked;
    public bool Playlist => pl.Checked;
    public string Quality => Vals[Math.Max(0, qual.SelectedIndex)];

    public AddDialog()
    {
        Text = "إضافة رابط تحميل";
        Width = 560; Height = 360;
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;

        var lbl = new Label { Text = "الصق الرابط (أو عدة روابط، كل رابط في سطر):", Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 8, 8, 0) };
        qual.Items.AddRange(Names); qual.SelectedIndex = 0;

        var opts = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8, 6, 8, 0), RightToLeft = RightToLeft.Yes };
        opts.Controls.Add(video); opts.Controls.Add(new Label { Text = "الجودة:", AutoSize = true, Margin = new Padding(12, 6, 3, 0) });
        opts.Controls.Add(qual); opts.Controls.Add(pl);

        var ok = new Button { Text = "ابدأ التحميل", DialogResult = DialogResult.OK, Width = 120, Height = 32 };
        var cancel = new Button { Text = "إلغاء", DialogResult = DialogResult.Cancel, Width = 90, Height = 32 };
        var btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), RightToLeft = RightToLeft.Yes };
        btns.Controls.Add(ok); btns.Controls.Add(cancel);
        CancelButton = cancel;

        Controls.Add(box);
        Controls.Add(lbl);
        Controls.Add(opts);
        Controls.Add(btns);

        try
        {
            if (Clipboard.ContainsText())
            {
                var t = Clipboard.GetText().Trim();
                if (t.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !t.Contains('\n')) box.Text = t;
            }
        }
        catch { }
        Shown += (s, e) => { box.Focus(); box.SelectAll(); };
    }
}
