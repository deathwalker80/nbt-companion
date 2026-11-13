using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NbtCompanion.Core;

namespace NbtCompanion.UI
{
    public sealed partial class MainForm
    {
        // connection
        private TextBox _tHost = null!;
        private TextBox _tPassword = null!;
        private TextBox _connectInfo = null!;

        // ssh
        private Label _lSshPort = null!;
        private Label _lPython = null!;
        private Label _lScript = null!;
        private Button _bApprove = null!;
        private GhCommit? _newerCommit;

        // nbt-aad
        private ListView _relList = null!;
        private TextBox _relNotes = null!;
        private CheckBox _cPre = null!;
        private RadioButton _rKeep1 = null!;
        private RadioButton _rKeep2 = null!;
        private CheckBox _cColor = null!;
        private CheckBox _cMenu = null!;
        private Label _lColorFile = null!;
        private Label _lInstalled = null!;
        private string _colorIconsFile = "";

        // menu
        private Label _lMenuNow = null!;

        // tile
        private RadioButton _rFollow = null!;
        private RadioButton _rAlways = null!;
        private TextBox _tileOut = null!;
        private Kit? _tileKit;

        // look
        private ComboBox _cbBrand = null!;
        private TextBox _tLabel = null!;
        private TextBox _tImages = null!;
        private ListView _lookList = null!;
        private TextBox _lookOut = null!;
        private Kit? _lookKit;

        // env
        private TextBox _envBox = null!;

        // diagnostics
        private Label _lDiag = null!;

        /// <summary>Rows of a page: height 0 = as tall as the content, otherwise a share of the free height.</summary>
        private static TableLayoutPanel Grid(params (Control control, float share)[] rows)
        {
            var t = new TableLayoutPanel { ColumnCount = 1, RowCount = rows.Length, BackColor = Theme.Back };
            for (int i = 0; i < rows.Length; i++)
            {
                t.RowStyles.Add(rows[i].share <= 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Percent, rows[i].share));
                rows[i].control.Dock = DockStyle.Fill;
                t.Controls.Add(rows[i].control, 0, i);
            }
            return t;
        }

        private Button Act(string text, Action onClick, bool primary = false)
        {
            Button b = Theme.Button(text, primary);
            b.Click += (s, e) => onClick();
            return b;
        }

        private static TextBox Output()
        {
            TextBox t = Theme.TextBox(true, true);
            t.ReadOnly = true;
            t.BackColor = Theme.Panel;
            return t;
        }

        // ================================================================== 1. connection

        private Panel PageConnect()
        {
            _tHost = Theme.TextBox();
            _tHost.Width = 220;
            _tHost.Text = _settings.Host;
            _tPassword = Theme.TextBox();
            _tPassword.Width = 220;
            _tPassword.UseSystemPasswordChar = true;
            _tPassword.Text = _password;
            _connectInfo = Output();

            var form = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Theme.Back };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.Controls.Add(Theme.Label(L.T("Indirizzo SSH", "SSH address")), 0, 0);
            form.Controls.Add(Row(_tHost, Theme.Label(L.T("vuoto = automatico (annuncio HSFZ, poi 169.254.199.99 e 169.254.199.119)",
                "empty = automatic (HSFZ announcement, then 169.254.199.99 and 169.254.199.119)"), Theme.Small, Theme.Dim)), 1, 0);
            form.Controls.Add(Theme.Label(L.T("Password root", "root password")), 0, 1);
            form.Controls.Add(Row(_tPassword, Theme.Label(L.T("chiesta a te, mai salvata: resta in memoria finche' l'app e' aperta",
                "asked to you, never saved: it stays in memory while the app is open"), Theme.Small, Theme.Dim)), 1, 1);

