using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RecRoomLauncher
{
    // Brand-gradient progress bar on a neutral track.
    internal sealed class BrandProgress : Control
    {
        private double _value;

        public BrandProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Height = 12;
            Anchor = AnchorStyles.Left | AnchorStyles.Right;
        }

        public double Value { get { return _value; } set { _value = Math.Max(0, Math.Min(1, value)); Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Rounded(track, Height / 2f))
            using (var brush = new SolidBrush(Theme.Neutral)) g.FillPath(brush, path);
            if (_value <= 0) return;
            var fill = new RectangleF(0, 0, Math.Max(Height, (Width - 1) * (float)_value), Height - 1);
            using (var path = Theme.Rounded(fill, Height / 2f))
            using (var brush = Theme.BrandGradient(new RectangleF(0, 0, Width, Height), 0, true)) g.FillPath(brush, path);
        }
    }

    // A selectable option card (radio-button behaviour within its group).
    internal sealed class ChoiceTile : RoundedPanel
    {
        private readonly Label _title;
        private readonly Label _body;
        private readonly IconTile _icon;
        private readonly TableLayoutPanel _grid;
        private bool _selected;
        public event EventHandler Selected;
        public readonly TableLayoutPanel Extra = Theme.Stack();

        public ChoiceTile(Glyph glyph, string title, string body)
        {
            BackColor = Color.White;
            BorderColor = Theme.CardEdge;
            BorderWidth = 2;
            Radius = 14;
            Padding = new Padding(16, 14, 16, 14);
            Margin = new Padding(0, 10, 0, 0);
            Anchor = AnchorStyles.Left | AnchorStyles.Right;
            Height = 90;
            Cursor = Cursors.Hand;

            var grid = new TableLayoutPanel { ColumnCount = 2, Margin = Padding.Empty, BackColor = Color.White };
            _grid = grid;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _icon = new IconTile(glyph, Theme.Neutral, Theme.Subtle) { Size = new Size(40, 40), Margin = new Padding(0, 0, 14, 0), Anchor = AnchorStyles.Left | AnchorStyles.Top };
            grid.Controls.Add(_icon, 0, 0);
            grid.SetRowSpan(_icon, 3);
            _title = Theme.Label(title, Theme.F(16, Weight.Medium), Theme.Ink, Padding.Empty);
            _body = Theme.Label(body, Theme.F(14, Weight.Regular), Theme.Body, new Padding(0, 4, 0, 0));
            grid.Controls.Add(_title, 1, 0);
            grid.Controls.Add(_body, 1, 1);
            Extra.Dock = DockStyle.None;
            Extra.AutoSize = true;
            Extra.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            Extra.BackColor = Color.White;
            Extra.Visible = false;
            grid.Controls.Add(Extra, 1, 2);
            Controls.Add(grid);
            foreach (var control in new Control[] { this, grid, _title, _body, _icon }) control.Click += (s, e) => Select();
        }

        // Size to the content: the grid fills the width and the tile grows to its preferred height.
        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_grid == null || Width <= 0) return;
            var width = Math.Max(0, Width - Padding.Horizontal);
            _grid.SetBounds(Padding.Left, Padding.Top, width, _grid.Height);
            var height = _grid.GetPreferredSize(new Size(width, 0)).Height;
            if (_grid.Height != height) _grid.Height = height;
            var wanted = height + Padding.Vertical;
            if (Height != wanted) Height = wanted;
        }

        public bool IsSelected { get { return _selected; } }

        public new void Select()
        {
            if (_selected) return;
            SetSelected(true);
            var handler = Selected;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            BorderColor = selected ? Theme.Green : Theme.CardEdge;
            _icon.Set(_icon.Icon, selected ? Theme.GreenTint : Theme.Neutral, selected ? Theme.GreenIcon : Theme.Subtle);
            Extra.Visible = selected && Extra.Controls.Count > 0;
            PerformLayout();
            Invalidate();
        }

        public static void Group(params ChoiceTile[] tiles)
        {
            foreach (var tile in tiles)
            {
                var current = tile;
                current.Selected += (s, e) => { foreach (var other in tiles) if (other != current) other.SetSelected(false); };
            }
        }
    }

    // A setup page: scrolling body with a fixed button footer, so actions never scroll out of view.
    internal sealed class SetupPage : TableLayoutPanel
    {
        public readonly TableLayoutPanel Content = Theme.Stack();
        private readonly FlowLayoutPanel _footer = new FlowLayoutPanel {
            AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 14, 0, 0), BackColor = Color.White
        };

        public SetupPage()
        {
            Dock = DockStyle.Fill;
            ColumnCount = 1;
            BackColor = Color.White;
            Margin = Padding.Empty;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Margin = Padding.Empty };
            Content.Dock = DockStyle.None;
            Content.AutoSize = false;
            Content.BackColor = Color.White;
            Content.Padding = new Padding(0, 0, 8, 6);
            scroller.Controls.Add(Content);
            Controls.Add(scroller, 0, 0);
            Controls.Add(_footer, 0, 1);
            _scroller = scroller;
            // Size the body explicitly: WinForms keeps stale scroll ranges for docked or hidden children.
            scroller.Layout += (s, e) => FitContent();
            Content.Layout += (s, e) => FitContent();
        }

        private readonly Panel _scroller;
        private bool _fitting;

        private void FitContent()
        {
            // Hidden pages sit at the default 200px width; fitting them then records a huge scroll range.
            if (_fitting || !Visible || _scroller.Width < 100) return;
            _fitting = true;
            try
            {
                // Leave room for the vertical scrollbar up front so content never needs a horizontal one.
                var width = _scroller.Width;
                var height = Content.GetPreferredSize(new Size(width, 0)).Height;
                if (height > _scroller.Height)
                {
                    width -= SystemInformation.VerticalScrollBarWidth;
                    height = Content.GetPreferredSize(new Size(width, 0)).Height;
                }
                var origin = _scroller.AutoScrollPosition;
                if (Content.Bounds != new Rectangle(origin.X, origin.Y, width, height)) Content.SetBounds(origin.X, origin.Y, width, height);
                if (_scroller.AutoScrollMinSize.Height != height) _scroller.AutoScrollMinSize = new Size(0, height);
                // WinForms never shrinks an existing scroll range during layout; rebuild it afterwards.
                if (_scroller.DisplayRectangle.Height > Math.Max(height, _scroller.ClientSize.Height) + 1 && !_resetQueued && IsHandleCreated)
                {
                    _resetQueued = true;
                    BeginInvoke((Action)ResetScrollRange);
                }
            }
            finally { _fitting = false; }
        }

        private bool _resetQueued;

        private void ResetScrollRange()
        {
            _resetQueued = false;
            _scroller.AutoScroll = false;
            _scroller.AutoScroll = true;
            FitContent();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && IsHandleCreated) BeginInvoke((Action)FitContent);
        }

        public void Add(Control control) { Theme.Add(Content, control); }

        public void SetButtons(params Control[] buttons)
        {
            foreach (var button in buttons) _footer.Controls.Add(button);
        }
    }

    // First-run setup shown inside the launcher window: install the pinned game build, apply the
    // bundled patch, then optionally set up a local server.
    internal sealed class SetupView : Panel
    {
        private enum Source { Steam, Folder, Existing }

        private readonly Func<string, string, string> _browseFolder;
        private readonly Action<string> _log;
        private readonly Action<string, string> _finished;
        private readonly string _knownServer;

        private readonly RoundedPanel _card;
        private readonly Label _step = Theme.Caption("", Padding.Empty);
        private readonly Panel _pages = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

        // Game page
        private readonly InputField _destination = new InputField(null, 15, 46, Theme.Neutral);
        private readonly InputField _sourceFolder = new InputField(null, 15, 46, Theme.Neutral);
        private readonly InputField _existingFolder = new InputField(null, 15, 46, Theme.Neutral);
        private ChoiceTile _steamChoice, _folderChoice, _existingChoice;
        private readonly Label _gameMessage = Theme.Label("", Theme.F(14, Weight.Regular), Theme.Red, new Padding(0, 10, 0, 0));
        private readonly Label _spaceInfo = Theme.Label("", Theme.F(14, Weight.Regular), Theme.Subtle, new Padding(0, 8, 0, 0));

        // Steam page
        private readonly BrandProgress _downloadBar = new BrandProgress { Margin = new Padding(0, 18, 0, 0) };
        private readonly Label _downloadText = Theme.Label("", Theme.F(15, Weight.Regular), Theme.Body, new Padding(0, 10, 0, 0));
        private readonly System.Windows.Forms.Timer _downloadTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        private string _depotDirectory;
        private bool _depotExisted;
        private DateTime _lastChange;
        private long _lastPresent = -1;

        // Install page
        private readonly string[] _stepNames = { "Verify game files", "Copy to the install folder", "Install the patch", "Check the patched game", "Referee component" };
        private readonly IconTile[] _stepMarks = new IconTile[5];
        private readonly Label[] _stepTexts = new Label[5];
        private readonly BrandProgress _installBar = new BrandProgress { Margin = new Padding(0, 20, 0, 0) };
        private readonly Label _installText = Theme.Label("", Theme.F(14, Weight.Regular), Theme.Body, new Padding(0, 10, 0, 0));
        private readonly Label _installError = Theme.Label("", Theme.F(14, Weight.Regular), Theme.Red, new Padding(0, 12, 0, 0));
        private PillButton _installRetry, _installBack, _installNext;
        private CancellationTokenSource _cancel;
        private Source _source;
        private string _origin;
        private string _gamePath;

        // Server page
        private ChoiceTile _serverExisting, _serverPackage, _serverSkip;
        private readonly InputField _serverFolder = new InputField(null, 15, 46, Theme.Neutral);
        private readonly InputField _serverZip = new InputField(null, 15, 46, Theme.Neutral);
        private readonly InputField _serverDestination = new InputField(null, 15, 46, Theme.Neutral);
        private readonly BrandProgress _serverBar = new BrandProgress { Margin = new Padding(0, 14, 0, 0), Visible = false };
        private readonly Label _serverMessage = Theme.Label("", Theme.F(14, Weight.Regular), Theme.Red, new Padding(0, 10, 0, 0));
        private PillButton _serverNext;
        private string _serverPath;

        // Done page
        private readonly Label _doneSummary = Theme.Label("", Theme.F(15, Weight.Regular), Theme.Body, new Padding(0, 12, 0, 0));

        private readonly Dictionary<string, Control> _pageMap = new Dictionary<string, Control>();

        public SetupView(string notice, string knownServer, Func<string, string, string> browseFolder, Action<string> log, Action<string, string> finished, string resumeGame)
        {
            _browseFolder = browseFolder;
            _log = log;
            _finished = finished;
            _knownServer = knownServer;
            Dock = DockStyle.Fill;
            BackColor = Theme.Page;
            Padding = new Padding(28, 24, 28, 28);

            _card = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Color.White, BorderColor = Theme.CardEdge, Radius = 18, Padding = new Padding(34, 26, 34, 26) };
            var frame = Theme.Stack();
            frame.BackColor = Color.White;
            Theme.Add(frame, _step);
            Theme.AddFill(frame, _pages);
            _card.Controls.Add(frame);
            Controls.Add(_card);

            _pageMap["game"] = BuildGamePage(notice);
            _pageMap["steam"] = BuildSteamPage();
            _pageMap["install"] = BuildInstallPage();
            _pageMap["server"] = BuildServerPage();
            _pageMap["done"] = BuildDonePage();
            foreach (var page in _pageMap.Values) { page.Visible = false; _pages.Controls.Add(page); }
            _downloadTimer.Tick += (s, e) => PollDownload();
            if (resumeGame != null)
            {
                // The game was installed and patched last time; only the server step is left.
                _gamePath = resumeGame;
                EnterServerPage();
            }
            else ShowPage("game");
        }

        private void ShowPage(string name)
        {
            foreach (var pair in _pageMap) pair.Value.Visible = pair.Key == name;
            switch (name)
            {
                case "game": _step.Text = "SETUP · STEP 1 OF 3 · REC ROOM"; break;
                case "steam": _step.Text = "SETUP · STEP 1 OF 3 · DOWNLOAD"; break;
                case "install": _step.Text = "SETUP · STEP 2 OF 3 · INSTALL AND PATCH"; break;
                case "server": _step.Text = "SETUP · STEP 3 OF 3 · SERVER"; break;
                default: _step.Text = "SETUP COMPLETE"; break;
            }
        }

        private static Label Title(string text) { return Theme.Label(text, Theme.F(30, Weight.Medium), Theme.Ink, new Padding(0, 14, 0, 0)); }
        private static Label Body(string text) { return Theme.Label(text, Theme.F(16, Weight.Regular), Theme.Body, new Padding(0, 10, 0, 0)); }
        private static Label FieldLabel(string text) { return Theme.Label(text, Theme.F(14, Weight.Medium), Theme.LabelInk, new Padding(0, 18, 0, 8)); }

        private Control FolderRow(InputField field, string browseDescription, bool pickFile)
        {
            var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty, BackColor = Color.White };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            field.Margin = new Padding(0, 0, 8, 0);
            row.Controls.Add(field, 0, 0);
            var pick = PillButton.Tinted("Browse", Glyph.Folder, Theme.GreenTint, Theme.GreenTintHover, Theme.Green);
            pick.Font = Theme.F(14, Weight.Medium);
            pick.Height = 46; pick.BaseHeight = 46; pick.Radius = 10; pick.Gap = 8; pick.SidePadding = 16; pick.Margin = Padding.Empty;
            pick.Click += (s, e) => {
                string chosen;
                if (pickFile)
                {
                    using (var dialog = new OpenFileDialog { Title = browseDescription, Filter = "Server package (*.zip)|*.zip" })
                        chosen = dialog.ShowDialog(FindForm()) == DialogResult.OK ? dialog.FileName : null;
                }
                else chosen = _browseFolder(browseDescription, field.Value);
                if (chosen != null) field.Value = chosen;
            };
            row.Controls.Add(pick, 1, 0);
            return row;
        }

        private static PillButton Primary(string text, Glyph glyph)
        {
            var button = PillButton.Primary(text, glyph, 17, 54, 4);
            button.AutoSize = true;
            button.SidePadding = 30;
            button.Margin = Padding.Empty;
            return button;
        }

        private static PillButton Secondary(string text)
        {
            return new PillButton(text, Glyph.None, Theme.F(15, Weight.Medium)) {
                Fill = Color.White, HoverFill = Theme.Neutral, Edge = Theme.NeutralEdge, Height = 50, BaseHeight = 50, SidePadding = 22,
                AutoSize = true, Margin = new Padding(0, 0, 10, 4)
            };
        }

        // ---------- Step 1: choose the game source and destination ----------

        private Control BuildGamePage(string notice)
        {
            var page = new SetupPage();
            page.Add(Title("Install Rec Room"));
            page.Add(Body("This launcher plays Rec Room " + ClientBuild.Version + " with the community patch. Choose where to install it. Nothing is changed in your Steam library."));
            if (!String.IsNullOrEmpty(notice))
            {
                var warn = Theme.Label(notice, Theme.F(14, Weight.Medium), Theme.Gold, new Padding(0, 10, 0, 0));
                page.Add(warn);
            }

            _steamChoice = new ChoiceTile(Glyph.Arrow, "Download with Steam (recommended)",
                "Your signed-in Steam client downloads exactly this build (" + Bytes.Format(ClientBuild.DownloadBytes) + " download, " + Bytes.Format(ClientBuild.TotalBytes) + " installed) with the Steam console command " + ClientBuild.SteamCommand + ". The launcher never asks for your Steam password.");
            _folderChoice = new ChoiceTile(Glyph.Folder, "Use game files I already have",
                "Copy a complete, unmodified " + ClientBuild.Version + " folder you downloaded before. Every file is checked first.");
            Theme.Add(_folderChoice.Extra, FieldLabel("Folder with the game files (contains RecRoom.exe)"));
            Theme.Add(_folderChoice.Extra, FolderRow(_sourceFolder, "Select the folder containing Rec Room " + ClientBuild.Version, false));
            _existingChoice = new ChoiceTile(Glyph.Check, "I already have a patched copy",
                "Keep an installation you already play with. Its files are checked and the bundled patch is applied in place; an older injector patch is replaced (its files are backed up) and your connection settings are kept.");
            Theme.Add(_existingChoice.Extra, FieldLabel("Patched Rec Room folder"));
            Theme.Add(_existingChoice.Extra, FolderRow(_existingFolder, "Select your Rec Room folder (contains RecRoom.exe)", false));
            ChoiceTile.Group(_steamChoice, _folderChoice, _existingChoice);
            foreach (var tile in new[] { _steamChoice, _folderChoice, _existingChoice }) tile.Selected += (s, e) => UpdateGamePage();

            page.Add(_steamChoice);
            page.Add(_folderChoice);
            page.Add(_existingChoice);

            var destinationLabel = FieldLabel("Install folder");
            var destinationRow = FolderRow(_destination, "Choose an empty folder for Rec Room", false);
            _destination.Value = InstallPaths.DefaultGame;
            _destination.Box.TextChanged += (s, e) => UpdateGamePage();
            page.Add(destinationLabel);
            page.Add(destinationRow);
            page.Add(_spaceInfo);
            page.Add(_gameMessage);

            var next = Primary("Continue", Glyph.Arrow);
            next.Click += (s, e) => ContinueFromGame(destinationLabel, destinationRow);
            page.SetButtons(next);

            _steamChoice.SetSelected(true);
            _existingChoice.Selected += (s, e) => { destinationLabel.Visible = destinationRow.Visible = false; };
            _steamChoice.Selected += (s, e) => { destinationLabel.Visible = destinationRow.Visible = true; };
            _folderChoice.Selected += (s, e) => { destinationLabel.Visible = destinationRow.Visible = true; };
            UpdateGamePage();
            return page;
        }

        private void UpdateGamePage()
        {
            _gameMessage.Text = "";
            if (_existingChoice != null && _existingChoice.IsSelected) { _spaceInfo.Text = ""; return; }
            long free;
            var path = _destination.Value;
            _spaceInfo.Text = InstallPaths.TryFreeSpace(path, out free)
                ? "Needs " + Bytes.Format(ClientBuild.TotalBytes) + " · " + Bytes.Format(free) + " free on " + Path.GetPathRoot(InstallPaths.Normalize(path))
                : "Needs " + Bytes.Format(ClientBuild.TotalBytes);
        }

        private void ContinueFromGame(Control destinationLabel, Control destinationRow)
        {
            _gameMessage.Text = "";
            _origin = null;
            if (_existingChoice.IsSelected)
            {
                var folder = _existingFolder.Value;
                if (!File.Exists(Path.Combine(folder, "RecRoom.exe"))) { _gameMessage.Text = "Choose the folder that contains RecRoom.exe."; return; }
                // The patch renames Referee.dll; a Steam-managed install would have it restored on the next update or file check.
                if (InstallPaths.Normalize(folder).IndexOf(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _gameMessage.Text = "Steam manages that folder and would undo the patch. Choose \"Use game files I already have\" to make a separate copy instead.";
                    return;
                }
                _source = Source.Existing;
                _gamePath = InstallPaths.Normalize(folder);
                StartInstall(_gamePath);
                return;
            }
            var problem = InstallPaths.ValidateFreshDestination(_destination.Value, ClientBuild.TotalBytes + (64L << 20));
            if (problem != null) { _gameMessage.Text = problem; return; }
            _gamePath = InstallPaths.Normalize(_destination.Value);
            if (_folderChoice.IsSelected)
            {
                var source = _sourceFolder.Value;
                string missing;
                if (!GameFiles.LooksComplete(source, out missing)) { _gameMessage.Text = "That folder is not a complete " + ClientBuild.Version + " build. " + missing; return; }
                _source = Source.Folder;
                StartInstall(InstallPaths.Normalize(source));
                return;
            }
            _source = Source.Steam;
            EnterSteamPage();
        }

        // ---------- Step 1b: Steam console download ----------

        private Control BuildSteamPage()
        {
            var page = new SetupPage();
            page.Add(Title("Download with Steam"));
            page.Add(Body("Steam downloads this exact build through its developer console. Make sure Steam is open and you're signed in, then:"));
            var steps = new[] {
                "Click Open Steam console below. It opens Steam's console tab and copies the command for you. (You can also open it yourself: paste steam://open/console into your browser's address bar or Windows Run.)",
                "In Steam's console, type or paste this command and press Enter:\n        " + ClientBuild.SteamCommand,
                "Keep Steam open. The console prints \"Depot download complete\" when done; setup continues on its own."
            };
            for (var i = 0; i < steps.Length; i++)
                page.Add(Theme.Label((i + 1) + ".  " + steps[i], Theme.F(15, Weight.Regular), Theme.Ink, new Padding(0, i == 0 ? 16 : 8, 0, 0)));

            page.Add(FieldLabel("Download command"));
            var command = new InputField(null, 15, 46, Theme.Neutral);
            command.Value = ClientBuild.SteamCommand;
            command.Box.ReadOnly = true;
            var copy = PillButton.Tinted("Copy", Glyph.Check, Theme.GreenTint, Theme.GreenTintHover, Theme.Green);
            copy.Height = 46; copy.BaseHeight = 46; copy.Radius = 10; copy.Margin = Padding.Empty;
            copy.Click += (s, e) => { CopyCommand(); _downloadText.Text = "Command copied. Paste it into Steam's console."; };
            var commandRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty, BackColor = Color.White };
            commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            command.Margin = new Padding(0, 0, 8, 0);
            commandRow.Controls.Add(command, 0, 0);
            commandRow.Controls.Add(copy, 1, 0);
            page.Add(commandRow);

            page.Add(_downloadBar);
            page.Add(_downloadText);
            page.Add(Theme.Label("Steam saves the files in its own download folder first; setup then moves or copies them into your install folder and checks every file.", Theme.F(13, Weight.Regular), Theme.Subtle, new Padding(0, 14, 0, 0)));

            var open = Primary("Open Steam console", Glyph.Play);
            open.Click += (s, e) => OpenSteamConsole();
            var back = Secondary("Back");
            back.Click += (s, e) => { _downloadTimer.Stop(); ShowPage("game"); };
            page.SetButtons(open, back);
            return page;
        }

        private void CopyCommand()
        {
            try { Clipboard.SetText(ClientBuild.SteamCommand); }
            catch (Exception) { }
        }

        private void EnterSteamPage()
        {
            _depotDirectory = SteamDepot.OutputDirectory();
            if (_depotDirectory == null)
            {
                _gameMessage.Text = "Steam was not found on this PC. Install Steam and sign in, or use game files you already have.";
                return;
            }
            _depotExisted = Directory.Exists(_depotDirectory) && Directory.EnumerateFileSystemEntries(_depotDirectory).Any();
            _lastPresent = -1;
            _lastChange = DateTime.UtcNow;
            _downloadBar.Value = 0;
            _downloadText.Text = _depotExisted
                ? "Steam's download folder already has files for this depot. They will be checked; nothing there is moved or deleted."
                : "Waiting for the download to start…";
            ShowPage("steam");
            _downloadTimer.Start();
            PollDownload();
        }

        private void OpenSteamConsole()
        {
            CopyCommand();
            try
            {
                if (!SteamDepot.IsRunning())
                {
                    SteamDepot.StartSteam();
                    _downloadText.Text = "Starting Steam. Sign in if asked, then click Open Steam console again.";
                    return;
                }
                SteamDepot.OpenConsole();
                _downloadText.Text = "Steam's console is open and the command is copied. Paste it and press Enter.";
                _log("Opened the Steam console for: " + ClientBuild.SteamCommand);
            }
            catch (Exception ex) { _downloadText.Text = ex.Message; }
        }

        // Steam reports nothing to other programs, so watch the depot folder until every file is
        // present at full size and nothing has been written for a while. Verification decides.
        private bool _polling;

        private async void PollDownload()
        {
            if (_polling || !_pageMap["steam"].Visible) return;
            _polling = true;
            try { await PollDownloadOnce(); }
            finally { _polling = false; }
        }

        private async Task PollDownloadOnce()
        {
            var dir = _depotDirectory;
            long present = 0;
            var complete = false;
            DateTime newest = DateTime.MinValue;
            await Task.Run(() => {
                present = GameFiles.PresentBytes(dir);
                string ignored;
                complete = GameFiles.LooksComplete(dir, out ignored);
                if (Directory.Exists(dir))
                    foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        var time = File.GetLastWriteTimeUtc(file);
                        if (time > newest) newest = time;
                    }
            });
            if (!_pageMap["steam"].Visible) return;
            if (present != _lastPresent) { _lastPresent = present; _lastChange = DateTime.UtcNow; }
            if (newest > _lastChange) _lastChange = newest;
            _downloadBar.Value = (double)present / ClientBuild.TotalBytes;
            var quiet = DateTime.UtcNow - _lastChange;
            if (complete && quiet > TimeSpan.FromSeconds(15))
            {
                _downloadTimer.Stop();
                _downloadText.Text = "Download finished. Checking files…";
                _log("Steam finished downloading Rec Room " + ClientBuild.Version + ".");
                StartInstall(_depotDirectory);
                return;
            }
            if (present > 0)
                _downloadText.Text = (complete ? "All files are present; waiting for Steam to finish writing… " : "Downloading… ") +
                    Bytes.Format(present) + " of " + Bytes.Format(ClientBuild.TotalBytes);
        }

        // ---------- Step 2: verify, place, patch ----------

        private Control BuildInstallPage()
        {
            var page = new SetupPage();
            page.Add(Title("Installing Rec Room"));
            page.Add(Body("Every file is checked against Steam's list for this build before the patch is applied."));
            var list = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 20, 0, 0), BackColor = Color.White };
            list.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < _stepNames.Length; i++)
            {
                list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _stepMarks[i] = new IconTile(Glyph.None, Theme.Neutral, Theme.Subtle) { Circle = true, IconFraction = 13f / 24f, Size = new Size(24, 24), Margin = new Padding(0, 0, 14, 12), Anchor = AnchorStyles.Left };
                _stepTexts[i] = Theme.Label(_stepNames[i], Theme.F(15, Weight.Regular), Theme.Subtle, new Padding(0, 0, 0, 12));
                list.Controls.Add(_stepMarks[i], 0, i);
                list.Controls.Add(_stepTexts[i], 1, i);
            }
            page.Add(list);
            page.Add(_installBar);
            page.Add(_installText);
            page.Add(_installError);

            _installNext = Primary("Continue", Glyph.Arrow);
            _installNext.Visible = false;
            _installNext.Click += (s, e) => EnterServerPage();
            _installRetry = Secondary("Retry");
            _installRetry.Visible = false;
            _installRetry.Click += (s, e) => StartInstall(_retrySource);
            _installBack = Secondary("Back");
            _installBack.Click += (s, e) => { if (_cancel != null) _cancel.Cancel(); else ShowPage("game"); };
            page.SetButtons(_installNext, _installRetry, _installBack);
            return page;
        }

        private string _retrySource;

        private void SetStep(int index, Glyph glyph, string text)
        {
            Color tint = Theme.Neutral, ink = Theme.Subtle, textColor = Theme.Subtle;
            if (glyph == Glyph.Check) { tint = Theme.GreenTint; ink = Theme.GreenIcon; textColor = Theme.Ink; }
            else if (glyph == Glyph.Cross) { tint = Theme.RedTint; ink = Theme.Red; textColor = Theme.Red; }
            else if (glyph == Glyph.Alert) { tint = Theme.GoldTint; ink = Theme.Gold; textColor = Theme.Ink; }
            else if (glyph == Glyph.Refresh) { tint = Theme.GreenTint; ink = Theme.GreenIcon; textColor = Theme.Ink; }
            _stepMarks[index].Set(glyph, tint, ink);
            _stepTexts[index].ForeColor = textColor;
            _stepTexts[index].Text = text ?? _stepNames[index];
        }

        private async void StartInstall(string source)
        {
            _retrySource = source;
            if (_origin == null) _origin = _source.ToString().ToLowerInvariant();
            for (var i = 0; i < _stepNames.Length; i++) SetStep(i, Glyph.None, null);
            _installError.Text = "";
            _installText.Text = "";
            _installBar.Value = 0;
            _installNext.Visible = false;
            _installRetry.Visible = false;
            _installBack.Text = "Cancel";
            ShowPage("install");
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            var current = 0;
            var lastReport = DateTime.MinValue;
            var progress = new Progress<InstallProgress>(p => {
                if ((DateTime.UtcNow - lastReport).TotalMilliseconds < 120 && p.Done < p.Total) return;
                lastReport = DateTime.UtcNow;
                _installBar.Value = p.Fraction;
                _installText.Text = Bytes.Format(p.Done) + " of " + Bytes.Format(p.Total) + "  ·  " + p.Detail;
            });
            try
            {
                // 1. Verify the source against Steam's manifest.
                current = 0;
                SetStep(0, Glyph.Refresh, "Verifying " + ClientBuild.Files.Count + " game files…");
                var failed = await Task.Run(() => GameFiles.Verify(source, progress, token));
                if (failed.Count > 0)
                    throw new InvalidOperationException(failed.Count + " file(s) are missing or don't match this build, e.g. " + String.Join(", ", failed.Take(3)) +
                        (_source == Source.Steam ? ". If Steam is still downloading, wait for it to finish and click Retry." : "."));
                SetStep(0, Glyph.Check, "All " + ClientBuild.Files.Count + " game files match build " + ClientBuild.Version);

                // 2. Place the files in the chosen folder.
                current = 1;
                if (_source == Source.Existing)
                    SetStep(1, Glyph.Check, "Using your existing folder in place");
                else
                {
                    var move = _source == Source.Steam && !_depotExisted && InstallPaths.SameVolume(source, _gamePath);
                    SetStep(1, Glyph.Refresh, move ? "Moving files into " + _gamePath + "…" : "Copying files into " + _gamePath + "…");
                    var target = _gamePath;
                    await Task.Run(() => PlaceFiles(source, target, move, progress, token));
                    SetStep(1, Glyph.Check, (move ? "Moved into " : "Copied into ") + _gamePath);
                    // From here on the files live in the install folder; a retry continues in place.
                    _source = Source.Existing;
                    _retrySource = _gamePath;
                }

                // 3. Patch.
                current = 2;
                SetStep(2, Glyph.Refresh, "Installing " + PatchPackage.Label + "…");
                var game = _gamePath;
                await Task.Run(() => PatchPackage.Install(game, message => _log(message)));
                SetStep(2, Glyph.Check, "Installed " + PatchPackage.Label);

                // 4. Check the installed files (hashes only; nothing is run).
                current = 3;
                SetStep(3, Glyph.Refresh, "Checking the patched game files…");
                var preflight = await Task.Run(() => PatchPackage.RunPreflight(game));
                SetStep(3, Glyph.Check, preflight.Length > 0 ? preflight : "Patch check passed");

                // 5. Referee (Steam's install script normally does this).
                current = 4;
                if (Referee.IsInstalled()) SetStep(4, Glyph.Check, "Referee component is already installed");
                else
                {
                    var answer = MessageBox.Show(FindForm(),
                        "Steam normally runs Rec Room's RefereeClientInstaller when it installs the game. Run it now?\n\nWindows will ask for administrator approval.",
                        "Referee component", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (answer == DialogResult.Yes)
                    {
                        SetStep(4, Glyph.Refresh, "Installing the Referee component…");
                        try
                        {
                            var code = await Referee.Install(game);
                            SetStep(4, code == 0 && Referee.IsInstalled() ? Glyph.Check : Glyph.Alert,
                                code == 0 && Referee.IsInstalled() ? "Referee component installed" : "Referee installer exited with code " + code + "; the game may still run");
                        }
                        catch (System.ComponentModel.Win32Exception) { SetStep(4, Glyph.Alert, "Referee install was cancelled; you can run it later from the game folder"); }
                    }
                    else SetStep(4, Glyph.Alert, "Skipped the Referee component");
                }

                var state = InstallState.Load() ?? new InstallState();
                state.Game = new GameState {
                    Path = game, Build = ClientBuild.Version, ManifestId = ClientBuild.ManifestId, Status = "ready",
                    Patch = PatchPackage.Label, Mode = "referee", Source = _origin
                };
                if (state.Server == null) state.Server = new ServerStateRecord { Choice = "undecided", Status = "notInstalled" };
                state.SetupComplete = false;
                state.Save();
                _log("Rec Room " + ClientBuild.Version + " is installed and patched in " + game + ".");
                _installBar.Value = 1;
                _installText.Text = "Rec Room is installed and patched.";
                _installNext.Visible = true;
                _installBack.Visible = false;
            }
            catch (OperationCanceledException)
            {
                SetStep(current, Glyph.Cross, _stepNames[current] + " — cancelled");
                _installError.Text = "Setup was cancelled. Nothing was promoted to the install folder.";
                _installRetry.Visible = true;
                _installBack.Text = "Back";
            }
            catch (Exception ex)
            {
                SetStep(current, Glyph.Cross, _stepNames[current] + " — failed");
                _installError.Text = ex.Message;
                _installRetry.Visible = true;
                _installBack.Text = "Back";
                _log("Setup error: " + ex.Message);
            }
            finally
            {
                _cancel = null;
                if (_installBack.Text == "Cancel") _installBack.Text = "Back";
            }
        }

        // Copy into a staging folder and promote it, or move Steam's fresh download (same drive only).
        private static void PlaceFiles(string source, string target, bool move, IProgress<InstallProgress> progress, CancellationToken token)
        {
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                throw new InvalidOperationException("The install folder is no longer empty.");
            var parent = Path.GetDirectoryName(target);
            if (parent != null) Directory.CreateDirectory(parent);
            if (Directory.Exists(target)) Directory.Delete(target);
            if (move)
            {
                Directory.Move(source, target);
                progress.Report(new InstallProgress("move", 1, 1, "Moved"));
                return;
            }
            var staging = target + ".installing";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            try
            {
                GameFiles.CopyBuild(source, staging, progress, token);
                string problem;
                if (!GameFiles.LooksComplete(staging, out problem)) throw new InvalidOperationException("The copy is incomplete. " + problem);
                Directory.Move(staging, target);
            }
            catch (Exception)
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (Exception) { }
                throw;
            }
        }

        // ---------- Step 3: server ----------

        private readonly Label _noServerNotice = Theme.Label(
            "You will not be able to play Rec Room without a server. Install one here, or have the address of someone else's running server ready before you skip.",
            Theme.F(15, Weight.Medium), Theme.Gold, new Padding(0, 12, 0, 0));
        private readonly RadioButton _sourceDownload = new RadioButton();
        private readonly RadioButton _sourceLocal = new RadioButton();
        private readonly Label _packageInfo = Theme.Label("", Theme.F(14, Weight.Regular), Theme.Body, new Padding(0, 10, 0, 0));
        private readonly TableLayoutPanel _packList = Theme.Stack();
        private readonly CheckBox _deleteDownloads = new CheckBox();
        private readonly Label _packageSpace = Theme.Label("", Theme.F(14, Weight.Regular), Theme.Subtle, new Padding(0, 8, 0, 0));
        private readonly Label _serverProgressText = Theme.Label("", Theme.F(13, Weight.Regular), Theme.Body, new Padding(0, 6, 0, 0));
        private Control _localZipRow, _localZipLabel;
        private ServerPackage _core;
        private readonly List<KeyValuePair<CheckBox, PackGroup>> _packChoices = new List<KeyValuePair<CheckBox, PackGroup>>();

        private Control BuildServerPage()
        {
            var page = new SetupPage();
            page.Add(Title("Install a server on this PC?"));
            page.Add(Body("Rec Room needs a RecToon server to play. A server on this PC lets you host and play here; friends can join it if it is reachable from their network."));
            page.Add(_noServerNotice);

            _serverPackage = new ChoiceTile(Glyph.Folder, "Install the RecToon server (recommended)",
                "Installs the server with the content you pick below. Every file is checked, a new HTTPS certificate is created for this PC, and Node.js is included.");
            foreach (var radio in new[] { _sourceDownload, _sourceLocal })
            {
                radio.AutoSize = true;
                radio.Font = Theme.F(14, Weight.Regular);
                radio.ForeColor = Theme.Ink;
                radio.BackColor = Color.White;
                radio.Margin = new Padding(0, 8, 0, 0);
                radio.CheckedChanged += (s, e) => { if (((RadioButton)s).Checked) LoadPackageInfo(); };
            }
            _sourceDownload.Text = "Download from the RecToon release " + ServerRelease.Tag + " on GitHub (needs internet)";
            _sourceLocal.Text = "Use server package files already on this PC";
            Theme.Add(_serverPackage.Extra, FieldLabel("Source"));
            Theme.Add(_serverPackage.Extra, _sourceDownload);
            Theme.Add(_serverPackage.Extra, _sourceLocal);
            _localZipLabel = FieldLabel("Core package (…-core.zip; packs beside it are found automatically)");
            _localZipRow = FolderRow(_serverZip, "Select the RecToon server core package (…-core.zip)", true);
            Theme.Add(_serverPackage.Extra, _localZipLabel);
            Theme.Add(_serverPackage.Extra, _localZipRow);
            Theme.Add(_serverPackage.Extra, FieldLabel("Content"));
            Theme.Add(_serverPackage.Extra, _packageInfo);
            _packList.Dock = DockStyle.None;
            _packList.AutoSize = true;
            _packList.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _packList.BackColor = Color.White;
            _packList.Margin = new Padding(0, 2, 0, 0);
            Theme.Add(_serverPackage.Extra, _packList);
            Theme.Add(_serverPackage.Extra, FieldLabel("Install folder"));
            Theme.Add(_serverPackage.Extra, FolderRow(_serverDestination, "Choose an empty folder for the server", false));
            _deleteDownloads.Text = "Delete the downloaded package files after the server is installed";
            _deleteDownloads.Checked = true;
            _deleteDownloads.AutoSize = true;
            _deleteDownloads.Font = Theme.F(14, Weight.Regular);
            _deleteDownloads.ForeColor = Theme.Ink;
            _deleteDownloads.BackColor = Color.White;
            _deleteDownloads.Margin = new Padding(0, 10, 0, 0);
            _deleteDownloads.CheckedChanged += (s, e) => UpdatePackageSpace();
            Theme.Add(_serverPackage.Extra, _deleteDownloads);
            Theme.Add(_serverPackage.Extra, _packageSpace);
            _serverDestination.Value = InstallPaths.DefaultServer;
            _serverZip.Box.TextChanged += (s, e) => { if (_sourceLocal.Checked) LoadPackageInfo(); };
            _serverDestination.Box.TextChanged += (s, e) => UpdatePackageSpace();

            _serverExisting = new ChoiceTile(Glyph.Home, "Use a RecToon server folder on this PC", "Point the launcher at a server you already have (the folder that contains server.js).");
            Theme.Add(_serverExisting.Extra, FieldLabel("Server folder"));
            Theme.Add(_serverExisting.Extra, FolderRow(_serverFolder, "Select the folder containing server.js", false));

            _serverSkip = new ChoiceTile(Glyph.Globe, "Skip — I'll join someone else's server",
                "Only choose this if you have the address of another RecToon server that is running. Without a server you cannot play.");
            ChoiceTile.Group(_serverPackage, _serverExisting, _serverSkip);
            foreach (var tile in new[] { _serverPackage, _serverExisting, _serverSkip })
                tile.Selected += (s, e) => _noServerNotice.ForeColor = _serverSkip.IsSelected ? Theme.Red : Theme.Gold;
            page.Add(_serverPackage);
            page.Add(_serverExisting);
            page.Add(_serverSkip);
            page.Add(_serverBar);
            page.Add(_serverProgressText);
            page.Add(_serverMessage);
            _serverNext = Primary("Finish setup", Glyph.Check);
            _serverNext.Click += async (s, e) => await ContinueFromServer();
            page.SetButtons(_serverNext);
            return page;
        }

        private void EnterServerPage()
        {
            _serverMessage.Text = "";
            _serverProgressText.Text = "";
            var bundled = ServerSetup.FindBundledCore();
            if (String.IsNullOrEmpty(_serverZip.Value) && bundled != null) _serverZip.Value = bundled;
            if (bundled != null) _sourceLocal.Checked = true;
            else _sourceDownload.Checked = true;
            if (ServerSetup.ValidateServerFolder(_knownServer) == null)
            {
                _serverFolder.Value = _knownServer;
                _serverExisting.Select();
            }
            else _serverPackage.Select();
            LoadPackageInfo();
            ShowPage("server");
        }

        // List the content groups for the chosen source (release download or local package files).
        private void LoadPackageInfo()
        {
            _core = null;
            _packChoices.Clear();
            _packList.Controls.Clear();
            _packList.RowStyles.Clear();
            _packageInfo.ForeColor = Theme.Body;
            var local = _sourceLocal.Checked;
            _localZipLabel.Visible = _localZipRow.Visible = local;
            _deleteDownloads.Visible = !local;
            List<PackGroup> groups;
            try
            {
                if (local)
                {
                    var zip = _serverZip.Value;
                    if (!File.Exists(zip)) { _packageInfo.Text = zip.Length == 0 ? "Choose the core package." : "File not found."; UpdatePackageSpace(); return; }
                    var package = ServerSetup.ReadPackage(zip);
                    if (package.Id != "core")
                    {
                        _packageInfo.ForeColor = Theme.Red;
                        _packageInfo.Text = "\"" + package.Title + "\" is a content pack. Choose the core package (…-core.zip).";
                        UpdatePackageSpace();
                        return;
                    }
                    _core = package;
                    groups = PackGroup.FromLocal(ServerSetup.FindPacks(package));
                    _packageInfo.Text = "Server core " + package.Version + " (required, " + Bytes.Format(package.TotalBytes) + ")" + (groups.Count > 0 ? " plus:" : ". No content packs were found beside it.");
                }
                else
                {
                    var core = ServerRelease.Core;
                    groups = ServerRelease.Groups();
                    _packageInfo.Text = "Server core " + ServerRelease.Index.Version + " (required, " + Bytes.Format(core.Bytes) + " download) plus:";
                }
            }
            catch (Exception ex)
            {
                _packageInfo.ForeColor = Theme.Red;
                _packageInfo.Text = ex.Message;
                UpdatePackageSpace();
                return;
            }
            foreach (var group in groups)
            {
                var size = local ? Bytes.Format(group.InstalledBytes) : Bytes.Format(group.DownloadBytes) + " download";
                var parts = group.ExpectedParts > 1 ? " · " + group.ExpectedParts + " parts" : "";
                var box = new CheckBox {
                    Text = group.Title + " — " + size + parts + (group.Complete ? "" : " · INCOMPLETE: some parts are missing") + "\n" + group.Description,
                    Checked = group.Complete, Enabled = group.Complete, AutoSize = true, Font = Theme.F(14, Weight.Regular), ForeColor = Theme.Ink,
                    Margin = new Padding(0, 6, 0, 0), BackColor = Color.White, MaximumSize = new Size(760, 0)
                };
                box.CheckedChanged += (s, e) => UpdatePackageSpace();
                Theme.Add(_packList, box);
                _packChoices.Add(new KeyValuePair<CheckBox, PackGroup>(box, group));
            }
            UpdatePackageSpace();
            _serverPackage.PerformLayout();
        }

        private void UpdatePackageSpace()
        {
            var chosen = _packChoices.Where(p => p.Key.Checked).Select(p => p.Value).ToList();
            long installed, download = 0;
            if (_sourceLocal.Checked)
            {
                if (_core == null) { _packageSpace.Text = ""; return; }
                installed = _core.TotalBytes + chosen.Sum(g => g.InstalledBytes);
            }
            else
            {
                var core = ServerRelease.Core;
                installed = core.InstalledBytes + chosen.Sum(g => g.InstalledBytes);
                download = core.Bytes + chosen.Sum(g => g.DownloadBytes);
            }
            long free;
            var hasFree = InstallPaths.TryFreeSpace(_serverDestination.Value, out free);
            _packageSpace.Text = (download > 0 ? "Download " + Bytes.Format(download) + " · " : "") + "Installed " + Bytes.Format(installed) +
                (download > 0 ? " · Needs " + Bytes.Format(installed + download) + " free while installing" : "") + (hasFree ? " · " + Bytes.Format(free) + " free" : "");
        }

        private async Task ContinueFromServer()
        {
            _serverMessage.Text = "";
            _serverMessage.ForeColor = Theme.Red;
            var state = InstallState.Load() ?? new InstallState();
            if (_serverSkip.IsSelected)
            {
                var answer = MessageBox.Show(FindForm(),
                    "Without a server you will not be able to play Rec Room.\n\nSkip only if you have the address of someone else's running RecToon server. You can install a server later from Settings → Run setup again.",
                    "No server", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (answer != DialogResult.OK) return;
                _serverPath = null;
                state.Server = new ServerStateRecord { Choice = "skipped", Status = "notInstalled" };
            }
            else if (_serverExisting.IsSelected)
            {
                var problem = ServerSetup.ValidateServerFolder(_serverFolder.Value);
                if (problem != null) { _serverMessage.Text = problem; return; }
                _serverPath = InstallPaths.Normalize(_serverFolder.Value);
                state.Server = new ServerStateRecord { Choice = "existing", Path = _serverPath, Status = "installed" };
            }
            else
            {
                var local = _sourceLocal.Checked;
                if (local && _core == null) { _serverMessage.Text = "Choose the server's core package (…-core.zip)."; return; }
                var groups = _packChoices.Where(p => p.Key.Checked).Select(p => p.Value).ToList();
                var destination = _serverDestination.Value;
                var core = _core;
                var deleteAfter = !local && _deleteDownloads.Checked;
                _serverNext.Enabled = false;
                _serverBar.Visible = true;
                _serverBar.Value = 0;
                var lastReport = DateTime.MinValue;
                var progress = new Progress<InstallProgress>(p => {
                    if ((DateTime.UtcNow - lastReport).TotalMilliseconds < 150 && p.Done < p.Total) return;
                    lastReport = DateTime.UtcNow;
                    _serverBar.Value = p.Fraction;
                    var verb = p.Stage == "download" ? "Downloading " : p.Stage == "verify" ? "Checking " : "Installing and checking ";
                    _serverProgressText.Text = verb + Bytes.Format(p.Done) + " of " + Bytes.Format(p.Total) + "  ·  " + p.Detail;
                });
                string serverDir;
                try
                {
                    serverDir = await Task.Run(() => {
                        var packs = new List<ServerPackage>();
                        if (local)
                            packs.AddRange(groups.SelectMany(g => g.Local));
                        else
                        {
                            var assets = new List<ReleaseAsset> { ServerRelease.Core };
                            assets.AddRange(groups.SelectMany(g => g.Remote));
                            var files = ServerRelease.Download(assets, progress, CancellationToken.None);
                            core = ServerSetup.ReadPackage(files[0]);
                            packs.AddRange(files.Skip(1).Select(ServerSetup.ReadPackage));
                        }
                        var installed = ServerSetup.InstallPackages(core, packs, destination, progress, CancellationToken.None);
                        if (deleteAfter) ServerRelease.DeleteDownloads();
                        return installed;
                    });
                }
                catch (Exception ex)
                {
                    _serverMessage.Text = ex.Message + (local ? "" : " Downloads already finished are kept; Finish setup resumes.");
                    _serverProgressText.Text = "";
                    _log("Server install error: " + ex.Message);
                    return;
                }
                finally { _serverNext.Enabled = true; _serverBar.Visible = false; }
                _serverProgressText.Text = "";
                _log("Installed the RecToon server (core" + String.Concat(groups.Select(g => ", " + g.Id)) + ") in " + serverDir + " with a new HTTPS certificate.");
                _serverPath = serverDir;
                state.Server = new ServerStateRecord { Choice = local ? "package" : "download", Path = serverDir, Status = "installed" };
            }
            state.SetupComplete = true;
            state.Save();
            ShowDone(state);
        }

        // ---------- Done ----------

        private readonly Label _doneWarning = Theme.Label("", Theme.F(15, Weight.Medium), Theme.Red, new Padding(0, 14, 0, 0));

        private Control BuildDonePage()
        {
            var page = new SetupPage();
            page.Add(new IconTile(Glyph.Check, Theme.GreenTint, Theme.GreenIcon) { Size = new Size(60, 60), Margin = new Padding(0, 18, 0, 0), Anchor = AnchorStyles.Left });
            page.Add(Title("You're all set"));
            page.Add(_doneSummary);
            page.Add(_doneWarning);
            var open = Primary("Open the launcher", Glyph.Arrow);
            open.Click += (s, e) => _finished(_gamePath, _serverPath);
            page.SetButtons(open);
            return page;
        }

        private void ShowDone(InstallState state)
        {
            var lines = new List<string> {
                "Rec Room " + ClientBuild.Version + " (patched): " + state.Game.Path,
                "Patch: " + PatchPackage.Label,
                _serverPath != null ? "Server: " + _serverPath : "Server: none"
            };
            lines.Add("Steam must be running and signed in when you play.");
            _doneSummary.Text = String.Join("\n", lines);
            var warnings = new List<string>();
            if (_serverPath == null)
                warnings.Add("No server is installed. You will not be able to play until you join someone else's running server from the launcher, or install one with Settings → Run setup again.");
            else if (ServerSetup.NodeFor(_serverPath) == null)
                warnings.Add("Node.js was not found. The server needs it to run: install Node.js from nodejs.org, or use a server package that includes the runtime.");
            _doneWarning.Text = String.Join("\n", warnings);
            _doneWarning.Visible = warnings.Count > 0;
            ShowPage("done");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _downloadTimer.Stop(); _downloadTimer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
