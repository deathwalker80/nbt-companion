using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using NbtCompanion.Core;

namespace NbtCompanion.UI
{
    /// <summary>
    /// Window: header, state bar, page list on the left, page in the middle, complete log at the bottom.
    /// Every page has the same layout: title, description, content, action buttons at the bottom left in a fixed order.
    /// </summary>
    public sealed partial class MainForm : Form
    {
        private readonly Settings _settings;
        private HeadUnit? _hu;
        private DiscoveryResult? _disc;
        private string _password = "";
        private bool _sshOpen;
        private List<GhRelease> _releases = new List<GhRelease>();
        private InstalledAad? _installed;
        private bool _busy;

        private readonly List<Button> _actions = new List<Button>();
        private readonly Dictionary<string, Panel> _pages = new Dictionary<string, Panel>();
        private readonly List<NavItem> _navButtons = new List<NavItem>();
        private string _currentPage = "connect";

        private Panel _host = null!;
        private RichTextBox _log = null!;
        private Chip _stNet = null!;
        private Chip _stUnit = null!;
        private Chip _stSsh = null!;
        private Chip _stAad = null!;
        private Chip _stLatest = null!;
        private Chip _stBusy = null!;
        private BusyBar _busyBar = null!;

        public MainForm()
        {
            _settings = Settings.Load();
            L.English = _settings.Language == "en";
            Text = "NBT Companion " + AppInfo.Version + " by DeathWalkeR";
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            MinimumSize = new Size(1100, 760);
            Size = new Size(1280, 900);
            StartPosition = FormStartPosition.CenterScreen;
            Theme.DarkTitleBar(this);
            try
            {
                if (Environment.ProcessPath != null)
                {
                    Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
                }
            }
            catch (Exception)
            {
                // default icon
            }
            Log.Line += OnLogLine;
            BuildUi();
            Log.Write("NBT Companion " + AppInfo.Version + " avviato. Log completo: " + Log.FilePath);
            Shown += (s, e) => RunTask(L.T("Controllo online di NBT-AAD e di NBT Companion", "Online check of NBT-AAD and NBT Companion"), () =>
            {
                try
                {
                    LoadReleases();
                }
                catch (StepException ex)
                {
                    Log.Write("Elenco di NBT-AAD non disponibile: " + ex.Message.Split('\n')[0]);
                }
                CheckSelfUpdate(false);
            }, false, false);
            FormClosing += (s, e) =>
            {
                _settings.Save();
                if (_hu != null)
                {
                    try
                    {
                        _hu.CleanStage();
                    }
                    catch (Exception)
                    {
                        // unit gone
                    }
                    _hu.Dispose();
                }
                Log.ClearSecrets();
            };
        }

        // ------------------------------------------------------------------ layout

