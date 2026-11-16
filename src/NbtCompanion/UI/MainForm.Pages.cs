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
            var t = new TableLayoutPanel { ColumnCount = 1, RowCount = rows.Length, BackColor = Theme.Panel };
            // one column as wide as the card: a wide row never pushes fields (and their scroll bars) out of sight
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < rows.Length; i++)
            {
                t.RowStyles.Add(rows[i].share <= 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Percent, rows[i].share));
                Control c = rows[i].control is TextBox || rows[i].control is ListView ? new Frame(rows[i].control) : rows[i].control;
                c.Dock = DockStyle.Fill;
                if (c is Frame)
                {
                    c.Margin = new Padding(0, 2, 0, 8);
                }
                t.Controls.Add(c, 0, i);
            }
            return t;
        }

        private Button Act(string text, Action onClick, bool primary = false)
        {
            Button b = Theme.Button(text, primary);
            b.Click += (s, e) => onClick();
            return b;
        }

        /// <summary>Label of a form row, vertically centred on its field, bold.</summary>
        private static Label FormLabel(string text)
        {
            Label l = Theme.Label(text, Theme.Bold, Theme.Dim);
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(0, 0, 18, 8);
            return l;
        }

        private static TextBox Output()
        {
            TextBox t = Theme.TextBox(true, true);
            t.ReadOnly = true;
            t.BackColor = Theme.Back;
            return t;
        }

        // ================================================================== 1. connection

        private Panel PageConnect()
        {
            _tHost = Theme.TextBox();
            _tHost.Width = 220;
            _tHost.Text = _settings.Host.Length > 0 ? _settings.Host : DiagNet.DefaultHosts[0];
            _tPassword = Theme.TextBox();
            _tPassword.Width = 220;
            _tPassword.UseSystemPasswordChar = true;
            _tPassword.Text = _password.Length > 0 ? _password : HeadUnit.DefaultPassword;
            _connectInfo = Output();

            var form = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Theme.Panel };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.Controls.Add(FormLabel(L.T("Indirizzo SSH", "SSH address")), 0, 0);
            form.Controls.Add(Row(_tHost, Theme.Label(L.T("predefinito, modificabile; se non risponde: annuncio HSFZ e 169.254.199.119",
                "default, editable; if it does not answer: HSFZ announcement and 169.254.199.119"), Theme.Small, Theme.Dim)), 1, 0);
            form.Controls.Add(FormLabel(L.T("Password root", "root password")), 0, 1);
            form.Controls.Add(Row(_tPassword, Theme.Label(L.T("predefinita ID5/ID6, modificabile; mai salvata ne' scritta nei log",
                "ID5/ID6 default, editable; never saved nor written to the logs"), Theme.Small, Theme.Dim)), 1, 1);

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
            // a manual connection also switches the automatic one back on
            _autoOff = false;
            ConnectTo(false);
        }

        /// <summary>Connection with the address and password of page 1; auto = started by the automatic connection.</summary>
        private void ConnectTo(bool auto)
        {
            string configured = _tHost.Text.Trim();
            string pw = _tPassword.Text;
            if (!auto)
            {
                _settings.Host = configured;
                _settings.Save();
            }
            RunTask(auto ? L.T("Collegamento automatico", "Automatic connection") : L.T("Collegamento", "Connection"), () =>
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
                try
                {
                    _hu = HeadUnit.Connect(host, pw);
                }
                catch (StepException ex) when (ex.AuthRejected)
                {
                    // never retried by itself with a refused password: the user corrects it and presses Connect
                    _autoOff = true;
                    throw new StepException(ex.Message + L.T(" Collegamento automatico fermato: correggi la password e premi \"Collega\".",
                        " Automatic connection stopped: correct the password and press \"Connect\"."));
                }
                _password = pw;
                _unit = null;
                _hu.TransferSelfTest();
                List<string> brands = TileKits.DetectBrands(_hu);
                _unit = UnitInfo.Read(_hu, brands);
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
                    + L.T("Unita': ", "Unit: ") + _unit.Family + "\r\n"
                    + L.T("Versione software: ", "Software version: ") + (_unit.SwVersion.Length > 0 ? _unit.SwVersion : L.T("non letta (", "not read (") + string.Join(L.T(" e ", " and "), UnitInfo.VersionFiles) + L.T(" assenti o vuoti)", " missing or empty)")) + "\r\n"
                    + (_unit.SwVersion.Length > 0 && !_unit.IsNbtEvo
                        ? L.T("ATTENZIONE: non risulta una NBT EVO; questa app e' fatta solo per le NBT EVO ID5/ID6.\r\n", "WARNING: this does not look like an NBT EVO; this app is made for NBT EVO ID5/ID6 only.\r\n")
                        : "")
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
            }, false, !auto);
        }

        private void DoDisconnect()
        {
            if (_hu == null)
            {
                return;
            }
            HeadUnit hu = _hu;
            // after a manual disconnection the app does not connect again by itself (until "Connect" or a new cable link)
            _autoOff = true;
            RunTask(L.T("Scollegamento", "Disconnection"), () =>
            {
                hu.CleanStage();
                hu.Dispose();
                _hu = null;
                _unit = null;
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
            InstallRelease(rel);
        }

        /// <summary>Install of one NBT-AAD release with the options of page 3 (also used by the update page).</summary>
        private void InstallRelease(GhRelease rel)
        {
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
            return Page(UnderDevelopment, Theme.Warn, L.T("Casella Android Auto nella home (nbt-aa-tile)", "Android Auto tile on the home screen (nbt-aa-tile)"),
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
            _tImages.Width = 360;
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
            var form = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Theme.Panel };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.Controls.Add(FormLabel(L.T("Marca (testi dell'HMI)", "Brand (HMI texts)")), 0, 0);
            form.Controls.Add(Row(_cbBrand, Theme.Label(L.T("mini, bmw, bmwm (menu con testi M), bmwi", "mini, bmw, bmwm (menu with M texts), bmwi"), Theme.Small, Theme.Dim)), 1, 0);
            form.Controls.Add(FormLabel(L.T("Testo della voce", "Entry text")), 0, 1);
            form.Controls.Add(Row(_tLabel, Theme.Label(L.T("al posto di \"n. t. a. (ASIA)\" / \"CarLife\", in tutte le lingue", "instead of \"n. t. a. (ASIA)\" / \"CarLife\", in every language"), Theme.Small, Theme.Dim)), 1, 1);
            form.Controls.Add(FormLabel(L.T("Immagini", "Images")), 0, 2);
            form.Controls.Add(Row(_tImages, browse, noImages), 1, 2);
            Control content = Grid((form, 0), (_lookList, 50), (_lookOut, 50));
            FillLookList();
            return Page(UnderDevelopment, Theme.Warn, L.T("Grafica della voce ex CarLife (aa-menu-look)", "Look of the former CarLife entry (aa-menu-look)"),
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

        // ================================================================== 9. NBT Plus apps

        private ComboBox _cbPlus = null!;
        private Label _lPlusApk = null!;
        private Label _lPlusUsb = null!;
        private TextBox _plusOut = null!;
        private List<GhRelease> _plusReleases = new List<GhRelease>();
        private Kit? _plusKit;

        private Panel PagePlus()
        {
            _cbPlus = Theme.Combo();
            _cbPlus.Width = 300;
            _cbPlus.Items.AddRange(NbtPlus.Apps.Cast<object>().ToArray());
            _cbPlus.SelectedItem = NbtPlus.Apps.FirstOrDefault(a => a.Key == _settings.PlusApp) ?? NbtPlus.Apps[0];
            _cbPlus.SelectedIndexChanged += (s, e) =>
            {
                _settings.PlusApp = PlusApp().Key;
                _settings.Save();
                UpdatePlusLabels();
            };
            _lPlusApk = Theme.Label("", Theme.Body, Theme.Text);
            _lPlusUsb = Theme.Label("", Theme.Body, Theme.Text);
            _plusOut = Output();
            var form = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Theme.Panel };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.Controls.Add(FormLabel(L.T("App sul telefono", "App on the phone")), 0, 0);
            form.Controls.Add(Row(_cbPlus, Theme.Label(L.T("una sola sul telefono: due insieme si contendono il collegamento con l'iDrive",
                "only one on the phone: two together fight over the link with the iDrive"), Theme.Small, Theme.Dim)), 1, 0);
            form.Controls.Add(FormLabel("APK"), 0, 1);
            form.Controls.Add(_lPlusApk, 1, 1);
            form.Controls.Add(FormLabel(L.T("Plugin iDrive", "iDrive plugins")), 0, 2);
            form.Controls.Add(_lPlusUsb, 1, 2);
            var notes = Theme.Label(L.T(
                "Da deathwalker80/idrive-plus. L'APK (generico NBT Plus o specifico MINI / BMW / Supra Plus) viene scaricato e controllato (SHA-256 di GitHub) " +
                "in Documenti\\NBT-Companion\\apk, da copiare sul telefono. I plugin dell'iDrive (Meteo, Notizie, Notizie locali, Carburanti, Traffico, Stato veicolo) " +
                "vengono installati con lo 00-installa.sh della chiavetta, eseguito dalla RAM di hu-omap: le pagine \"in attesa del telefono\" seguono l'app scelta. " +
                "Serve NBT-AAD installato. \"Rimuovi\" sposta i plugin in /opt/aa/plugins_off (recuperabili). Riavvio a mano.",
                "From deathwalker80/idrive-plus. The APK (generic NBT Plus or specific MINI / BMW / Supra Plus) is downloaded and checked (GitHub SHA-256) " +
                "into Documents\\NBT-Companion\\apk, to copy to the phone. The iDrive plugins (Weather, News, Local news, Fuel, Traffic, Vehicle status) " +
                "are installed with the stick's 00-installa.sh, run from hu-omap's RAM: the \"waiting for the phone\" pages follow the chosen app. " +
                "NBT-AAD must be installed. \"Remove\" moves the plugins to /opt/aa/plugins_off (recoverable). Reboot by hand."), Theme.Body, Theme.Dim);
            notes.MaximumSize = new Size(880, 0);
            Control content = Grid((form, 0), (notes, 0), (_plusOut, 100));
            UpdatePlusLabels();
            return Page(Tested, Theme.Good, L.T("App NBT Plus (iDrive Plus)", "NBT Plus apps (iDrive Plus)"),
                L.T("App del telefono e plugin dell'iDrive di NBT Plus / MINI Plus / BMW Plus / Supra Plus.",
                    "Phone app and iDrive plugins of NBT Plus / MINI Plus / BMW Plus / Supra Plus."),
                content,
                Act(L.T("Aggiorna elenco", "Refresh list"), () => RunTask(L.T("Controllo online di iDrive Plus / NBT Plus", "Online check of iDrive Plus / NBT Plus"), LoadPlusReleases, false)),
                Act(L.T("Scarica APK", "Download APK"), DoPlusApk),
                Act(L.T("Installa plugin iDrive", "Install iDrive plugins"), DoPlusInstall, true),
                Act(L.T("Rimuovi plugin", "Remove plugins"), DoPlusRemove),
                Act(L.T("Stato", "Status"), () => RunTask(L.T("Stato dei plugin", "Plugin status"), () =>
                {
                    string o = NbtPlus.Status(_hu!);
                    Ui(() => _plusOut.Text = o.Replace("\r\n", "\n").Replace("\n", "\r\n"));
                })));
        }

        private NbtPlus.AppChoice PlusApp()
        {
            return (_cbPlus.SelectedItem as NbtPlus.AppChoice) ?? NbtPlus.Apps[0];
        }

        private void LoadPlusReleases()
        {
            _plusReleases = GitHub.Releases(NbtPlus.Repo);
            _plusKit = null;
            NbtPlus.Found? usb = NbtPlus.Usb(_plusReleases);
            Log.Write("iDrive Plus / NBT Plus online: " + _plusReleases.Count + " pubblicazioni; chiavetta " + (usb != null ? usb.Asset.Name + " (" + usb.Release.Name + ")" : "-"));
            BeginInvoke(new Action(UpdatePlusLabels));
        }

        private void UpdatePlusLabels()
        {
            if (_lPlusApk == null || _lPlusApk.IsDisposed)
            {
                return;
            }
            if (_plusReleases.Count == 0)
            {
                _lPlusApk.Text = L.T("elenco non letto: \"Aggiorna elenco\"", "list not read: \"Refresh list\"");
                _lPlusUsb.Text = _lPlusApk.Text;
                return;
            }
            NbtPlus.Found? apk = NbtPlus.Apk(_plusReleases, PlusApp());
            NbtPlus.Found? usb = NbtPlus.Usb(_plusReleases);
            _lPlusApk.Text = apk != null ? apk.Asset.Name + "  (" + apk.Release.Name + ", " + apk.Release.Published.ToString("yyyy-MM-dd") + ")" : L.T("nessun APK pubblicato", "no APK published");
            _lPlusUsb.Text = usb != null ? usb.Asset.Name + "  (" + usb.Release.Name + ", " + usb.Release.Published.ToString("yyyy-MM-dd") + ")" : L.T("nessuna chiavetta pubblicata", "no stick published");
        }

        private void DoPlusApk()
        {
            NbtPlus.Found? apk = NbtPlus.Apk(_plusReleases, PlusApp());
            if (apk == null)
            {
                Warn(L.T("Nessun APK per questa app: premi \"Aggiorna elenco\".", "No APK for this app: press \"Refresh list\"."));
                return;
            }
            RunTask(L.T("Download di ", "Download of ") + apk.Asset.Name, () =>
            {
                string path = NbtPlus.DownloadApk(apk);
                Ui(() => _plusOut.Text = L.T("APK scaricato e controllato: ", "APK downloaded and checked: ") + path
                    + L.T("\r\nCopialo sul telefono e installalo (una sola app NBT Plus / MINI / BMW / Supra Plus sul telefono).",
                        "\r\nCopy it to the phone and install it (only one NBT Plus / MINI / BMW / Supra Plus app on the phone)."));
                OpenFolder(Path.GetDirectoryName(path)!);
            }, false);
        }

        private void DoPlusInstall()
        {
            NbtPlus.AppChoice app = PlusApp();
            if (_plusReleases.Count == 0)
            {
                Warn(L.T("Elenco non letto: premi \"Aggiorna elenco\".", "List not read: press \"Refresh list\"."));
                return;
            }
            if (!Confirm(L.T("Installo i plugin dell'iDrive (Meteo, Notizie, Notizie locali, Carburanti, Traffico, Stato veicolo) con le pagine per ",
                    "Install the iDrive plugins (Weather, News, Local news, Fuel, Traffic, Vehicle status) with the pages for ") + app
                + L.T("? Sono librerie caricate dall'HMI all'avvio. Le librerie in uso vengono rinominate, non sovrascritte.",
                    "? They are libraries loaded by the HMI at boot. Libraries in use are renamed, not overwritten.")))
            {
                return;
            }
            RunTask(L.T("Installazione dei plugin iDrive", "iDrive plugins install"), () =>
            {
                _plusKit ??= NbtPlus.FetchKit(_plusReleases);
                string o = NbtPlus.Install(_hu!, _plusKit, app);
                Ui(() => _plusOut.Text = o.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            });
        }

        private void DoPlusRemove()
        {
            if (_plusReleases.Count == 0)
            {
                Warn(L.T("Elenco non letto: premi \"Aggiorna elenco\".", "List not read: press \"Refresh list\"."));
                return;
            }
            if (!Confirm(L.T("Tolgo i plugin dell'iDrive (spostati in /opt/aa/plugins_off, recuperabili)? Android Auto non viene toccato.",
                "Remove the iDrive plugins (moved to /opt/aa/plugins_off, recoverable)? Android Auto is not touched.")))
            {
                return;
            }
            RunTask(L.T("Rimozione dei plugin iDrive", "iDrive plugins removal"), () =>
            {
                _plusKit ??= NbtPlus.FetchKit(_plusReleases);
                string o = NbtPlus.Remove(_hu!, _plusKit);
                Ui(() => _plusOut.Text = o.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            });
        }

        // ================================================================== 10. updates

        private sealed class UpdateRow
        {
            public string Key { get; set; } = "";
            public string Name { get; set; } = "";
            public string Author { get; set; } = "";
            public string Status { get; set; } = "";
            public string Installed { get; set; } = "";
            public string Available { get; set; } = "";
            public string Result { get; set; } = "";
            public bool NeedsUpdate { get; set; }
            /// <summary>Found on the head unit at the last check (only what is installed is updated from this page).</summary>
            public bool Present { get; set; }
            /// <summary>Page where it is installed the first time.</summary>
            public string InstallPage { get; set; } = "";
            public GhRelease? Release { get; set; }
        }

        private ListView _updList = null!;
        private readonly List<UpdateRow> _updRows = new List<UpdateRow>();

        private Panel PageUpdates()
        {
            _updList = Theme.List();
            _updList.Columns.Add(L.T("Programma", "Program"), 180);
            _updList.Columns.Add(L.T("Autore", "Author"), 125);
            _updList.Columns.Add(L.T("Stato", "Status"), 130);
            _updList.Columns.Add(L.T("Installato", "Installed"), 175);
            _updList.Columns.Add(L.T("Disponibile", "Available"), 175);
            _updList.Columns.Add(L.T("Esito", "Result"), 150);
            var notes = Theme.Label(L.T(
                "\"Controlla tutto\" confronta quello che c'e' sulla testata collegata (letto e confrontato con lo SHA-256) con le ultime release su GitHub. " +
                "Testato = provato in auto e distribuito; In sviluppo = pubblicato ma ancora in prova; i programmi in sviluppo non ancora provati (MINI Sport) sono solo elencati, non installabili. " +
                "L'app sul telefono non si legge da qui: viene indicata solo l'ultima disponibile.",
                "\"Check all\" compares what is on the connected head unit (read and compared by SHA-256) with the latest releases on GitHub. " +
                "Tested = tried in a car and distributed; Under development = published but still being tried; programs under development not tried yet (MINI Sport) are only listed, not installable. " +
                "The phone app cannot be read from here: only the latest available one is shown."), Theme.Body, Theme.Dim);
            notes.MaximumSize = new Size(900, 0);
            Control content = Grid((_updList, 100), (notes, 0));
            FillUpdateList();
            return Page(L.T("Aggiornamenti", "Updates"),
                L.T("NBT Companion, NBT-AAD e i programmi di DeathWalkeR: versione installata e ultima uscita.",
                    "NBT Companion, NBT-AAD and DeathWalkeR's programs: installed version and latest release."),
                content,
                Act(L.T("Controlla tutto", "Check all"), () => RunTask(L.T("Controllo degli aggiornamenti", "Update check"), CheckAllUpdates, false), true),
                Act(L.T("Aggiorna selezionato", "Update selected"), DoUpdateSelected));
        }

        private void FillUpdateList()
        {
            if (_updList == null || _updList.IsDisposed)
            {
                return;
            }
            string? keep = _updList.SelectedItems.Count == 1 ? (_updList.SelectedItems[0].Tag as UpdateRow)?.Key : null;
            _updList.BeginUpdate();
            _updList.Items.Clear();
            List<UpdateRow> rows = _updRows.Count > 0 ? _updRows : BaseUpdateRows();
            foreach (UpdateRow r in rows)
            {
                var it = new ListViewItem(new[] { r.Name, r.Author, r.Status, r.Installed, r.Available, r.Result })
                {
                    Tag = r,
                    ForeColor = r.NeedsUpdate ? Theme.Warn : r.Key == "minisport" ? Theme.Dim : Theme.Text,
                };
                _updList.Items.Add(it);
                if (r.Key == keep)
                {
                    it.Selected = true;
                }
            }
            _updList.EndUpdate();
        }

        private static List<UpdateRow> BaseUpdateRows()
        {
            string notChecked = L.T("non controllato", "not checked");
            string tested = Tested;
            string dev = UnderDevelopment;
            return new List<UpdateRow>
            {
                new UpdateRow { Key = "companion", Name = "NBT Companion", Author = "DeathWalkeR", Status = dev, Installed = AppInfo.Version, Available = notChecked },
                new UpdateRow { Key = "aad", Name = "NBT-AAD (Android Auto)", Author = "Sq00ky / nanaisu", Status = L.T("Di terzi", "Third party"), Installed = notChecked, Available = notChecked, InstallPage = "3" },
                new UpdateRow { Key = "tile", Name = L.T("Casella home (nbt-aa-tile)", "Home tile (nbt-aa-tile)"), Author = "DeathWalkeR", Status = dev, Installed = notChecked, Available = notChecked, InstallPage = "5" },
                new UpdateRow { Key = "plugins", Name = L.T("NBT Plus - plugin iDrive", "NBT Plus - iDrive plugins"), Author = "DeathWalkeR", Status = tested, Installed = notChecked, Available = notChecked, InstallPage = "9" },
                new UpdateRow { Key = "apk", Name = L.T("NBT Plus - app telefono", "NBT Plus - phone app"), Author = "DeathWalkeR", Status = tested, Installed = L.T("sul telefono (non leggibile)", "on the phone (not readable)"), Available = notChecked },
                new UpdateRow { Key = "minisport", Name = "MINI Sport (nbt-evo-apps)", Author = "DeathWalkeR", Status = dev, Installed = "-", Available = L.T("non distribuito", "not distributed"), Result = L.T("non installabile", "not installable") },
            };
        }

        private void CheckAllUpdates()
        {
            List<UpdateRow> rows = BaseUpdateRows();
            bool unit = _hu != null && _hu.Connected;
            string connect = L.T("collega la testata", "connect the head unit");
            string upToDate = L.T("aggiornato", "up to date");
            string toUpdate = L.T("da aggiornare", "update available");
            string missing = L.T("non installato", "not installed");

            // every row is checked on its own: a failure (GitHub, SSH) is written in that row and the others go on
            void Guard(string key, Action check)
            {
                UpdateRow row = rows.First(r => r.Key == key);
                try
                {
                    check();
                }
                catch (Exception ex)
                {
                    row.NeedsUpdate = false;
                    row.Result = L.T("errore: ", "error: ") + ex.Message.Split('\n')[0];
                    Log.Write("Aggiornamenti - " + row.Name + ": " + ex.Message);
                }
            }

            Guard("companion", () =>
            {
                UpdateRow comp = rows.First(r => r.Key == "companion");
                SelfUpdate.Available? up = SelfUpdate.Check();
                comp.Available = up != null ? up.Version.ToString() : AppInfo.Version;
                comp.NeedsUpdate = up != null;
                comp.Result = up != null ? toUpdate : upToDate;
                comp.Release = up?.Release;
            });

            Guard("aad", () =>
            {
                UpdateRow aad = rows.First(r => r.Key == "aad");
                aad.Installed = connect;
                InstalledAad? inst = null;
                if (unit)
                {
                    // the unit first: what is installed is shown even when GitHub can't be reached
                    inst = AadInstaller.ReadInstalled(_hu!, _releases, ShaToRelease());
                    _installed = inst;
                    aad.Installed = inst.Summary();
                }
                if (_releases.Count == 0)
                {
                    LoadReleases();
                    if (unit)
                    {
                        inst = AadInstaller.ReadInstalled(_hu!, _releases, ShaToRelease());
                        _installed = inst;
                        aad.Installed = inst.Summary();
                    }
                }
                GhRelease? rel = _releases.FirstOrDefault(r => !r.Prerelease);
                GhRelease? pre = _releases.FirstOrDefault();
                aad.Available = rel != null ? rel.Name + (pre != null && pre != rel ? "  (pre-release: " + pre.Name + ")" : "") : "-";
                aad.Release = rel;
                if (inst != null)
                {
                    aad.Present = inst.DaemonPresent;
                    AadUpdate.State st = AadUpdate.Evaluate(_releases, inst.DaemonPresent, inst.DaemonSha);
                    aad.NeedsUpdate = st == AadUpdate.State.Older;
                    aad.Result = st switch
                    {
                        AadUpdate.State.NotInstalled => missing,
                        AadUpdate.State.ListUnreadable => L.T("elenco release non leggibile", "release list not readable"),
                        AadUpdate.State.Unknown => L.T("versione non riconosciuta (pagina 3)", "unknown version (page 3)"),
                        AadUpdate.State.Older => toUpdate,
                        AadUpdate.State.NewPreRelease => L.T("nuova pre-release (pagina 3)", "new pre-release (page 3)"),
                        AadUpdate.State.NewerPreRelease => L.T("pre-release piu' recente", "newer pre-release"),
                        _ => upToDate,
                    };
                }
            });

            Guard("tile", () =>
            {
                UpdateRow tile = rows.First(r => r.Key == "tile");
                if (!unit)
                {
                    tile.Installed = connect;
                }
                _tileKit ??= TileKits.FetchTile();
                tile.Available = _tileKit.ReleaseName;
                if (unit)
                {
                    byte[]? onUnit = _hu!.Download("/opt/aa-tile/aa-tile");
                    byte[]? launcher = onUnit == null ? null : _hu!.Download("/opt/aa-tile/aa-tile-launcher.sh");
                    tile.Present = onUnit != null;
                    tile.Installed = onUnit == null ? L.T("non installata all'avvio", "not installed at boot") : L.T("installata all'avvio", "installed at boot");
                    tile.NeedsUpdate = onUnit != null && (!onUnit.SequenceEqual(_tileKit.Files["aa-tile"])
                        || launcher == null || !launcher.SequenceEqual(_tileKit.Files["aa-tile-launcher.sh"]));
                    tile.Result = onUnit == null ? missing : tile.NeedsUpdate ? toUpdate : upToDate;
                }
            });

            Guard("plugins", () =>
            {
                UpdateRow plugins = rows.First(r => r.Key == "plugins");
                if (!unit)
                {
                    plugins.Installed = connect;
                }
                if (_plusReleases.Count == 0)
                {
                    LoadPlusReleases();
                }
                NbtPlus.Found? usb = NbtPlus.Usb(_plusReleases);
                plugins.Available = usb != null ? usb.Asset.Name + " (" + usb.Release.Name + ")" : "-";
                if (unit && usb != null)
                {
                    _plusKit ??= NbtPlus.FetchKit(_plusReleases);
                    int present = 0;
                    int same = 0;
                    foreach (string n in NbtPlus.Plugins)
                    {
                        byte[]? lib = _hu!.Download(HeadUnit.HmiRo + "/Plugins/" + n + "/lib" + n + ".so");
                        if (lib == null)
                        {
                            continue;
                        }
                        present++;
                        if (lib.SequenceEqual(_plusKit.Files[n + "/lib" + n + ".so"]))
                        {
                            same++;
                        }
                    }
                    plugins.Present = present > 0;
                    plugins.Installed = present + L.T(" plugin su ", " plugins of ") + NbtPlus.Plugins.Length;
                    plugins.NeedsUpdate = present > 0 && same < NbtPlus.Plugins.Length;
                    plugins.Result = present == 0 ? missing : same == NbtPlus.Plugins.Length ? upToDate : toUpdate + " (" + (NbtPlus.Plugins.Length - same) + ")";
                }
            });

            Guard("apk", () =>
            {
                UpdateRow apk = rows.First(r => r.Key == "apk");
                if (_plusReleases.Count == 0)
                {
                    LoadPlusReleases();
                }
                NbtPlus.Found? a = NbtPlus.Apk(_plusReleases, Ui(PlusApp));
                apk.Available = a != null ? a.Asset.Name : "-";
                apk.Result = a != null ? L.T("scaricabile", "downloadable") : L.T("non trovata", "not found");
            });

            foreach (UpdateRow r in rows)
            {
                Log.Write("Aggiornamenti - " + r.Name + " | " + L.T("installato: ", "installed: ") + r.Installed + " | " + L.T("disponibile: ", "available: ") + r.Available
                    + (r.Result.Length > 0 ? " | " + L.T("esito: ", "result: ") + r.Result : ""));
            }
            _updRows.Clear();
            _updRows.AddRange(rows);
            BeginInvoke(new Action(FillUpdateList));
        }

        private void DoUpdateSelected()
        {
            UpdateRow? r = _updList.SelectedItems.Count == 1 ? _updList.SelectedItems[0].Tag as UpdateRow : null;
            if (r == null)
            {
                Warn(L.T("Seleziona una riga dell'elenco.", "Select a row of the list."));
                return;
            }
            if (r.InstallPage.Length > 0)
            {
                // updates only: the first install has its own page, with its own checks and tests
                if (!r.Present)
                {
                    Warn(r.Name + L.T(": non risulta installato sulla testata (premi \"Controlla tutto\" con la testata collegata). La prima installazione si fa dalla pagina ",
                        ": not found on the head unit (press \"Check all\" with the head unit connected). The first install is done from page ") + r.InstallPage + ".");
                    return;
                }
                if (!r.NeedsUpdate)
                {
                    Warn(r.Name + L.T(": niente da aggiornare (" + r.Result + ").", ": nothing to update (" + r.Result + ")."));
                    return;
                }
                _afterTask = () => RunTask(L.T("Nuovo controllo degli aggiornamenti", "Update check again"), CheckAllUpdates, false);
            }
            DispatchUpdate(r);
            _afterTask = null;
        }

        private void DispatchUpdate(UpdateRow r)
        {
            switch (r.Key)
            {
                case "companion":
                    RunTask(L.T("Controllo aggiornamenti di NBT Companion", "NBT Companion update check"), () => CheckSelfUpdate(true), false);
                    break;
                case "aad":
                    if (r.Release == null)
                    {
                        Warn(L.T("Premi prima \"Controlla tutto\".", "Press \"Check all\" first."));
                        return;
                    }
                    if (_hu == null || !_hu.Connected)
                    {
                        Warn(L.T("Prima collega la testata (pagina 1).", "Connect the head unit first (page 1)."));
                        return;
                    }
                    InstallRelease(r.Release);
                    break;
                case "tile":
                    if (Confirm(L.T("Aggiorno la casella all'avvio con il kit ", "Update the boot tile with kit ") + (_tileKit?.ReleaseName ?? "") + L.T(" (modo attuale mantenuto)?", " (current mode kept)?")))
                    {
                        TileRun(L.T("Aggiornamento della casella", "Tile update"), k =>
                        {
                            CmdResult m = _hu!.OmapShell("cat /var/opt/aa-tile/mode", 20, null, true);
                            string mode = m.Text.Trim() == "always" ? "always" : "follow";
                            return TileKits.TileInstallBoot(_hu!, k, mode);
                        });
                    }
                    break;
                case "plugins":
                    DoPlusInstall();
                    break;
                case "apk":
                    DoPlusApk();
                    break;
                default:
                    Warn(L.T("MINI Sport e' in sviluppo e non ancora provato in auto: non installabile da qui.",
                        "MINI Sport is under development and not tried in a car yet: not installable from here."));
                    break;
            }
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
