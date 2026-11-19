using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NbtCompanion.Core;

namespace NbtCompanion.UI
{
    /// <summary>Page 11: the apps of nbt-evo-apps that run on the head unit (MINI Sport and the next ones).</summary>
    public partial class MainForm
    {
        private sealed class CarChoice
        {
            public string Key { get; set; } = "";
            public string Label { get; set; } = "";

            public override string ToString()
            {
                return Label;
            }
        }

        private static readonly CarChoice[] Cars =
        {
            new CarChoice { Key = "mini", Label = "MINI" },
            new CarChoice { Key = "bmw", Label = "BMW" },
            new CarChoice { Key = "supra", Label = "Toyota Supra" },
        };

        private List<GhRelease> _evoReleases = new List<GhRelease>();
        private EvoApps.EvoKit? _evoKit;
        private readonly Dictionary<string, string> _evoInstalled = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool _evoInstalledRead;
        private ListView _evoList = null!;
        private ComboBox _cbCar = null!;
        private TextBox _evoOut = null!;
        private Label _lEvoRelease = null!;

        private Panel PageEvo()
        {
            _cbCar = Theme.Combo();
            _cbCar.Width = 200;
            _cbCar.Items.AddRange(Cars.Cast<object>().ToArray());
            _cbCar.SelectedIndexChanged += (s, e) => FillEvoList();
            _lEvoRelease = Theme.Label(L.T("elenco non letto: \"Leggi elenco\"", "list not read: \"Read list\""), Theme.Body, Theme.Text);
            _evoList = Theme.List();
            _evoList.MultiSelect = true;
            _evoList.Columns.Add(L.T("App", "App"), 190);
            _evoList.Columns.Add(L.T("Pacchetto", "Package"), 120);
            _evoList.Columns.Add(L.T("Per", "For"), 90);
            _evoList.Columns.Add(L.T("Stato", "Status"), 130);
            _evoList.Columns.Add(L.T("Release", "Release"), 90);
            _evoList.Columns.Add(L.T("Installata", "Installed"), 110);
            _evoList.Columns.Add(L.T("Esito", "Result"), 150);
            _evoOut = Output();

            var form = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Theme.Panel };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.Controls.Add(FormLabel(L.T("Auto", "Car")), 0, 0);
            form.Controls.Add(Row(_cbCar, Theme.Label(L.T("dalla marca letta nell'HMI; le app per un'altra marca non si installano",
                "from the brand read in the HMI; apps for another brand are not installed"), Theme.Small, Theme.Dim)), 1, 0);
            form.Controls.Add(FormLabel(L.T("Release", "Release")), 0, 1);
            form.Controls.Add(_lEvoRelease, 1, 1);
            var notes = Theme.Label(L.T(
                "Da deathwalker80/nbt-evo-apps: app che girano sulla testata (MINI Sport e le prossime). Lo zip della release viene controllato " +
                "(SHA-256 di GitHub, files.sum di ogni pacchetto, librerie QNX ARM), copiato nella cartella di appoggio di hu-omap (/var/opt/nbtc) e installato col pkg.sh della chiavetta: " +
                "prima di un aggiornamento la versione in uso resta come precedente (una sola), \"Togli\" sposta l'app in /opt/aa/plugins_off. " +
                "Riavvio a mano: tasto di accensione tenuto premuto 30 secondi. \"Scarica zip\" salva lo zip in Documenti\\NBT-Companion\\nbt-evo-apps per la chiavetta USB.",
                "From deathwalker80/nbt-evo-apps: apps that run on the head unit (MINI Sport and the next ones). The release zip is checked " +
                "(GitHub SHA-256, files.sum of every package, QNX ARM libraries), copied into the staging folder on hu-omap (/var/opt/nbtc) and installed with the stick's pkg.sh: " +
                "before an update the version in use is kept as the previous one (only one), \"Remove\" moves the app to /opt/aa/plugins_off. " +
                "Reboot by hand: hold the power button for 30 seconds. \"Download zip\" saves the zip in Documents\\NBT-Companion\\nbt-evo-apps for a USB stick."),
                Theme.Body, Theme.Dim);
            notes.MaximumSize = new Size(880, 0);
            Control content = Grid((form, 0), (_evoList, 60), (notes, 0), (_evoOut, 40));
            FillEvoList();
            return Page(L.T("App NBT EVO (nbt-evo-apps)", "NBT EVO apps (nbt-evo-apps)"),
                L.T("Le app di DeathWalkeR che girano direttamente sulla testata, senza telefono: elenco, versione installata e ultima release.",
                    "DeathWalkeR's apps that run on the head unit itself, without a phone: list, installed version and latest release."),
                content,
                Act(L.T("Leggi elenco", "Read list"), () => RunTask(L.T("Elenco delle app NBT EVO", "NBT EVO apps list"), LoadEvo, false), true),
                Act(L.T("Installa / aggiorna", "Install / update"), () => DoEvoInstall(SelectedEvo())),
                Act(L.T("Togli", "Remove"), DoEvoRemove),
                Act(L.T("Versione precedente", "Previous version"), DoEvoRestore),
                Act(L.T("Scarica zip", "Download zip"), DoEvoZip));
        }