        private void BuildUi()
        {
            SuspendLayout();
            Controls.Clear();
            _actions.Clear();
            _pages.Clear();
            _navButtons.Clear();

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                BackColor = Theme.Back,
                Padding = new Padding(0),
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 262));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 3));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));

            // header: icon, name, version, author, subtitle, buttons on the right
            var header = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = new Padding(0) };
            var logo = new PictureBox
            {
                Size = new Size(46, 46),
                Location = new Point(14, 9),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = AppIconImage(),
            };
            header.Controls.Add(logo);
            var title = Theme.Label("NBT Companion", Theme.Heading);
            title.Location = new Point(68, 9);
            header.Controls.Add(title);
            var version = Theme.Label(AppInfo.Version + "  by DeathWalkeR", Theme.Small, Theme.Dim);
            version.Location = new Point(70, 37);
            header.Controls.Add(version);
            var sub = Theme.Label(L.T("BMW / MINI / Toyota Supra NBT EVO (ID5/ID6) - NBT-AAD via cavo ENET",
                "BMW / MINI / Toyota Supra NBT EVO (ID5/ID6) - NBT-AAD over the ENET cable"), Theme.Small, Theme.Dim);
            sub.Location = new Point(68 + Math.Max(title.PreferredWidth, version.PreferredWidth) + 34, 23);
            header.Controls.Add(sub);
            var lang = Theme.Button(L.English ? "Italiano" : "English");
            lang.MinimumSize = new Size(100, 34);
            lang.Click += (s, e) =>
            {
                if (_busy)
                {
                    return;
                }
                L.English = !L.English;
                _settings.Language = L.English ? "en" : "it";
                _settings.Save();
                string? logRtf = _log.Rtf;
                BuildUi();
                if (logRtf != null)
                {
                    _log.Rtf = logRtf;
                }
                _log.SelectionStart = _log.TextLength;
                _log.ScrollToCaret();
            };
            header.Controls.Add(lang);
            var upd = Theme.Button(L.T("Aggiornamenti", "Updates"));
            upd.MinimumSize = new Size(130, 34);
            upd.Click += (s, e) => RunTask(L.T("Controllo aggiornamenti di NBT Companion", "NBT Companion update check"), () => CheckSelfUpdate(true), false);
            header.Controls.Add(upd);
            _actions.Add(upd);
            header.Resize += (s, e) =>
            {
                lang.Location = new Point(header.Width - lang.Width - 18, 15);
                upd.Location = new Point(lang.Left - upd.Width - 10, 15);
            };
            root.Controls.Add(header, 0, 0);
            root.SetColumnSpan(header, 2);

            _busyBar = new BusyBar { Dock = DockStyle.Fill, Margin = new Padding(0) };
            root.Controls.Add(_busyBar, 0, 1);
            root.SetColumnSpan(_busyBar, 2);

            // state chips
            var state = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Back,
                Padding = new Padding(16, 10, 0, 0),
                WrapContents = false,
                Margin = new Padding(0),
            };
            _stNet = new Chip();
            _stUnit = new Chip();
            _stSsh = new Chip();
            _stAad = new Chip();
            _stLatest = new Chip();
            _stBusy = new Chip { Visible = false, ForeColor = Theme.Accent };
            state.Controls.AddRange(new Control[] { _stNet, _stUnit, _stSsh, _stAad, _stLatest, _stBusy });
            root.Controls.Add(state, 0, 2);
            root.SetColumnSpan(state, 2);

            // page list
            var nav = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Theme.Panel,
                Padding = new Padding(10, 12, 10, 10),
                Margin = new Padding(0, 0, 0, 0),
            };
            AddNav(nav, "connect", "1", L.T("Collegamento", "Connection"));
            AddNav(nav, "ssh", "2", L.T("SSH: apri / chiudi", "SSH: open / close"));
            AddNav(nav, "aad", "3", "Android Auto (NBT-AAD)");
            AddNav(nav, "menu", "4", L.T("Posizione nel menu", "Menu position"));
            AddNav(nav, "tile", "5", L.T("Casella nella home", "Home screen tile"));
            AddNav(nav, "look", "6", L.T("Grafica ex CarLife", "Former CarLife look"));
            AddNav(nav, "env", "7", "nbt-aad.env");
            AddNav(nav, "diag", "8", L.T("Diagnostica", "Diagnostics"));
            root.Controls.Add(nav, 0, 3);

            // page host
            _host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back, Padding = new Padding(20, 6, 20, 8), Margin = new Padding(0) };
            root.Controls.Add(_host, 1, 3);

            // log
            var logOuter = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back, Padding = new Padding(16, 6, 16, 14), Margin = new Padding(0) };
            var logCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 12) };
            _log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Theme.Back,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None,
                Font = Theme.Mono,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                DetectUrls = false,
            };
            Theme.DarkScrollBars(_log);
            var logBar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 46, ColumnCount = 2, BackColor = Theme.Panel };
            logBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            logBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var logTitle = Theme.Label(L.T("Log completo", "Complete log"), Theme.Bold, Theme.Dim);
            logTitle.Anchor = AnchorStyles.Left;
            logBar.Controls.Add(logTitle, 0, 0);
            var openLog = Theme.Button(L.T("Apri cartella dei log", "Open log folder"));
            openLog.Click += (s, e) => OpenFolder(AppPaths.Logs);
            var clear = Theme.Button(L.T("Pulisci finestra", "Clear window"));
            clear.Click += (s, e) => _log.Clear();
            var logButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Margin = new Padding(0, 2, 0, 0) };
            logButtons.Controls.Add(openLog);
            logButtons.Controls.Add(clear);
            logBar.Controls.Add(logButtons, 1, 0);
            var logFrame = new Frame(_log) { Dock = DockStyle.Fill };
            logCard.Controls.Add(logFrame);
            logCard.Controls.Add(logBar);
            logOuter.Controls.Add(logCard);
            root.Controls.Add(logOuter, 0, 4);
            root.SetColumnSpan(logOuter, 2);

            Controls.Add(root);

            _pages["connect"] = PageConnect();
            _pages["ssh"] = PageSsh();
            _pages["aad"] = PageAad();
            _pages["menu"] = PageMenu();
            _pages["tile"] = PageTile();
            _pages["look"] = PageLook();
            _pages["env"] = PageEnv();
            _pages["diag"] = PageDiag();
            ShowPage(_currentPage);
            RefreshState();
            ResumeLayout(true);
        }

        /// <summary>Logo for the header, from the PNG embedded at build time (Assets/NbtCompanion.png).</summary>
        private static Image? AppIconImage()
        {
            using System.IO.Stream? s = typeof(MainForm).Assembly.GetManifestResourceStream("NbtCompanion.png");
            if (s == null)
            {
                return null;
            }
            using var src = Image.FromStream(s);
            return new Bitmap(src);
        }

        private void AddNav(FlowLayoutPanel nav, string key, string number, string text)
        {
            var b = new NavItem
            {
                Text = text,
                Number = number,
                Tag = key,
                Width = 242,
                Margin = new Padding(0, 0, 0, 4),
            };
            b.Click += (s, e) => ShowPage(key);
            nav.Controls.Add(b);
            _navButtons.Add(b);
        }

        private void ShowPage(string key)
        {
            _currentPage = key;
            _host.Controls.Clear();
            _host.Controls.Add(_pages[key]);
            foreach (NavItem b in _navButtons)
            {
                b.Selected = (string?)b.Tag == key;
            }
        }

        /// <summary>Page frame: title, description, content in a card (fills), action bar.</summary>
        private Panel Page(string title, string description, Control content, params Button[] actions)
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Theme.Back,
            };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            var head = Theme.Label(title, Theme.Heading);
            head.Margin = new Padding(0, 0, 0, 2);
            t.Controls.Add(head, 0, 0);
            var desc = Theme.Label(description, Theme.Body, Theme.Dim);
            desc.MaximumSize = new Size(920, 0);
            desc.Margin = new Padding(0, 0, 0, 10);
            t.Controls.Add(desc, 0, 1);
            var card = new Card { Dock = DockStyle.Fill, Margin = new Padding(0) };
            Control inner = content is TextBox || content is ListView ? new Frame(content) : content;
            inner.Dock = DockStyle.Fill;
            card.Controls.Add(inner);
            t.Controls.Add(card, 0, 2);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 12, 0, 0), BackColor = Theme.Back };
            foreach (Button b in actions)
            {
                bar.Controls.Add(b);
                _actions.Add(b);
            }
            t.Controls.Add(bar, 0, 3);
            var p = new Panel { Dock = DockStyle.Fill };
            p.Controls.Add(t);
            return p;
        }

        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 8), Dock = DockStyle.Top };
            foreach (Control c in controls)
            {
                f.Controls.Add(c is TextBox ? new Frame(c) { Margin = new Padding(0, 0, 12, 0) } : c);
            }
            return f;
        }

        // ------------------------------------------------------------------ state and log

        private void OnLogLine(string block)
        {
            if (IsDisposed)
            {
                return;
            }
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(OnLogLine), block);
                return;
            }
            if (_log == null || _log.IsDisposed)
            {
                return;
            }
            bool bad = block.Contains("ERRORE") || block.Contains("ERROR") || block.Contains("NBTC-ERROR") || block.Contains("FALLIT") || block.Contains("FAILED");
            bool good = block.Contains("  OK") || block.Contains("NBTC-RESULT: OK") || block.Contains(" OK:") || block.Contains("ALL PASS");
            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = bad ? Theme.Bad : good ? Theme.Good : Theme.Text;
            _log.AppendText(block);
            _log.SelectionColor = Theme.Text;
            _log.ScrollToCaret();
        }

        private void RefreshState()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshState));
                return;
            }
            List<System.Net.IPAddress> ll = DiagNet.LinkLocalAddresses();
            _stNet.Text = L.T("Rete: ", "Network: ") + (ll.Count > 0 ? string.Join(", ", ll) : L.T("nessun 169.254.x.x", "no 169.254.x.x"));
            _stNet.ForeColor = ll.Count > 0 ? Theme.Good : Theme.Warn;
            _stUnit.Text = L.T("Testata: ", "Head unit: ") + (_disc != null ? (_disc.Vin.Length > 0 ? _disc.Vin : "?") + " @ " + _disc.Gateway : L.T("non trovata", "not found"));
            _stUnit.ForeColor = _disc != null ? Theme.Good : Theme.Dim;
            if (_hu != null && _hu.Connected)
            {
                _stSsh.Text = "SSH: " + L.T("collegato a ", "connected to ") + _hu.Host;
                _stSsh.ForeColor = Theme.Good;
            }
            else
            {
                _stSsh.Text = "SSH: " + (_sshOpen ? L.T("aperto, non collegato", "open, not connected") : L.T("chiuso o non verificato", "closed or not checked"));
                _stSsh.ForeColor = _sshOpen ? Theme.Warn : Theme.Dim;
            }
            _stAad.Text = "NBT-AAD: " + (_installed != null ? _installed.Summary() : L.T("non letto", "not read"));
            _stAad.ForeColor = _installed != null && _installed.DaemonPresent ? Theme.Good : Theme.Dim;
            GhRelease? latest = _releases.FirstOrDefault();
            GhRelease? latestRel = _releases.FirstOrDefault(r => !r.Prerelease);
            _stLatest.Text = L.T("Online: ", "Online: ") + (latest == null ? L.T("non controllato", "not checked")
                : latest.Name + (latest.Prerelease ? " (pre-release)" : "") + (latestRel != null && latestRel != latest ? "  |  release: " + latestRel.Name : ""));
            _stLatest.ForeColor = latest == null ? Theme.Dim : Theme.Text;
            UpdatePageState();
        }

        /// <summary>Runs work off the UI thread; buttons are disabled until it ends; errors are written in clear.</summary>
        private void RunTask(string title, Action work, bool needsUnit = true, bool popupOnError = true)
        {
            if (_busy)
            {
                return;
            }
            if (needsUnit && (_hu == null || !_hu.Connected))
            {
                Warn(L.T("Prima collega la testata (pagina 1).", "Connect the head unit first (page 1)."));
                return;
            }
            _busy = true;
            foreach (Button b in _actions)
            {
                b.Enabled = false;
            }
            _busyBar.Running = true;
            _stBusy.Text = L.T("In corso: ", "Running: ") + title;
            _stBusy.Visible = true;
            UseWaitCursor = true;
            Log.Section(title);
            Task.Run(() =>
            {
                string? error = null;
                try
                {
                    work();
                    Log.Write("ESITO: " + title + " - " + L.T("completato", "done"));
                }
                catch (StepException ex)
                {
                    error = ex.Message;
                    Log.Write("ERRORE: " + ex.Message);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Log.Write("ERRORE imprevisto: " + ex);
                }
                BeginInvoke(new Action(() =>
                {
                    _busy = false;
                    foreach (Button b in _actions)
                    {
                        b.Enabled = true;
                    }
                    _busyBar.Running = false;
                    _stBusy.Visible = false;
                    UseWaitCursor = false;
                    RefreshState();
                    if (error != null && popupOnError)
                    {
                        MessageBox.Show(this, error, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }));
            });
        }

        private void Warn(string text)
        {
            MessageBox.Show(this, text, "NBT Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private bool Confirm(string text)
        {
            return MessageBox.Show(this, text, "NBT Companion", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        /// <summary>Asks to type a word, for the steps that write to the car without SSH (unlock/revert).</summary>
        private bool ConfirmWord(string text, string word)
        {
            using var f = new Form
            {
                Text = "NBT Companion",
                BackColor = Theme.Back,
                ForeColor = Theme.Text,
                Font = Theme.Body,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(620, 300),
            };
            Theme.DarkTitleBar(f);
            var msg = Theme.Label(text + "\n\n" + L.T("Per confermare scrivi: ", "To confirm type: ") + word);
            msg.MaximumSize = new Size(580, 0);
            msg.Location = new Point(20, 18);
            var box = Theme.TextBox();
            box.Width = 220;
            var boxFrame = new Frame(box) { Location = new Point(20, 196) };
            var ok = Theme.Button(L.T("Conferma", "Confirm"), true);
            ok.Location = new Point(20, 245);
            ok.DialogResult = DialogResult.OK;
            var no = Theme.Button(L.T("Annulla", "Cancel"));
            no.Location = new Point(160, 245);
            no.DialogResult = DialogResult.Cancel;
            f.Controls.AddRange(new Control[] { msg, boxFrame, ok, no });
            f.AcceptButton = ok;
            f.CancelButton = no;
            return f.ShowDialog(this) == DialogResult.OK && box.Text.Trim() == word;
        }

        private static void OpenFolder(string path)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }

        private T Ui<T>(Func<T> f)
        {
            return InvokeRequired ? (T)Invoke(f) : f();
        }

        /// <summary>
        /// Looks for a newer NBT Companion; asks before installing it. Never while an operation on the head unit runs:
        /// RunTask allows one task at a time and the update is offered only at the end of this one.
        /// </summary>
        private void CheckSelfUpdate(bool userAsked)
        {
            SelfUpdate.Available? up;
            try
            {
                up = SelfUpdate.Check();
            }
            catch (StepException ex)
            {
                Log.Write("Controllo aggiornamenti di NBT Companion non riuscito: " + ex.Message.Split('\n')[0]);
                if (userAsked)
                {
                    throw new StepException(L.T("Controllo aggiornamenti non riuscito (repository " + SelfUpdate.Repo + " non raggiungibile o privato).",
                        "Update check failed (repository " + SelfUpdate.Repo + " not reachable or private)."));
                }
                return;
            }
            if (up == null)
            {
                Log.Write("NBT Companion " + AppInfo.Version + ": nessuna versione piu' nuova pubblicata");
                return;
            }
            Log.Write("NBT Companion " + up.Version + " disponibile (" + up.Release.Published.ToString("yyyy-MM-dd") + ")");
            bool yes = Ui(() => MessageBox.Show(this,
                L.T("E' disponibile NBT Companion ", "NBT Companion ") + up.Version + L.T(" (in uso: ", " is available (running: ") + AppInfo.Version + ").\n\n"
                + (up.Release.Body.Length > 0 ? up.Release.Body + "\n\n" : "")
                + L.T("Scarico, controllo lo SHA-256 e riavvio il programma aggiornato?", "Download, check the SHA-256 and restart the updated program?"),
                "NBT Companion", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes);
            if (!yes)
            {
                Log.Write("Aggiornamento rimandato dall'utente");
                return;
            }
            SelfUpdate.Apply(up);
            BeginInvoke(new Action(Close));
        }

        private void LoadReleases()
        {
            _releases = GitHub.Releases("Sq00ky/Supra-iDrive6-Android-Auto");
            GhRelease? latest = _releases.FirstOrDefault();
            GhRelease? latestRel = _releases.FirstOrDefault(r => !r.Prerelease);
            Log.Write("NBT-AAD online: " + _releases.Count + " pubblicazioni. Ultima uscita: " + (latest != null ? latest.Name + (latest.Prerelease ? " (pre-release)" : "") + ", " + latest.Published.ToString("yyyy-MM-dd HH:mm") + " UTC" : "-")
                + (latestRel != null && latestRel != latest ? "; ultima release: " + latestRel.Name + ", " + latestRel.Published.ToString("yyyy-MM-dd HH:mm") + " UTC" : ""));
            BeginInvoke(new Action(FillReleaseList));
        }

        private Dictionary<string, string> ShaToRelease()
        {
            var map = new Dictionary<string, string>();
            foreach (GhRelease r in _releases)
            {
                GhAsset? a = r.Asset("nbt-aad");
                if (a != null && a.Sha256.Length == 64 && !map.ContainsKey(a.Sha256))
                {
                    map[a.Sha256] = r.Name;
                }
            }
            return map;
        }
    }
}
