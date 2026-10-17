using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RecRoomLauncher
{
    // Server settings for an existing server: choose which add-ons it uses (clothing, each community
    // room, the archive) and download published add-ons it does not have yet.
    internal sealed class ServerSettingsDialog : Form
    {
        private readonly string _serverDirectory;
        private readonly Action<string> _log;
        private readonly List<ServerAddon> _addons;
        private readonly List<KeyValuePair<CheckBox, ServerAddon>> _boxes = new List<KeyValuePair<CheckBox, ServerAddon>>();
        private readonly Label _status = Theme.Label("", Theme.F(14, Weight.Regular), Theme.Body, new Padding(0, 10, 0, 0));
        private readonly BrandProgress _progress = new BrandProgress { Margin = new Padding(0, 12, 0, 0), Visible = false };
        private readonly PillButton _save = PillButton.Primary("Save", Glyph.None, 15, 46, 4);
        private readonly PillButton _cancel;
        private bool _working;

        // True when a change only takes effect after the server restarts (community rooms).
        public bool RestartNeeded { get; private set; }

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public ServerSettingsDialog(string serverDirectory, Action<string> log)
        {
            _serverDirectory = serverDirectory;
            _log = log;
            _addons = ServerAddons.Detect(serverDirectory);

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Server settings";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Theme.Page;
            Font = Theme.F(15, Weight.Regular);
            ClientSize = new Size(700, 660);
            Theme.UseRoundCorners(this);

            var header = new ModalHeader("Server settings", "Choose which add-ons your server uses.") { Dock = DockStyle.Top, Height = 106 };
            header.MouseDown += (s, e) => {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
            };
            var close = new PillButton("", Glyph.Close, Theme.F(14, Weight.Regular)) {
                Size = new Size(34, 34), Radius = 10, IconPx = 22, SidePadding = 0, Gap = 0,
                Fill = Color.FromArgb(20, Color.White), HoverFill = Color.FromArgb(41, Color.White), Ink = Theme.Hex(0xC9D6CF), HoverInk = Color.White,
                TabStop = false
            };
            close.AccessibleName = "Close";
            close.Click += (s, e) => { if (!_working) { DialogResult = DialogResult.Cancel; Close(); } };
            header.Controls.Add(close);
            header.Resize += (s, e) => close.Location = new Point(header.Width - close.Width - (int)Theme.S(18), (int)Theme.S(18));

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(28, 20, 28, 22), AutoScroll = true };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            Theme.Add(body, Theme.Caption("Server folder", Padding.Empty));
            var folderRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            folderRow.Controls.Add(Theme.Label(serverDirectory, Theme.F(14, Weight.Regular), Theme.Ink, new Padding(0, 4, 12, 0)));
            var open = new LinkLabel { Text = "Open folder", AutoSize = true, Font = Theme.F(14, Weight.Medium), LinkColor = Theme.Green, ActiveLinkColor = Theme.Hex(0x145B45), Margin = new Padding(0, 4, 0, 0) };
            open.LinkClicked += (s, e) => { try { Process.Start("explorer.exe", "\"" + serverDirectory + "\""); } catch (Exception) { } };
            folderRow.Controls.Add(open);
            Theme.Add(body, folderRow);

            Theme.Add(body, Theme.Caption("Add-ons", new Padding(0, 22, 0, 4)));
            if (_addons.Count == 0)
                Theme.Add(body, Theme.Label("No add-ons were found in this server and none are available to download.", Theme.F(14, Weight.Regular), Theme.Body, new Padding(0, 6, 0, 0)));
            foreach (var addon in _addons)
            {
                var box = new CheckBox {
                    Text = (addon.Kind == AddonKind.Missing ? "Install " + addon.Title : addon.Title),
                    Checked = addon.Enabled, AutoSize = true, Font = Theme.F(15, Weight.Medium), ForeColor = Theme.Ink,
                    Margin = new Padding(0, 10, 0, 0), BackColor = Theme.Page
                };
                box.CheckedChanged += (s, e) => UpdateStatus();
                Theme.Add(body, box);
                Theme.Add(body, Theme.Label(addon.Detail, Theme.F(13, Weight.Regular), Theme.Subtle, new Padding(22, 2, 0, 0)));
                _boxes.Add(new KeyValuePair<CheckBox, ServerAddon>(box, addon));
            }
            Theme.Add(body, Theme.Label("Turned-off add-ons stay on disk, so you can turn them back on at any time without downloading anything.",
                Theme.F(13, Weight.Regular), Theme.Subtle, new Padding(0, 18, 0, 0)));
            Theme.Add(body, _progress);
            Theme.Add(body, _status);
            Theme.AddFill(body, new Panel { Margin = Padding.Empty });

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0, 14, 0, 0) };
            _save.Radius = 12;
            _save.SidePadding = 26;
            _save.AutoSize = true;
            _save.Margin = Padding.Empty;
            _save.Click += async (s, e) => await SaveAsync();
            _cancel = new PillButton("Cancel", Glyph.None, Theme.F(15, Weight.Medium)) {
                Fill = Color.White, HoverFill = Theme.Neutral, Edge = Theme.NeutralEdge, Height = 46, BaseHeight = 46, SidePadding = 22,
                AutoSize = true, Margin = new Padding(0, 0, 10, 4)
            };
            _cancel.Click += (s, e) => { if (!_working) { DialogResult = DialogResult.Cancel; Close(); } };
            buttons.Controls.Add(_save);
            buttons.Controls.Add(_cancel);
            Theme.Add(body, buttons);

            Controls.Add(body);
            Controls.Add(header);
            CancelButton = _cancel;
            UpdateStatus();
            ResumeLayout(false);
            PerformLayout();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_working) e.Cancel = true;   // an install is moving files; finish it first
            base.OnFormClosing(e);
        }

        private void UpdateStatus()
        {
            if (_working) return;
            _status.ForeColor = Theme.Body;
            var downloads = _boxes.Where(b => b.Key.Checked && b.Value.Kind == AddonKind.Missing).Select(b => b.Value.Download).ToList();
            var changes = _boxes.Count(b => b.Value.Kind != AddonKind.Missing && b.Key.Checked != b.Value.Enabled);
            if (downloads.Count == 0 && changes == 0) { _status.Text = "No changes."; return; }
            var parts = new List<string>();
            if (changes > 0) parts.Add(changes + (changes == 1 ? " add-on" : " add-ons") + " to switch");
            if (downloads.Count > 0)
            {
                long free;
                var needed = downloads.Sum(d => d.DownloadBytes + d.InstalledBytes);
                parts.Add("download " + Bytes.Format(downloads.Sum(d => d.DownloadBytes)) +
                    (InstallPaths.TryFreeSpace(_serverDirectory, out free) ? " (needs " + Bytes.Format(needed) + ", " + Bytes.Format(free) + " free)" : ""));
            }
            if (_boxes.Any(b => b.Value.NeedsRestart && b.Key.Checked != b.Value.Enabled) || downloads.Any(d => d.Id == "community-rooms"))
                parts.Add("room changes apply after a server restart");
            _status.Text = String.Join(" · ", parts) + ".";
        }

        private async Task SaveAsync()
        {
            if (_working) return;
            var wanted = _boxes.Where(b => b.Value.Kind != AddonKind.Missing).ToDictionary(b => b.Value.Id, b => b.Key.Checked);
            var downloads = _boxes.Where(b => b.Key.Checked && b.Value.Kind == AddonKind.Missing).Select(b => b.Value.Download).ToList();
            RestartNeeded = _boxes.Any(b => b.Value.NeedsRestart && b.Key.Checked != b.Value.Enabled) || downloads.Any(d => d.Id == "community-rooms");
            _working = true;
            _save.Enabled = false;
            _cancel.Enabled = false;
            foreach (var box in _boxes) box.Key.Enabled = false;
            try
            {
                foreach (var message in ServerAddons.Apply(_serverDirectory, _addons, wanted)) _log(message);
                if (downloads.Count > 0)
                {
                    _progress.Visible = true;
                    var lastReport = DateTime.MinValue;
                    var progress = new Progress<InstallProgress>(p => {
                        if ((DateTime.UtcNow - lastReport).TotalMilliseconds < 150 && p.Done < p.Total) return;
                        lastReport = DateTime.UtcNow;
                        _progress.Value = p.Fraction;
                        var verb = p.Stage == "download" ? "Downloading " : p.Stage == "verify" ? "Checking " : "Installing ";
                        _status.Text = verb + Bytes.Format(p.Done) + " of " + Bytes.Format(p.Total) + "  ·  " + p.Detail;
                    });
                    var directory = _serverDirectory;
                    await Task.Run(() => ServerAddons.Install(directory, downloads, progress, CancellationToken.None));
                    foreach (var group in downloads) _log("Installed the " + group.Title + " add-on.");
                }
                _working = false;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                _working = false;
                _progress.Visible = false;
                _status.ForeColor = Theme.Red;
                _status.Text = ex.Message + (downloads.Count > 0 ? " Finished downloads are kept; saving again resumes." : "");
                _log("Server settings error: " + ex.Message);
                _save.Enabled = true;
                _cancel.Enabled = true;
                foreach (var box in _boxes) box.Key.Enabled = true;
            }
        }
    }
}