        private void UiDo(Action a)
        {
            if (InvokeRequired)
            {
                Invoke(a);
            }
            else
            {
                a();
            }
        }

        private string EvoCar()
        {
            return (_cbCar.SelectedItem as CarChoice)?.Key ?? "";
        }

        /// <summary>Release list and stick zip (checked); with the unit connected also the installed versions and the car.</summary>
        private void LoadEvo()
        {
            _evoReleases = GitHub.Releases(EvoApps.Repo);
            EvoApps.Found? f = EvoApps.Latest(_evoReleases);
            Log.Write("nbt-evo-apps online: " + _evoReleases.Count + " pubblicazioni; zip della chiavetta " + (f != null ? f.Asset.Name + " (" + f.Release.Name + ")" : "-"));
            _evoKit = f != null ? EvoApps.Fetch(_evoReleases) : null;
            ReadEvoInstalled();
            UiDo(() =>
            {
                _lEvoRelease.Text = f != null ? f.Asset.Name + "  (" + f.Release.Name + ", " + f.Release.Published.ToString("yyyy-MM-dd") + ")"
                    : L.T("nessuna release con lo zip della chiavetta pubblicata finora", "no release with the stick zip published so far");
                FillEvoList();
            });
        }

        private void ReadEvoInstalled()
        {
            _evoInstalled.Clear();
            _evoInstalledRead = false;
            if (_hu == null || !_hu.Connected || _evoKit == null)
            {
                return;
            }
            foreach (EvoPackage p in _evoKit.Packages)
            {
                _evoInstalled[p.Name] = EvoApps.Installed(_hu, p.Name);
            }
            _evoInstalledRead = true;
            string car = EvoApps.SavedCar(_hu);
            if (car.Length == 0 && _unit != null)
            {
                car = EvoApps.CarFromBrands(_unit.Brands);
            }
            UiDo(() =>
            {
                if (_cbCar.SelectedItem == null)
                {
                    _cbCar.SelectedItem = Cars.FirstOrDefault(c => c.Key == car);
                }
            });
        }

        private void FillEvoList()
        {
            if (_evoList == null || _evoList.IsDisposed)
            {
                return;
            }
            var keep = new HashSet<string>(_evoList.SelectedItems.Cast<ListViewItem>().Select(i => (i.Tag as EvoPackage)?.Name ?? ""));
            _evoList.BeginUpdate();
            _evoList.Items.Clear();
            if (_evoKit != null)
            {
                string car = EvoCar();
                foreach (EvoPackage p in _evoKit.Packages)
                {
                    string inst = !_evoInstalledRead ? L.T("collega la testata", "connect the head unit")
                        : _evoInstalled.TryGetValue(p.Name, out string? v) && v.Length > 0 ? v : "-";
                    string forCar = p.ForCar(car);
                    string result = forCar == "no" ? L.T("non per questa auto", "not for this car")
                        : !_evoInstalledRead ? ""
                        : inst == "-" ? L.T("non installata", "not installed")
                        : inst == p.Version ? L.T("aggiornata", "up to date")
                        : L.T("da aggiornare", "update available");
                    var it = new ListViewItem(new[]
                    {
                        p.Title, p.Name, p.Brands.Length == 0 ? "all" : p.Brands, p.Tested ? Tested : UnderDevelopment, p.Version, inst, result,
                    })
                    {
                        Tag = p,
                        ForeColor = forCar == "no" ? Theme.Dim : result == L.T("da aggiornare", "update available") ? Theme.Warn : Theme.Text,
                    };
                    _evoList.Items.Add(it);
                    it.Selected = keep.Contains(p.Name);
                }
            }
            _evoList.EndUpdate();
        }