            Control content = Grid((form, 0), (Theme.Label(L.T("Esito", "Result"), Theme.Bold, Theme.Dim), 0), (_connectInfo, 100));
            return Page(L.T("Collegamento", "Connection"),
                L.T("Cavo ENET dall'OBD al PC, scheda di rete con indirizzo 169.254.x.x, quadro acceso. L'app trova la testata (telaio), controlla la porta SSH 22, entra come root e fa l'auto-test del trasferimento verso hu-omap.",
                    "ENET cable from the OBD port to the PC, network adapter with a 169.254.x.x address, ignition on. The app finds the head unit (VIN), checks SSH port 22, logs in as root and runs the transfer self-test to hu-omap."),
                content,
                Act(L.T("Cerca la testata", "Find the head unit"), DoFind),
                Act(L.T("Collega", "Connect"), DoConnect, true),
                Act(L.T("Scollega", "Disconnect"), DoDisconnect));
        }

        private void DoFind()
        {
            RunTask(L.T("Ricerca della testata", "Head unit search"), () =>
            {
                FindUnit(Ui(() => _tHost.Text));
            }, false);
        }

        private string? FindUnit(string configured)
        {
            List<System.Net.IPAddress> ll = DiagNet.LinkLocalAddresses();
            if (ll.Count == 0)
            {
                throw new StepException(L.T("Nessuna scheda di rete con indirizzo 169.254.x.x: collega il cavo ENET e aspetta che Windows assegni l'indirizzo.",
                    "No network adapter with a 169.254.x.x address: plug the ENET cable and wait for Windows to assign the address."));
            }
            Log.Write("Schede 169.254: " + string.Join(", ", ll));
            _disc = DiagNet.Discover(3000);
            Log.Write(_disc != null
                ? "Annuncio HSFZ da " + _disc.Gateway + ", telaio " + (_disc.Vin.Length > 0 ? _disc.Vin : "(non letto)")
                : "Nessun annuncio HSFZ sulla porta 6811 (testata spenta, cavo o scheda di rete?)");
            string? host = DiagNet.FindSsh(configured, _disc?.Gateway, 1500);
            _sshOpen = host != null;
            Log.Write(host != null ? "SSH (porta 22) aperto su " + host : "SSH (porta 22) chiuso su tutti gli indirizzi provati");
            string text = L.T("Telaio: ", "VIN: ") + (_disc?.Vin ?? "-") + "\r\n"
                + L.T("Gateway HSFZ: ", "HSFZ gateway: ") + (_disc?.Gateway ?? "-") + "\r\n"
                + "SSH: " + (host ?? L.T("chiuso (pagina 2 per aprirlo)", "closed (page 2 to open it)")) + "\r\n";
            Ui(() => _connectInfo.Text = text);
            RefreshState();
            return host;
        }

        private void DoConnect()
        {
            string configured = _tHost.Text.Trim();
            string pw = _tPassword.Text;
            _settings.Host = configured;
            _settings.Save();
            RunTask(L.T("Collegamento", "Connection"), () =>
            {
                string? host = FindUnit(configured);
                if (host == null)
                {
                    throw new StepException(L.T("SSH chiuso: aprilo dalla pagina 2, poi ricollegati.", "SSH closed: open it from page 2, then connect again."));
                }
                if (_hu != null)
                {
                    _hu.Dispose();
                    _hu = null;
                }
                _hu = HeadUnit.Connect(host, pw);
                _password = pw;
                _hu.TransferSelfTest();
                List<string> brands = TileKits.DetectBrands(_hu);
                if (brands.Count == 1)
                {
                    _settings.Brand = brands[0];
                    _settings.Save();
                }
                if (_releases.Count == 0)
                {
                    try
                    {
                        LoadReleases();
                    }
                    catch (StepException ex)
                    {
                        Log.Write("Elenco online non disponibile: " + ex.Message);
                    }
                }
                _installed = AadInstaller.ReadInstalled(_hu, _releases, ShaToRelease());
                string info = Ui(() => _connectInfo.Text)
                    + L.T("Collegato come root a ", "Logged in as root on ") + host + "\r\n"
                    + L.T("Auto-test del trasferimento: superato (", "Transfer self-test: passed (") + _hu.TransferDescription + ")\r\n"
                    + L.T("Marca nell'HMI: ", "Brand in the HMI: ") + (brands.Count > 0 ? string.Join(", ", brands) : "-") + "\r\n"
                    + "NBT-AAD: " + _installed.Summary() + "\r\n"
                    + L.T("Versione precedente conservata: ", "Previous version kept: ") + (_installed.OldDaemon ? (_installed.OldBuild.Length > 0 ? _installed.OldBuild : L.T("si", "yes")) : "no") + "\r\n"
                    + L.T("Voci nel menu: ", "Menu entries: ") + string.Join(", ", _installed.Menu.Select(m => m.Text + " -> " + AadPackage.MenuStateName(m.StateId))) + "\r\n";
                Ui(() =>
                {
                    _connectInfo.Text = info;
                    if (brands.Count == 1)
                    {
                        _cbBrand.SelectedItem = brands[0];
                    }
                    return 0;
                });
            }, false);
        }

        private void DoDisconnect()
        {
            if (_hu == null)
            {
                return;
            }
            HeadUnit hu = _hu;
            RunTask(L.T("Scollegamento", "Disconnection"), () =>
            {
                hu.CleanStage();
                hu.Dispose();
                _hu = null;
                Log.Write("Scollegato. File temporanei in /dev/shmem/nbtc rimossi.");
            });
        }

        // ================================================================== 2. ssh

        private Panel PageSsh()
        {
            Unlocker.Commit(_settings, out string commit, out _);
            _lSshPort = Theme.Label("");
            _lPython = Theme.Label("");
            _lScript = Theme.Label(L.T("Script: ", "Script: ") + Unlocker.Repo + " @ " + commit.Substring(0, 7)
                + (commit == Unlocker.PinnedCommit ? " (" + Unlocker.PinnedDate + L.T(", verificato per questa versione dell'app)", ", checked for this app version)") : L.T(" (approvato da te)", " (approved by you)")));
            var notes = Theme.Label(L.T(
                "Come funziona: lo script di Xayanix (scaricato da GitHub su questo PC, mai incluso nell'app perche' il suo repository non ha licenza) entra nella testata via HSFZ 6801, " +
                "fa l'accesso di sicurezza livello 3 e riscrive /fs/sda0/opt/sys/etc/starter.cfg con OPT_DEBUG da STOP a RUN. Dopo il riavvio automatico (circa 50 s) sono aperti SSH 22, telnet 23 e qconn 8000 come root.\n" +
                "Chiusura: riscrive lo starter.cfg di fabbrica (OPT_DEBUG STOP). Lasciare l'SSH aperto puo' far comparire il codice errore B7F8E5 (issue #190 di Sq00ky).\n" +
                "Solo famiglia NBT-Evo 007.xxx, NON le 001_05x. Serve Python 3 su questo PC.",
                "How it works: Xayanix's script (downloaded from GitHub onto this PC, never bundled in the app because its repository has no licence) enters the head unit over HSFZ 6801, " +
                "does security access level 3 and rewrites /fs/sda0/opt/sys/etc/starter.cfg with OPT_DEBUG from STOP to RUN. After the automatic reboot (about 50 s) SSH 22, telnet 23 and qconn 8000 are open as root.\n" +
                "Closing: writes the factory starter.cfg back (OPT_DEBUG STOP). SSH left open can raise fault code B7F8E5 (Sq00ky's issue #190).\n" +
                "NBT-Evo 007.xxx family only, NOT 001_05x. Python 3 is needed on this PC."), Theme.Body, Theme.Dim);
            notes.MaximumSize = new Size(880, 0);
            _bApprove = Act(L.T("Usa il commit nuovo", "Use the new commit"), DoApproveCommit);
            Control content = Grid((_lSshPort, 0), (_lPython, 0), (_lScript, 0), (notes, 0), (new Panel(), 100));
            return Page(L.T("SSH: apertura e chiusura", "SSH: open and close"),
                L.T("Se l'SSH e' chiuso l'app lo apre; a fine lavoro puo' richiuderlo.", "If SSH is closed the app opens it; when the work is done it can close it again."),
                content,
                Act(L.T("Controlla", "Check"), DoSshCheck),
                Act(L.T("Installa Python (winget)", "Install Python (winget)"), () =>
                {
                    if (Confirm(L.T("Apro una finestra con winget install Python.Python.3.12: le condizioni le leggi e accetti tu in quella finestra. Procedo?",
                        "A window opens with winget install Python.Python.3.12: you read and accept its terms in that window. Go on?")))
                    {
                        Unlocker.InstallPythonWithWinget();
                    }
                }),
                Act(L.T("Apri SSH", "Open SSH"), DoUnlock, true),
                Act(L.T("Chiudi SSH", "Close SSH"), DoRevert),
                _bApprove);
        }

        private void DoSshCheck()
        {
            RunTask(L.T("Controllo SSH e strumenti", "SSH and tools check"), () =>
            {
                Unlocker.Python? py = Unlocker.FindPython();
                Log.Write(py != null ? "Python: " + py.Version + " (" + py.Exe + ")" : "Python 3 non trovato");
                string? host = null;
                if (DiagNet.LinkLocalAddresses().Count > 0)
                {
                    _disc = DiagNet.Discover(3000);
                    host = DiagNet.FindSsh(_settings.Host, _disc?.Gateway, 1500);
                    _sshOpen = host != null;
                }
                string script = Unlocker.EnsureScript(_settings);
                bool st = py != null && Unlocker.SelfTest(py, script);
                _newerCommit = null;
                try
                {
                    _newerCommit = Unlocker.NewerCommit(_settings);
                    Log.Write(_newerCommit == null
                        ? "Script di Xayanix: nessun commit piu' nuovo di quello in uso"
                        : "Script di Xayanix: c'e' un commit piu' nuovo, NON usato finche' non lo approvi: " + _newerCommit.Sha.Substring(0, 7) + " del "
                            + _newerCommit.Date.ToString("yyyy-MM-dd") + " - " + _newerCommit.Message.Split('\n')[0]);
                }
                catch (StepException ex)
                {
                    Log.Write("Controllo dei commit di Xayanix non riuscito: " + ex.Message);
                }
                Ui(() =>
                {
                    _lSshPort.Text = "SSH: " + (host != null ? L.T("aperto su ", "open on ") + host : L.T("chiuso (o testata non raggiungibile)", "closed (or head unit not reachable)"));
                    _lSshPort.ForeColor = host != null ? Theme.Good : Theme.Warn;
                    _lPython.Text = "Python: " + (py != null ? py.Version : L.T("non trovato: usa \"Installa Python (winget)\"", "not found: use \"Install Python (winget)\""))
                        + (py != null ? L.T(" - auto-test dello script: ", " - script self-test: ") + (st ? "ALL PASS" : L.T("NON superato", "NOT passed")) : "");
                    _lPython.ForeColor = py != null && st ? Theme.Good : Theme.Bad;
                    if (_newerCommit != null)
                    {
                        _lScript.Text += L.T("  |  nuovo commit disponibile: ", "  |  new commit available: ") + _newerCommit.Sha.Substring(0, 7);
                    }
                    return 0;
                });
            }, false);
        }

        private void DoApproveCommit()
        {
            if (_newerCommit == null)
            {
                Warn(L.T("Nessun commit nuovo: premi prima \"Controlla\".", "No new commit: press \"Check\" first."));
                return;
            }
            GhCommit c = _newerCommit;
            if (!Confirm(L.T("Usare d'ora in poi il commit ", "Use from now on commit ") + c.Sha.Substring(0, 7) + " (" + c.Date.ToString("yyyy-MM-dd") + "):\n" + c.Message
                + L.T("\n\nNon e' stato verificato con questa versione dell'app. Leggi prima le modifiche su GitHub. Procedo?",
                    "\n\nIt was not checked with this app version. Read the changes on GitHub first. Go on?")))
            {
                return;
            }
            RunTask(L.T("Approvazione del nuovo script", "Approval of the new script"), () =>
            {
                Unlocker.Approve(_settings, c);
                _newerCommit = null;
            }, false);
        }

        private void DoUnlock()
        {
            if (!ConfirmWord(L.T("Apertura SSH: lo script di Xayanix scrive starter.cfg (OPT_DEBUG RUN) direttamente sulla testata in funzione, senza firma. " +
                    "Un file sbagliato o di un'altra versione puo' impedire l'avvio della testata. Solo NBT-Evo 007.xxx. Batteria carica o mantenitore collegato.",
                    "Opening SSH: Xayanix's script writes starter.cfg (OPT_DEBUG RUN) directly to the running head unit, unsigned. " +
                    "A wrong or mismatched file can stop the head unit from booting. NBT-Evo 007.xxx only. Charged battery or charger connected."),
                    L.T("SBLOCCA", "UNLOCK")))
            {
                return;
            }
            RunTask(L.T("Apertura SSH", "SSH opening"), () => Flash(false), false);
        }

        private void DoRevert()
        {
            if (!ConfirmWord(L.T("Chiusura SSH: lo script di Xayanix riscrive lo starter.cfg di fabbrica (OPT_DEBUG STOP). Dopo il riavvio SSH, telnet e qconn sono chiusi.",
                    "Closing SSH: Xayanix's script writes the factory starter.cfg back (OPT_DEBUG STOP). After the reboot SSH, telnet and qconn are closed."),
                    L.T("RIPRISTINA", "REVERT")))
            {
                return;
            }
            RunTask(L.T("Chiusura SSH", "SSH closing"), () => Flash(true), false);
        }

        private void Flash(bool revert)
        {
            Unlocker.Python py = Unlocker.FindPython()
                ?? throw new StepException(L.T("Python 3 non trovato: usa \"Installa Python (winget)\".", "Python 3 not found: use \"Install Python (winget)\"."));
            string script = Unlocker.EnsureScript(_settings);
            if (!Unlocker.SelfTest(py, script))
            {
                throw new StepException(L.T("Auto-test dello script di Xayanix non superato: non scrivo nulla.", "Xayanix's script self-test failed: nothing written."));
            }
            if (DiagNet.LinkLocalAddresses().Count == 0)
            {
                throw new StepException(L.T("Nessuna scheda di rete con indirizzo 169.254.x.x.", "No network adapter with a 169.254.x.x address."));
            }
            _disc = DiagNet.Discover(3000);
            string? gateway = _disc?.Gateway;
            if (_disc != null)
            {
                Log.Write("Testata: telaio " + _disc.Vin + " via " + _disc.Gateway);
            }
            else
            {
                // The answer to the 6811 broadcast can be stopped by the Windows firewall. The script does its own
                // discovery and falls back to 169.254.199.99; without a head unit it stops before writing (ABORTED).
                Log.Write("Annuncio HSFZ (6811) senza risposta (firewall di Windows?): lo script di Xayanix cerca la testata da solo, poi prova 169.254.199.99");
            }
            if (_hu != null)
            {
                try
                {
                    _hu.CleanStage();
                }
                catch (Exception)
                {
                    // the unit is about to reboot anyway
                }
                _hu.Dispose();
                _hu = null;
            }
            if (!Unlocker.Flash(py, script, gateway, revert))
            {
                throw new StepException(L.T("Lo script non ha confermato la scrittura (vedi log): starter.cfg non cambiato.",
                    "The script did not confirm the write (see the log): starter.cfg not changed."));
            }
            if (revert)
            {
                Log.Write("starter.cfg di fabbrica scritto. Attendo il riavvio (90 s) e controllo che la porta 22 sia chiusa.");
                System.Threading.Thread.Sleep(90000);
                string? still = DiagNet.FindSsh(_settings.Host, gateway, 2000);
                _sshOpen = still != null;
                if (still != null)
                {
                    throw new StepException(L.T("La porta 22 risponde ancora su ", "Port 22 still answers on ") + still
                        + L.T(": riavvia la testata a mano (tasto power 30 s) e ricontrolla.", ": reboot the head unit by hand (power button 30 s) and check again."));
                }
                Log.Write("SSH chiuso: la porta 22 non risponde piu'.");
            }
            else
            {
                Log.Write("OPT_DEBUG RUN scritto. Attendo che la testata riparta con SSH (fino a 180 s).");
                string? host = DiagNet.WaitForSsh(_settings.Host, gateway, 180, System.Threading.CancellationToken.None);
                _sshOpen = host != null;
                if (host == null)
                {
                    throw new StepException(L.T("La porta 22 non risponde dopo 180 s: riavvia la testata a mano (tasto power 30 s), poi \"Controlla\".",
                        "Port 22 does not answer after 180 s: reboot the head unit by hand (power button 30 s), then \"Check\"."));
                }
                Log.Write("SSH aperto su " + host + ". Ora pagina 1: Collega.");
            }
        }

        // ================================================================== 3. nbt-aad

        private Panel PageAad()
        {
            _relList = Theme.List();
            _relList.Columns.Add(L.T("Nome", "Name"), 330);
            _relList.Columns.Add("Tag", 130);
            _relList.Columns.Add(L.T("Tipo", "Type"), 100);
            _relList.Columns.Add(L.T("Data (UTC)", "Date (UTC)"), 140);
            _relList.Columns.Add("nbt-aad", 90);
            _relList.SelectedIndexChanged += (s, e) =>
            {
                GhRelease? r = SelectedRelease();
                _relNotes.Text = r == null ? "" : (r.Body.Length > 0 ? r.Body.Replace("\r\n", "\n").Replace("\n", "\r\n") : L.T("(nessuna nota)", "(no notes)"));
            };
            _relNotes = Output();
            _cPre = Theme.Check(L.T("Mostra anche le pre-release", "Also show pre-releases"), _settings.ShowPrereleases);
            _cPre.CheckedChanged += (s, e) =>
            {
                _settings.ShowPrereleases = _cPre.Checked;
                _settings.Save();
                FillReleaseList();
            };
            _rKeep1 = Theme.Radio(L.T("Tieni 1 versione", "Keep 1 version"), _settings.KeepVersions == 1);
            _rKeep2 = Theme.Radio(L.T("Tieni 2 versioni (in uso + precedente)", "Keep 2 versions (current + previous)"), _settings.KeepVersions != 1);
            _cColor = Theme.Check(L.T("Icone a colori (tenute a ogni aggiornamento)", "Colour icons (kept at every update)"), _settings.ColorIcons);
            _cMenu = Theme.Check(L.T("Voci in MINI Connected / ConnectedDrive", "Entries in MINI Connected / ConnectedDrive"), _settings.MenuInConnected);
            var pick = Theme.Button(L.T("Scegli icone a colori...", "Choose colour icons..."));
            pick.Click += (s, e) =>
            {
                using var d = new OpenFileDialog { Filter = "NbtAad_ALL_Images.zip|*.zip", Title = L.T("Icone a colori (NbtAad_ALL_Images.zip)", "Colour icons (NbtAad_ALL_Images.zip)") };
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    _colorIconsFile = d.FileName;
                    UpdatePageState();
                }
            };
            _lColorFile = Theme.Label("", Theme.Small, Theme.Dim);
            _lInstalled = Theme.Label("", Theme.Bold);
            var options = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top };
            options.Controls.AddRange(new Control[] { _rKeep1, _rKeep2, _cColor, _cMenu });
            Control content = Grid(
                (Row(_cPre), 0),
                (_relList, 55),
                (Theme.Label(L.T("Note di rilascio", "Release notes"), Theme.Bold, Theme.Dim), 0),
                (_relNotes, 45),
                (options, 0),
                (Row(pick, _lColorFile), 0),
                (_lInstalled, 0));
            FillReleaseList();
            return Page("Android Auto (NBT-AAD)",
                L.T("Release e pre-release di Sq00ky/Supra-iDrive6-Android-Auto. Prima di scrivere: SHA-256 di GitHub, ELF ARM/QNX, 5 file del plugin, spazio libero, prova di avvio (--license). " +
                    "nbt-aad.env non viene toccato. Riavvio a mano (tasto power 30 s).",
                    "Releases and pre-releases of Sq00ky/Supra-iDrive6-Android-Auto. Before writing: GitHub SHA-256, ARM/QNX ELF, the 5 plugin files, free space, start test (--license). " +
                    "nbt-aad.env is not touched. Reboot by hand (power button 30 s)."),
                content,
                Act(L.T("Aggiorna elenco", "Refresh list"), () => RunTask(L.T("Controllo online di NBT-AAD", "Online check of NBT-AAD"), LoadReleases, false)),
                Act(L.T("Leggi installata", "Read installed"), () => RunTask(L.T("Lettura di NBT-AAD installato", "Reading installed NBT-AAD"),
                    () => _installed = AadInstaller.ReadInstalled(_hu!, _releases, ShaToRelease()))),
                Act(L.T("Installa selezionata", "Install selected"), DoInstall, true),
                Act(L.T("Ripristina precedente", "Restore previous"), DoRestorePrevious));
        }

        private GhRelease? SelectedRelease()
        {
            return _relList.SelectedItems.Count == 1 ? _relList.SelectedItems[0].Tag as GhRelease : null;
        }

        private void FillReleaseList()
        {
            if (_relList == null || _relList.IsDisposed)
            {
                return;
            }
            string? keep = SelectedRelease()?.Tag;
            _relList.BeginUpdate();
            _relList.Items.Clear();
            foreach (GhRelease r in _releases.Where(x => _settings.ShowPrereleases || !x.Prerelease))
            {
                GhAsset? a = r.Asset("nbt-aad");
                var it = new ListViewItem(new[]
                {
                    r.Name,
                    r.Tag,
                    r.Prerelease ? "pre-release" : "release",
                    r.Published.ToString("yyyy-MM-dd HH:mm"),
                    a != null ? (a.Size / 1024.0 / 1024.0).ToString("0.00") + " MB" : "-",
                })
                {
                    Tag = r,
                    ForeColor = r.Prerelease ? Theme.Warn : Theme.Text,
                };
                _relList.Items.Add(it);
                if (r.Tag == keep)
                {
                    it.Selected = true;
                }
            }
            _relList.EndUpdate();
            RefreshState();
        }

        private void DoInstall()
        {
            GhRelease? rel = SelectedRelease();
            if (rel == null)
            {
                Warn(L.T("Seleziona una versione nell'elenco.", "Select a version in the list."));
                return;
            }
            GhAsset? daemon = rel.Asset("nbt-aad");
            GhAsset? zip = rel.Asset("Releases.zip") ?? rel.Asset("release.zip");
            if (daemon == null || zip == null)
            {
                Warn(L.T("Questa pubblicazione non ha nbt-aad e Releases.zip.", "This release has no nbt-aad and Releases.zip."));
                return;
            }
            var opt = new InstallOptions
            {
                KeepVersions = _rKeep1.Checked ? 1 : 2,
                ColorIcons = _cColor.Checked,
                MenuInConnected = _cMenu.Checked,
                ColorIconsFile = _colorIconsFile,
            };
            _settings.KeepVersions = opt.KeepVersions;
            _settings.ColorIcons = opt.ColorIcons;
            _settings.MenuInConnected = opt.MenuInConnected;
            _settings.Save();
            string text = L.T("Installo ", "Install ") + rel.Name + " (" + rel.Tag + (rel.Prerelease ? ", pre-release" : "") + ")\n\n"
                + L.T("Versioni sulla testata dopo l'installazione: ", "Versions on the head unit after the install: ") + opt.KeepVersions + "\n"
                + L.T("Icone: ", "Icons: ") + (opt.ColorIcons ? L.T("a colori", "colour") : L.T("quelle della release", "the release ones")) + "\n"
                + L.T("Voci: ", "Entries: ") + (opt.MenuInConnected ? "MINI Connected / ConnectedDrive" : "Media/Radio") + "\n\n"
                + L.T("nbt-aad viene fermato. Procedo?", "nbt-aad gets stopped. Go on?");
            if (!Confirm(text))
            {
                return;
            }
            RunTask(L.T("Installazione di ", "Install of ") + rel.Name, () =>
            {
                byte[] d = GitHub.DownloadAsset(rel, daemon, false);
                byte[] z = GitHub.DownloadAsset(rel, zip, false);
                AadPackage pkg = AadPackage.FromRelease(rel.Tag, d, z);
                AadInstaller.Install(_hu!, pkg, opt);
                _installed = AadInstaller.ReadInstalled(_hu!, _releases, ShaToRelease());
            });
        }

        private void DoRestorePrevious()
        {
            if (!Confirm(L.T("Scambio la versione in uso con la precedente (nbt-aad.old e NbtAad.old). Rifacendolo si torna indietro. Procedo?",
                "Swap the current version with the previous one (nbt-aad.old and NbtAad.old). Doing it again swaps back. Go on?")))
            {
                return;
            }
            RunTask(L.T("Ripristino della versione precedente", "Restore of the previous version"), () =>
            {
                AadInstaller.RestorePrevious(_hu!);
                _installed = AadInstaller.ReadInstalled(_hu!, _releases, ShaToRelease());
            });
        }

        // ================================================================== 4. menu

        private Panel PageMenu()
        {
            _lMenuNow = Theme.Label("", Theme.Bold);
            var notes = Theme.Label(L.T(
                "In NbtAad.xml le voci \"Android Auto\" e \"Open Android Auto\" hanno insertStateID: 20 = Media/Radio, 50 = MINI Connected / ConnectedDrive. " +
                "Verificato su MINI F60; su BMW il 50 come ConnectedDrive non e' verificato. Ogni nuovo plugin riporta 20: con l'opzione della pagina 3 l'app lo rifa' da sola a ogni aggiornamento.",
                "In NbtAad.xml the entries \"Android Auto\" and \"Open Android Auto\" have insertStateID: 20 = Media/Radio, 50 = MINI Connected / ConnectedDrive. " +
                "Checked on a MINI F60; on BMW, 50 as ConnectedDrive is not checked. Every new plugin brings back 20: with the page 3 option the app redoes it at every update."), Theme.Body, Theme.Dim);
            notes.MaximumSize = new Size(880, 0);
            Control content = Grid((_lMenuNow, 0), (notes, 0), (new Panel(), 100));
            return Page(L.T("Posizione delle voci nel menu", "Position of the menu entries"),
                L.T("Sposta le voci di NBT-AAD tra Media/Radio e MINI Connected / ConnectedDrive. Riavvio a mano (tasto power 30 s).",
                    "Moves the NBT-AAD entries between Media/Radio and MINI Connected / ConnectedDrive. Reboot by hand (power button 30 s)."),
                content,
                Act(L.T("Leggi", "Read"), () => RunTask(L.T("Lettura del menu", "Menu read"), () => _installed = AadInstaller.ReadInstalled(_hu!, _releases, ShaToRelease()))),
                Act(L.T("Sposta in MINI Connected / ConnectedDrive", "Move to MINI Connected / ConnectedDrive"), () =>
                {
                    if (Confirm(L.T("Sposto le voci in MINI Connected / ConnectedDrive (50)?", "Move the entries to MINI Connected / ConnectedDrive (50)?")))
                    {
                        RunTask(L.T("Voci in MINI Connected / ConnectedDrive", "Entries to MINI Connected / ConnectedDrive"), () =>
                        {
                            AadInstaller.ApplyMenu(_hu!, "50");
                            _installed = AadInstaller.ReadInstalled(_hu!, _releases, ShaToRelease());
                        });
                    }
                }, true),
                Act(L.T("Riporta in Media/Radio", "Back to Media/Radio"), () =>
                {
                    if (Confirm(L.T("Riporto le voci in Media/Radio (20)?", "Move the entries back to Media/Radio (20)?")))
                    {
                        RunTask(L.T("Voci in Media/Radio", "Entries to Media/Radio"), () =>
                        {
                            AadInstaller.ApplyMenu(_hu!, "20");
                            _installed = AadInstaller.ReadInstalled(_hu!, _releases, ShaToRelease());
                        });
                    }
                }));
        }

        // ================================================================== 5. tile

        private Panel PageTile()
        {
            _rFollow = Theme.Radio(L.T("follow: casella solo con Android Auto collegato", "follow: tile only while Android Auto is connected"), _settings.TileMode != "always");
            _rAlways = Theme.Radio(L.T("always: casella sempre visibile", "always: tile always shown"), _settings.TileMode == "always");
            _tileOut = Output();
            var notes = Theme.Label(L.T(
                "Kit dall'ultima release di deathwalker80/nbt-aa-tile (non pre-release), verificato con SHA-256 di GitHub e SHA256SUMS, copiato nella RAM di hu-omap. " +
                "Serve nbt-aad 1.8.66 o successivo. Prima la prova in RAM, poi l'avvio automatico: 2 righe in start_webserver.sh (originale conservato), parte solo dopo nbt-aad, " +
                "si disattiva da solo dopo un crash o 3 avvii non superati. Vale per MINI e BMW (la casella non cambia grafica: i testi e le immagini sono nella pagina 6).",
                "Kit from the latest release of deathwalker80/nbt-aa-tile (no pre-release), checked with GitHub's SHA-256 and SHA256SUMS, copied into hu-omap's RAM. " +
                "Needs nbt-aad 1.8.66 or later. First the RAM test, then the boot start: 2 lines in start_webserver.sh (original kept), starts only after nbt-aad, " +
                "disables itself after a crash or 3 failed starts. Works for MINI and BMW (the tile does not change graphics: texts and images are on page 6)."), Theme.Body, Theme.Dim);
            notes.MaximumSize = new Size(880, 0);
            Control content = Grid((Row(_rFollow, _rAlways), 0), (notes, 0), (_tileOut, 100));
            return Page(L.T("Casella Android Auto nella home (nbt-aa-tile)", "Android Auto tile on the home screen (nbt-aa-tile)"),
                L.T("Per la prova in RAM: Android Auto avviato e telefono collegato, poi guarda il menu principale per 120 s.",
                    "For the RAM test: Android Auto started and phone connected, then watch the main menu for 120 s."),
                content,
                Act(L.T("Stato", "Status"), () => TileRun(L.T("Stato della casella", "Tile status"), k => TileKits.TileStatus(_hu!, k))),
                Act(L.T("Prova in RAM (120 s)", "RAM test (120 s)"), () => TileRun(L.T("Prova in RAM della casella", "Tile RAM test"), k => TileKits.TileTestRam(_hu!, k, TileMode()))),
                Act(L.T("Installa all'avvio", "Install at boot"), () =>
                {
                    if (Confirm(L.T("Installo la casella all'avvio in modo ", "Install the tile at boot in mode ") + TileMode()
                        + L.T(" (modifica start_webserver.sh, originale conservato)? Fallo solo dopo una prova in RAM riuscita.",
                            " (changes start_webserver.sh, original kept)? Do it only after a good RAM test.")))
                    {
                        TileRun(L.T("Installazione della casella all'avvio", "Tile install at boot"), k => TileKits.TileInstallBoot(_hu!, k, TileMode()));
                    }
                }, true),
                Act(L.T("Rimuovi", "Remove"), () =>
                {
                    if (Confirm(L.T("Tolgo la casella dall'avvio e rimetto start_webserver.sh originale?", "Remove the tile from boot and put the original start_webserver.sh back?")))
                    {
                        TileRun(L.T("Rimozione della casella", "Tile removal"), k => TileKits.TileRemove(_hu!, k));
                    }
                }));
        }

        private string TileMode()
        {
            string m = _rAlways.Checked ? "always" : "follow";
            _settings.TileMode = m;
            _settings.Save();
            return m;
        }

        private void TileRun(string title, Func<Kit, string> work)
        {
            RunTask(title, () =>
            {
                _tileKit ??= TileKits.FetchTile();
                string o = work(_tileKit);
                Ui(() => _tileOut.Text = o.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            });
        }

        // ================================================================== 6. former CarLife look

        private Panel PageLook()
        {
            _cbBrand = Theme.Combo();
            _cbBrand.Items.AddRange(TileKits.Brands.Cast<object>().ToArray());
            _cbBrand.SelectedItem = TileKits.Brands.Contains(_settings.Brand) ? _settings.Brand : "mini";
            _cbBrand.SelectedIndexChanged += (s, e) =>
            {
                _settings.Brand = (string)_cbBrand.SelectedItem!;
                _settings.Save();
                FillLookList();
            };
            _tLabel = Theme.TextBox();
            _tLabel.Width = 220;
            _tLabel.Text = _settings.MenuLookLabel;
            _tImages = Theme.TextBox();
            _tImages.Width = 460;
            _tImages.ReadOnly = true;
            _tImages.Text = _settings.MenuLookImages;
            var browse = Theme.Button(L.T("Cartella immagini...", "Images folder..."));
            browse.Click += (s, e) =>
            {
                using var d = new FolderBrowserDialog { Description = L.T("Cartella con le immagini (stessa struttura di images/ del kit: mini/..., bmw/..., common/...)",
                    "Folder with the images (same layout as the kit's images/: mini/..., bmw/..., common/...)") };
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    _tImages.Text = d.SelectedPath;
                    _settings.MenuLookImages = d.SelectedPath;
                    _settings.Save();
                    FillLookList();
                }
            };
            var noImages = Theme.Button(L.T("Nessuna immagine", "No images"));
            noImages.Click += (s, e) =>
            {
                _tImages.Text = "";
                _settings.MenuLookImages = "";
                _settings.Save();
                FillLookList();
            };
            _lookList = Theme.List();
            _lookList.Columns.Add(L.T("Immagine (percorso sotto /opt/hmi/ID5/data/ro)", "Image (path under /opt/hmi/ID5/data/ro)"), 560);
            _lookList.Columns.Add(L.T("Misura", "Size"), 110);
            _lookList.Columns.Add(L.T("Tipo colore", "Colour type"), 110);
            _lookOut = Output();
            var form = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Theme.Back };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.Controls.Add(Theme.Label(L.T("Marca (testi dell'HMI)", "Brand (HMI texts)")), 0, 0);
            form.Controls.Add(Row(_cbBrand, Theme.Label(L.T("mini, bmw, bmwm (menu con testi M), bmwi", "mini, bmw, bmwm (menu with M texts), bmwi"), Theme.Small, Theme.Dim)), 1, 0);
            form.Controls.Add(Theme.Label(L.T("Testo della voce", "Entry text")), 0, 1);
            form.Controls.Add(Row(_tLabel, Theme.Label(L.T("al posto di \"n. t. a. (ASIA)\" / \"CarLife\", in tutte le lingue", "instead of \"n. t. a. (ASIA)\" / \"CarLife\", in every language"), Theme.Small, Theme.Dim)), 1, 1);
            form.Controls.Add(Theme.Label(L.T("Immagini", "Images")), 0, 2);
            form.Controls.Add(Row(_tImages, browse, noImages), 1, 2);
            Control content = Grid((form, 0), (_lookList, 50), (_lookOut, 50));
            FillLookList();
            return Page(L.T("Grafica della voce ex CarLife (aa-menu-look)", "Look of the former CarLife entry (aa-menu-look)"),
                L.T("Testi \"Android Auto\" e, se le indichi, le tue immagini PNG al posto di quelle CarLife (stessa larghezza e altezza dell'originale). " +
                    "Ogni file originale viene salvato una sola volta in /opt/aa/menu-look-backup; \"Ripristina originali\" li rimette tutti. Riavvio a mano.",
                    "\"Android Auto\" texts and, if you give them, your PNG images instead of the CarLife ones (same width and height as the original). " +
                    "Every original file is saved once in /opt/aa/menu-look-backup; \"Restore originals\" puts all of them back. Reboot by hand."),
                content,
                Act(L.T("Elenca immagini della testata", "List head unit images"), () => LookRun(L.T("Immagini CarLife della testata", "Head unit CarLife images"),
                    (k, b) => TileKits.LookList(_hu!, k, b))),
                Act(L.T("Applica testi e immagini", "Apply texts and images"), () =>
                {
                    string label = _tLabel.Text;
                    try
                    {
                        TileKits.CheckLabel(label);
                    }
                    catch (StepException ex)
                    {
                        Warn(ex.Message);
                        return;
                    }
                    _settings.MenuLookLabel = label.Trim();
                    _settings.Save();
                    string folder = _tImages.Text;
                    if (Confirm(L.T("Cambio i testi in \"", "Change the texts to \"") + label.Trim() + "\""
                        + (_lookList.Items.Count > 0 ? L.T(" e ", " and ") + _lookList.Items.Count + L.T(" immagini", " images") : "")
                        + L.T(" per la marca ", " for brand ") + _cbBrand.SelectedItem + L.T("? Gli originali vengono salvati sulla testata.", "? The originals are saved on the head unit.")))
                    {
                        LookRun(L.T("Testi e immagini ex CarLife", "Former CarLife texts and images"), (k, b) => TileKits.LookInstall(_hu!, k, b, label, folder));
                    }
                }, true),
                Act(L.T("Ripristina originali", "Restore originals"), () =>
                {
                    if (Confirm(L.T("Rimetto tutti i file originali salvati da aa-menu-look?", "Put back every original file saved by aa-menu-look?")))
                    {
                        LookRun(L.T("Ripristino degli originali", "Restore of the originals"), (k, b) => TileKits.LookRestore(_hu!, k));
                    }
                }));
        }

        private void FillLookList()
        {
            if (_lookList == null || _lookList.IsDisposed)
            {
                return;
            }
            _lookList.Items.Clear();
            string brand = (string?)_cbBrand.SelectedItem ?? "mini";
            try
            {
                foreach (KeyValuePair<string, byte[]> kv in TileKits.CollectImages(_tImages.Text, brand).OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    PngInfo? p = TileKits.ReadPng(kv.Value);
                    _lookList.Items.Add(new ListViewItem(new[]
                    {
                        kv.Key.Substring("images/".Length),
                        p != null ? p.Width + " x " + p.Height : "-",
                        p != null ? p.ColorType + (p.ColorType == 6 ? " RGBA" : p.ColorType == 4 ? L.T(" grigio+alfa", " grey+alpha") : p.ColorType == 2 ? " RGB" : "") : "-",
                    }));
                }
            }
            catch (StepException ex)
            {
                Log.Write("ERRORE immagini: " + ex.Message);
            }
        }

        private void LookRun(string title, Func<Kit, string, string> work)
        {
            string brand = (string?)_cbBrand.SelectedItem ?? "mini";
            RunTask(title, () =>
            {
                _lookKit ??= TileKits.FetchLook();
                string o = work(_lookKit, brand);
                Ui(() => _lookOut.Text = o.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            });
        }

        // ================================================================== 7. nbt-aad.env

        private Panel PageEnv()
        {
            _envBox = Theme.TextBox(true, true);
            return Page("nbt-aad.env",
                L.T("Impostazioni di NBT-AAD (/opt/aa/nbt-aad.env), una per riga, NOME=valore (es. NBT_BT_CONN, NBT_TOUCH_OX, NBT_LOG_FILE). " +
                    "Prima di ogni scrittura la versione sulla testata viene salvata sul PC (cartella backup). Vale dal prossimo avvio di Android Auto.",
                    "NBT-AAD settings (/opt/aa/nbt-aad.env), one per line, NAME=value (e.g. NBT_BT_CONN, NBT_TOUCH_OX, NBT_LOG_FILE). " +
                    "Before every write the head unit's version is saved on the PC (backup folder). Takes effect at the next Android Auto start."),
                _envBox,
                Act(L.T("Leggi dalla testata", "Read from the head unit"), () => RunTask(L.T("Lettura di nbt-aad.env", "Reading nbt-aad.env"), () =>
                {
                    string? env = AadInstaller.ReadEnv(_hu!);
                    Log.Write(env == null ? "/opt/aa/nbt-aad.env non presente" : "/opt/aa/nbt-aad.env letto (" + env.Length + " caratteri)");
                    Ui(() => _envBox.Text = (env ?? "").Replace("\n", "\r\n"));
                })),
                Act(L.T("Salva sulla testata", "Save to the head unit"), () =>
                {
                    string text = _envBox.Text;
                    if (Confirm(L.T("Scrivo questo testo in /opt/aa/nbt-aad.env?", "Write this text to /opt/aa/nbt-aad.env?")))
                    {
                        RunTask(L.T("Scrittura di nbt-aad.env", "Writing nbt-aad.env"), () => AadInstaller.WriteEnv(_hu!, text));
                    }
                }, true),
                Act(L.T("Apri cartella backup", "Open backup folder"), () => OpenFolder(AppPaths.PcBackup)));
        }

        // ================================================================== 8. diagnostics

        private Panel PageDiag()
        {
            _lDiag = Theme.Label("", Theme.Body, Theme.Dim);
            var notes = Theme.Label(L.T(
                "Solo lettura. Comandi: uname, ifconfig, pidin, sloginfo, df, pfctl -s rules, elenchi di /opt/aa, plugin, /mnt. " +
                "File: nbt-aad.log, nbt-aad-app.log, stato di nbt-aad, aa-tile.log, nbt-aad.env, NbtAad.xml/manifest, start_webserver.sh, stato della casella, pf.conf, " +
                "e il log completo di questa app. Mai letture di dispositivi grezzi (/dev/fs0 ha riavviato la testata il 12/11).",
                "Read only. Commands: uname, ifconfig, pidin, sloginfo, df, pfctl -s rules, listings of /opt/aa, the plugin, /mnt. " +
                "Files: nbt-aad.log, nbt-aad-app.log, nbt-aad state, aa-tile.log, nbt-aad.env, NbtAad.xml/manifest, start_webserver.sh, tile state, pf.conf, " +
                "and the complete log of this app. Never raw device reads (/dev/fs0 rebooted the head unit on 12/11)."), Theme.Body, Theme.Dim);
            notes.MaximumSize = new Size(880, 0);
            Control content = Grid((notes, 0), (_lDiag, 0), (new Panel(), 100));
            return Page(L.T("Diagnostica", "Diagnostics"),
                L.T("ZIP con log e file utili, salvato in Documenti\\NBT-Companion.", "ZIP with logs and useful files, saved in Documents\\NBT-Companion."),
                content,
                Act(L.T("Crea ZIP diagnostica", "Create diagnostics ZIP"), () => RunTask(L.T("Diagnostica", "Diagnostics"), () =>
                {
                    string path = Diagnostics.Collect(_hu!, _disc?.Vin ?? "");
                    Ui(() => _lDiag.Text = path);
                }), true),
                Act(L.T("Apri cartella", "Open folder"), () => OpenFolder(AppPaths.Diagnostics)));
        }

        // ================================================================== state shown on the pages

        private void UpdatePageState()
        {
            if (_lInstalled != null && !_lInstalled.IsDisposed)
            {
                _lInstalled.Text = L.T("Sulla testata: ", "On the head unit: ") + (_installed != null ? _installed.Summary()
                    + (_installed.OldDaemon ? L.T("  |  precedente: ", "  |  previous: ") + (_installed.OldBuild.Length > 0 ? _installed.OldBuild : L.T("presente", "present")) : L.T("  |  nessuna precedente", "  |  no previous"))
                    : L.T("non letto (collega la testata)", "not read (connect the head unit)"));
            }
            if (_lColorFile != null && !_lColorFile.IsDisposed)
            {
                _lColorFile.Text = _colorIconsFile.Length > 0
                    ? L.T("scelto: ", "chosen: ") + _colorIconsFile
                    : File.Exists(AppPaths.ColorIconsFile)
                        ? L.T("copia a colori sul PC: ", "colour copy on the PC: ") + AppPaths.ColorIconsFile
                        : L.T("usate quelle del plugin attuale sulla testata", "the ones of the current plugin on the head unit are used");
            }
            if (_lMenuNow != null && !_lMenuNow.IsDisposed)
            {
                _lMenuNow.Text = L.T("Adesso: ", "Now: ") + (_installed != null && _installed.Menu.Count > 0
                    ? string.Join(",  ", _installed.Menu.Select(m => m.Text + " -> " + AadPackage.MenuStateName(m.StateId)))
                    : L.T("non letto", "not read"));
            }
            if (_lSshPort != null && !_lSshPort.IsDisposed && _lSshPort.Text.Length == 0)
            {
                _lSshPort.Text = "SSH: " + L.T("premi \"Controlla\"", "press \"Check\"");
                _lPython.Text = "Python: " + L.T("premi \"Controlla\"", "press \"Check\"");
            }
        }
    }
}
