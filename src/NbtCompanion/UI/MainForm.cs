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
        private readonly List<Button> _navButtons = new List<Button>();
        private string _currentPage = "connect";

        private Panel _host = null!;
        private RichTextBox _log = null!;
        private Label _stNet = null!;
        private Label _stUnit = null!;
        private Label _stSsh = null!;
        private Label _stAad = null!;
        private Label _stLatest = null!;

        public MainForm()
        {
            _settings = Settings.Load();
            L.English = _settings.Language == "en";
            Text = "NBT Companion " + AppInfo.Version;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            MinimumSize = new Size(1100, 760);
            Size = new Size(1240, 860);
            StartPosition = FormStartPosition.CenterScreen;
            Theme.DarkTitleBar(this);
            Log.Line += OnLogLine;
            BuildUi();
            Log.Write("NBT Companion " + AppInfo.Version + " avviato. Log completo: " + Log.FilePath);
            Shown += (s, e) => RunTask(L.T("Controllo online di NBT-AAD", "Online check of NBT-AAD"), LoadReleases, false, false);
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
                RowCount = 4,
                BackColor = Theme.Back,
                Padding = new Padding(0),
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));

            // header
            var header = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel };
            var title = Theme.Label("NBT Companion  " + AppInfo.Version, Theme.Heading);
            title.Location = new Point(18, 14);
            header.Controls.Add(title);
            var sub = Theme.Label(L.T("BMW / MINI / Toyota Supra NBT EVO (ID5/ID6) - NBT-AAD via cavo ENET",
                "BMW / MINI / Toyota Supra NBT EVO (ID5/ID6) - NBT-AAD over the ENET cable"), Theme.Small, Theme.Dim);
            sub.Location = new Point(330, 22);
            header.Controls.Add(sub);
            var lang = Theme.Button(L.English ? "Italiano" : "English");
            lang.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lang.MinimumSize = new Size(100, 30);
            lang.Click += (s, e) =>
            {
                if (_busy)
                {
                    return;
                }
                L.English = !L.English;
                _settings.Language = L.English ? "en" : "it";
                _settings.Save();
                BuildUi();
            };
            header.Controls.Add(lang);
            header.Resize += (s, e) => lang.Location = new Point(header.Width - lang.Width - 18, 12);
            root.Controls.Add(header, 0, 0);
            root.SetColumnSpan(header, 2);

            // state bar
            var state = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Back,
                Padding = new Padding(14, 8, 0, 0),
                WrapContents = false,
            };
            _stNet = Theme.Label("", Theme.Small, Theme.Dim);
            _stUnit = Theme.Label("", Theme.Small, Theme.Dim);
            _stSsh = Theme.Label("", Theme.Small, Theme.Dim);
            _stAad = Theme.Label("", Theme.Small, Theme.Dim);
            _stLatest = Theme.Label("", Theme.Small, Theme.Dim);
            foreach (Label l in new[] { _stNet, _stUnit, _stSsh, _stAad, _stLatest })
            {
                l.Margin = new Padding(0, 0, 26, 0);
                state.Controls.Add(l);
            }
            root.Controls.Add(state, 0, 1);
            root.SetColumnSpan(state, 2);

            // navigation
            var nav = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Theme.Panel,
                Padding = new Padding(10, 12, 10, 10),
            };
            AddNav(nav, "connect", L.T("1  Collegamento", "1  Connection"));
            AddNav(nav, "ssh", L.T("2  SSH: apri / chiudi", "2  SSH: open / close"));
            AddNav(nav, "aad", L.T("3  Android Auto (NBT-AAD)", "3  Android Auto (NBT-AAD)"));
            AddNav(nav, "menu", L.T("4  Posizione nel menu", "4  Menu position"));
            AddNav(nav, "tile", L.T("5  Casella nella home", "5  Home screen tile"));
            AddNav(nav, "look", L.T("6  Grafica ex CarLife", "6  Former CarLife look"));
            AddNav(nav, "env", "7  nbt-aad.env");
            AddNav(nav, "diag", L.T("8  Diagnostica", "8  Diagnostics"));
            root.Controls.Add(nav, 0, 2);

            // page host
            _host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back, Padding = new Padding(18, 10, 18, 10) };
            root.Controls.Add(_host, 1, 2);

            // log
            var logBox = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(12, 8, 12, 10) };
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
            var logBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Panel, WrapContents = false };
            logBar.Controls.Add(Theme.Label(L.T("Log completo", "Complete log"), Theme.Bold, Theme.Dim));
            var openLog = Theme.Button(L.T("Apri cartella dei log", "Open log folder"));
            openLog.Click += (s, e) => OpenFolder(AppPaths.Logs);
            logBar.Controls.Add(openLog);
            var clear = Theme.Button(L.T("Pulisci finestra", "Clear window"));
            clear.Click += (s, e) => _log.Clear();
            logBar.Controls.Add(clear);
            logBox.Controls.Add(_log);
            logBox.Controls.Add(logBar);
            root.Controls.Add(logBox, 0, 3);
            root.SetColumnSpan(logBox, 2);

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

        private void AddNav(FlowLayoutPanel nav, string key, string text)
        {
            var b = new Button
            {
                Text = text,
                Tag = key,
                Width = 226,
                Height = 42,
                TextAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Theme.Text,
                BackColor = Theme.Panel,
                Font = Theme.Bold,
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.Field;
            b.Click += (s, e) => ShowPage(key);
            nav.Controls.Add(b);
            _navButtons.Add(b);
        }

        private void ShowPage(string key)
        {
            _currentPage = key;
            _host.Controls.Clear();
            _host.Controls.Add(_pages[key]);
            foreach (Button b in _navButtons)
            {
                bool on = (string?)b.Tag == key;
                b.BackColor = on ? Theme.Accent : Theme.Panel;
                b.FlatAppearance.MouseOverBackColor = on ? Theme.Accent : Theme.Field;
            }
        }

        /// <summary>Page frame: title, description, content (fills), action bar.</summary>
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
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            var head = Theme.Label(title, Theme.Heading);
            head.Margin = new Padding(0, 0, 0, 4);
            t.Controls.Add(head, 0, 0);
            var desc = Theme.Label(description, Theme.Body, Theme.Dim);
            desc.MaximumSize = new Size(900, 0);
            desc.Margin = new Padding(0, 0, 0, 10);
            t.Controls.Add(desc, 0, 1);
            content.Dock = DockStyle.Fill;
            t.Controls.Add(content, 0, 2);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
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
            f.Controls.AddRange(controls);
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
            box.Location = new Point(20, 200);
            var ok = Theme.Button(L.T("Conferma", "Confirm"), true);
            ok.Location = new Point(20, 245);
            ok.DialogResult = DialogResult.OK;
            var no = Theme.Button(L.T("Annulla", "Cancel"));
            no.Location = new Point(160, 245);
            no.DialogResult = DialogResult.Cancel;
            f.Controls.AddRange(new Control[] { msg, box, ok, no });
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