        private List<EvoPackage> SelectedEvo()
        {
            return _evoList.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag as EvoPackage).Where(p => p != null).Select(p => p!).ToList();
        }

        private bool EvoReady(List<EvoPackage> sel, bool one)
        {
            if (_evoKit == null)
            {
                Warn(L.T("Premi prima \"Leggi elenco\" (serve una release con lo zip della chiavetta).",
                    "Press \"Read list\" first (a release with the stick zip is needed)."));
                return false;
            }
            if (sel.Count == 0 || (one && sel.Count != 1))
            {
                Warn(one ? L.T("Seleziona una app nell'elenco.", "Select one app in the list.") : L.T("Seleziona una o piu' app nell'elenco.", "Select one or more apps in the list."));
                return false;
            }
            if (_hu == null || !_hu.Connected)
            {
                Warn(L.T("Prima collega la testata (pagina 1).", "Connect the head unit first (page 1)."));
                return false;
            }
            return true;
        }

        private void DoEvoInstall(List<EvoPackage> sel)
        {
            if (!EvoReady(sel, false))
            {
                return;
            }
            string car = EvoCar();
            if (car.Length == 0)
            {
                Warn(L.T("Scegli l'auto (MINI, BMW o Supra).", "Choose the car (MINI, BMW or Supra)."));
                return;
            }
            EvoPackage? wrong = sel.FirstOrDefault(p => p.ForCar(car) == "no");
            if (wrong != null)
            {
                Warn(wrong.Title + L.T(" e' per ", " is for ") + wrong.Brands + L.T(": non si installa su questa auto.", ": it is not installed on this car."));
                return;
            }
            EvoPackage? dev = sel.FirstOrDefault(p => !p.Tested);
            string list = string.Join("\r\n", sel.Select(p => "  " + p.Title + " " + p.Version + " (" + (p.Tested ? Tested : UnderDevelopment) + ")"));
            if (!Confirm(L.T("Installo sulla testata (auto: ", "Install on the head unit (car: ") + car + "):\r\n" + list + "\r\n\r\n"
                + (dev != null ? L.T("ATTENZIONE: \"" + dev.Title + "\" e' in sviluppo, non ancora provata in auto.\r\n\r\n", "WARNING: \"" + dev.Title + "\" is under development, not tried in a car yet.\r\n\r\n") : "")
                + L.T("La versione in uso resta come precedente. Dopo: riavvio a mano, tasto di accensione tenuto premuto 30 secondi.",
                    "The version in use is kept as the previous one. Then: reboot by hand, hold the power button for 30 seconds.")))
            {
                return;
            }
            string phone = PlusApp().Key == "nbt" ? "nbt" : "brand";
            EvoApps.EvoKit k = _evoKit!;
            var names = sel.Select(p => p.Name).ToList();
            RunTask(L.T("Installazione app NBT EVO", "NBT EVO apps install"), () =>
            {
                string o = EvoApps.Install(_hu!, k, car, phone, names);
                ReadEvoInstalled();
                UiDo(() =>
                {
                    _evoOut.Text = o.Replace("\r\n", "\n").Replace("\n", "\r\n");
                    FillEvoList();
                });
                Log.Write(L.T("Installate: ", "Installed: ") + string.Join(", ", names) + L.T(". Ora riavvia a mano (30 secondi sul tasto).", ". Now reboot by hand (30 seconds on the button)."));
            });
        }

        private void DoEvoRemove()
        {
            List<EvoPackage> sel = SelectedEvo();
            if (!EvoReady(sel, true))
            {
                return;
            }
            EvoPackage p = sel[0];
            if (!Confirm(L.T("Tolgo \"", "Remove \"") + p.Title + L.T("\"? Viene spostata in /opt/aa/plugins_off (recuperabile). Poi riavvio a mano.",
                "\"? It is moved to /opt/aa/plugins_off (recoverable). Then reboot by hand.")))
            {
                return;
            }
            EvoApps.EvoKit k = _evoKit!;
            RunTask(L.T("Rimozione app NBT EVO", "NBT EVO app removal"), () =>
            {
                string o = EvoApps.Remove(_hu!, k, p.Name);
                ReadEvoInstalled();
                UiDo(() =>
                {
                    _evoOut.Text = o.Replace("\r\n", "\n").Replace("\n", "\r\n");
                    FillEvoList();
                });
            });
        }

        private void DoEvoRestore()
        {
            List<EvoPackage> sel = SelectedEvo();
            if (!EvoReady(sel, true))
            {
                return;
            }
            EvoPackage p = sel[0];
            if (!Confirm(L.T("Rimetto la versione precedente di \"", "Put back the previous version of \"") + p.Title
                + L.T("\" (quella tenuta da pkg.sh all'ultima installazione)? Poi riavvio a mano.", "\" (the one pkg.sh kept at the last install)? Then reboot by hand.")))
            {
                return;
            }
            EvoApps.EvoKit k = _evoKit!;
            RunTask(L.T("Versione precedente app NBT EVO", "NBT EVO app previous version"), () =>
            {
                string o = EvoApps.Restore(_hu!, k, p.Name);
                ReadEvoInstalled();
                UiDo(() =>
                {
                    _evoOut.Text = o.Replace("\r\n", "\n").Replace("\n", "\r\n");
                    FillEvoList();
                });
            });
        }

        private void DoEvoZip()
        {
            if (_evoKit == null)
            {
                Warn(L.T("Premi prima \"Leggi elenco\" (serve una release con lo zip della chiavetta).",
                    "Press \"Read list\" first (a release with the stick zip is needed)."));
                return;
            }
            EvoApps.EvoKit k = _evoKit;
            RunTask(L.T("Zip della chiavetta nbt-evo-apps", "nbt-evo-apps stick zip"), () =>
            {
                string path = EvoApps.SaveZip(k);
                UiDo(() => OpenFolder(System.IO.Path.GetDirectoryName(path)!));
            }, false);
        }
    }
}
