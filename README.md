# NBT Companion

One Windows program (a single `.exe`, nothing to install) for **BMW / MINI / Toyota Supra NBT EVO (ID5/ID6)** head
units: it connects to the car over the ENET cable, opens SSH if it is closed, installs / updates / restores
[NBT-AAD](https://github.com/Sq00ky/Supra-iDrive6-Android-Auto) (native Android Auto by Sq00ky / nanaisu) with a few
options, and can close SSH again at the end. No PuTTY, no USB stick.

**Status: 1.1.0, not tested in a car yet.** The transfer layer and every head-unit script were run against a simulated
unit (OpenSSH + the same shell scripts); the first car tests are on a MINI F60 (`NBTevo_Y21432J`).

## What it does

| Program | Author | Status |
|---|---|---|
| NBT Plus: iDrive plugins and phone apps ([idrive-plus](https://github.com/deathwalker80/idrive-plus)) | DeathWalkeR | **Tested** (tried in a car and distributed) |
| NBT Companion | DeathWalkeR | Under development |
| Home screen tile and former CarLife look ([nbt-aa-tile](https://github.com/deathwalker80/nbt-aa-tile)) | DeathWalkeR | Under development |
| MINI Sport (nbt-evo-apps) | DeathWalkeR | Under development, not tried in a car: listed only, not installable |
| NBT-AAD | Sq00ky / nanaisu | Third party |

| Page | |
|---|---|
| 1 Connection | Finds the head unit on the diagnostic network (HSFZ announcement, UDP 6811) and reads the VIN, checks SSH port 22, logs in as `root` (address and ID5/ID6 default password pre-filled and editable; the password is never saved) and runs a transfer self-test: a test file with all 256 byte values is written to hu-omap's RAM and read back. Then it reads what the unit is: the software version iDrive shows under Navigation > Settings > Position and software version (`/opt/sys/etc/nbt_version.txt`, e.g. `NBTevo_Y21432J`; a version that does not start with `NBTevo_` is flagged) and the brand in the HMI. **Automatic connection:** with the cable in, the app tries port 22 on the address of page 1, the HSFZ gateway and `169.254.199.99` / `169.254.199.119` every few seconds and connects with the password of page 1; it stops after a refused password or a manual "Disconnect", and starts again with "Connect" or when the cable is plugged in again. |
| 2 SSH | Opens SSH when it is closed and closes it again (see below). |
| 3 Android Auto (NBT-AAD) | Lists releases **and pre-releases** (sorted by date, with notes), downloads `nbt-aad` + `Releases.zip`, checks GitHub's SHA-256, ARM/QNX ELF, the 5 plugin files, free space, and that the new binary starts on the unit (`--license`) before anything is changed. Update as in Sq00ky's guide: stop nbt-aad, `nbt-aad` -> `nbt-aad.old`, plugin folder -> `/opt/aa/NbtAad.old` (never inside `Plugins`), copy the new one, read back every file (SHA-256), firewall rule with the built-in `--pf-check` / `--pf-install`. Keeps 1 or 2 versions. One-click restore of the previous version (swap). `nbt-aad.env` is not touched. |
| 4 Menu position | Moves the "Android Auto" and "Open Android Auto" entries between Media/Radio (`insertStateID="20"`) and MINI Connected / ConnectedDrive (`"50"`; checked on MINI, not checked on BMW). Done automatically at every update if the option is on. |
| 5 Home screen tile | [nbt-aa-tile](https://github.com/deathwalker80/nbt-aa-tile): RAM test, boot install, removal, status. |
| 6 Former CarLife look | nbt-aa-tile's `aa-menu-look`: "Android Auto" texts and your own PNGs instead of the CarLife ones (MINI, BMW, BMW M, BMW i), factory files backed up once, full restore. |
| 7 nbt-aad.env | Read, edit, write back (previous version saved on the PC). |
| 8 Diagnostics | Read-only ZIP with logs and state. Never raw device reads. |
| 9 NBT Plus apps | [iDrive Plus / NBT Plus](https://github.com/deathwalker80/idrive-plus): downloads the phone APK (generic NBT Plus or MINI / BMW / Supra Plus, GitHub SHA-256 checked) and installs or removes the iDrive plugins of the latest `USB_vNN.zip` with its own `00-installa.sh` / `10-togli.sh`, run from hu-omap's RAM. |
| 10 Updates | One list with NBT Companion, NBT-AAD and DeathWalkeR's programs: what is on the connected unit (compared by SHA-256) against the latest release on GitHub, and "Update selected". Only what is already installed is updated from here (the first install stays on its own page). NBT-AAD: only a known build older than the latest release is offered; a newer pre-release is never taken back, a new pre-release is only signalled (install it from page 3), an unknown build is left alone. After an update the list is checked again. |

Colour icons of the Android Auto entry (`NbtAad_ALL_Images.zip`) are kept at every update: the ones of the plugin on
the unit, else the copy kept on the PC, else a file you choose.

**Self-update:** at start (and from the "Updates" page) the app looks at the releases of this repository
(no pre-releases). A release must have the asset `NBT-Companion.exe`: the app checks GitHub's SHA-256 and the version
inside the exe, renames itself to `NBT-Companion.exe.old`, puts the new exe in its place and restarts. It asks before
updating. It works only while the repository is public.

Every operation is written to a complete log (`%LOCALAPPDATA%\NBT-Companion\logs`). The password never reaches it.
The app never reboots the head unit: reboot by hand (power button 30 s).

## Opening / closing SSH

The app uses Xayanix's [nbt-evo-ssh-unlocker](https://github.com/Xayanix/nbt-evo-ssh-unlocker). That repository has
no licence, so **none of its code is in this program or this repository**: the app downloads
`nbt_optdebug_flash.py` from GitHub onto your PC, pinned to commit `1791228` (7 Nov 2026) and checked with its
SHA-256, runs its self-test, then runs it with your Python 3 (`--commit` to open, `--commit --revert` to close).
A newer commit of that repository is used only after you approve it in the app.

What the script does (from its README): HSFZ 6801, programming session, security access level 3, then it writes
`/fs/sda0/opt/sys/etc/starter.cfg` with `OPT_DEBUG` from `STOP` to `RUN` directly on the running unit; after the
reboot SSH 22, telnet 23 and qconn 8000 are open as root. Closing writes the factory `starter.cfg` back.
NBT-Evo 007.xxx family only, **not** 001_05x. SSH left open can raise fault code B7F8E5
([NBT-AAD issue #190](https://github.com/Sq00ky/Supra-iDrive6-Android-Auto/issues/190)).

## Requirements

* Windows 10/11 x64.
* ENET (OBD to Ethernet) cable, network adapter with a `169.254.x.x` address.
* For opening/closing SSH only: Python 3 (the app can start `winget install Python.Python.3.12` for you).

## Build

```
dotnet publish src/NbtCompanion/NbtCompanion.csproj -c Release -o out
```

.NET 8 SDK, output `out/NBT-Companion.exe` (self-contained single file). SSH.NET 2024.2.0 is used on purpose: it is
the last version with `ssh-dss` host keys. Its two published advisories are about `ScpClient`, which this program
does not use.

## Licence and credits

PolyForm Noncommercial 1.0.0, see `LICENSE.md`. Noncommercial use only.

* **Sq00ky / nanaisu** - NBT-AAD (PolyForm Noncommercial). This app does not include or redistribute its binaries:
  it downloads them from GitHub onto the user's PC.
* **Xayanix** - nbt-evo-ssh-unlocker (downloaded and run on the user's PC, not included).
* **RRudkinp** - the CarLife tile mechanism and the `text.db` work behind nbt-aa-tile and aa-menu-look.
* **mhmm-ok** - ideas from the AutoUpdating APK installer (pre-checks, `--license` start test).
* **DeathWalkeR** - this program.

Not affiliated with or endorsed by BMW, MINI, Toyota, Google, Sq00ky or Xayanix. Use at your own risk.

## In italiano

App Windows unica per le NBT EVO: collegamento via cavo ENET, apertura/chiusura SSH (script di Xayanix scaricato sul
PC, non incluso), installazione/aggiornamento/ripristino di NBT-AAD con controlli prima di scrivere, voci in MINI
Connected / ConnectedDrive, casella nella home, grafica ex CarLife, `nbt-aad.env`, diagnostica. Interfaccia in
italiano e inglese. Indirizzo e password predefiniti ID5/ID6 gia' inseriti e modificabili; password mai salvata. Si aggiorna da solo dalle release di GitHub (chiede prima). Riavvio della testata sempre a mano.

Collegamento automatico: col cavo inserito l'app prova da sola la porta 22 sugli indirizzi standard e si collega con la
password predefinita; legge la versione software della testata (quella di Navigazione > Impostazioni > Posizione e
versione software, es. `NBTevo_Y21432J`) e la marca nell'HMI. Si ferma dopo una password rifiutata o uno "Scollega".

Pagina 10 "Aggiornamenti": NBT Companion, NBT-AAD e i programmi di DeathWalkeR in un unico elenco, con quello che c'e'
sulla testata (confronto SHA-256) e l'ultima release su GitHub; aggiorna solo cio' che e' gia' installato. Stato:
**testati** solo NBT Plus (plugin iDrive e app); NBT Companion, casella nella home e grafica ex CarLife **in sviluppo**;
MINI Sport in sviluppo e non ancora provato in auto, quindi solo elencato e non installabile; NBT-AAD e' di terzi.
