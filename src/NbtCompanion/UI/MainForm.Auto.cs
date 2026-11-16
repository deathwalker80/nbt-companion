using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NbtCompanion.Core;

namespace NbtCompanion.UI
{
    /// <summary>
    /// Automatic connection: every few seconds, while nothing else is running and no unit is connected, the app looks
    /// for a 169.254.x.x adapter, tries port 22 on the address of page 1, the HSFZ gateway and the standard addresses,
    /// and connects with the password of page 1 (ID5/ID6 default). Only read-only checks until the real connection,
    /// which is the same as the "Connect" button. It never touches what the user is typing.
    /// </summary>
    public partial class MainForm
    {
        private const int AutoIntervalMs = 4000;
        private const int AutoPortTimeoutMs = 400;
        private const int AutoDiscoverEverySeconds = 20;

        private System.Windows.Forms.Timer? _autoTimer;
        /// <summary>Off after a manual disconnection or a refused password; on again with "Connect" or a new cable link.</summary>
        private volatile bool _autoOff;
        private volatile bool _autoProbing;
        private bool _autoHadLink;
        private bool _autoClosedLogged;
        private DateTime _autoLastDiscover = DateTime.MinValue;
        /// <summary>After an automatic attempt the next one waits: a unit that refuses does not fill the log.</summary>
        private DateTime _autoRetryAt = DateTime.MinValue;
        private const int AutoRetrySeconds = 30;

        private void StartAutoConnect()
        {
            _autoTimer = new System.Windows.Forms.Timer { Interval = AutoIntervalMs };
            _autoTimer.Tick += (s, e) => AutoConnectTick();
            _autoTimer.Start();
        }

        private void AutoConnectTick()
        {
            if (_busy || _autoProbing || (_hu != null && _hu.Connected) || DateTime.Now < _autoRetryAt)
            {
                return;
            }
            // the user is writing the address or the password: wait
            if (_tHost == null || _tPassword == null || _tHost.Focused || _tPassword.Focused)
            {
                return;
            }
            string configured = _tHost.Text.Trim();
            if (_tPassword.Text.Length == 0)
            {
                return;
            }
            _autoProbing = true;
            Task.Run(() =>
            {
                try
                {
                    AutoProbe(configured);
                }
                catch (Exception ex)
                {
                    Log.Write("Collegamento automatico: " + ex.Message);
                }
                finally
                {
                    _autoProbing = false;
                }
            });
        }

        private void AutoProbe(string configured)
        {
            List<IPAddress> ll = DiagNet.LinkLocalAddresses();
            if (ll.Count == 0)
            {
                if (_autoHadLink)
                {
                    // cable unplugged: a new link starts from scratch, automatic connection on again
                    _autoHadLink = false;
                    _autoOff = false;
                    _autoClosedLogged = false;
                    _disc = null;
                    _sshOpen = false;
                    RefreshState();
                }
                return;
            }
            if (!_autoHadLink)
            {
                _autoHadLink = true;
                _autoOff = false;
                _autoLastDiscover = DateTime.MinValue;
                Log.Write("Collegamento automatico: scheda 169.254 presente (" + string.Join(", ", ll) + "), cerco la testata");
                RefreshState();
            }
            if (_autoOff)
            {
                return;
            }
            string? host = DiagNet.FindSsh(configured, _disc?.Gateway, AutoPortTimeoutMs);
            if (host == null && (DateTime.Now - _autoLastDiscover).TotalSeconds >= AutoDiscoverEverySeconds)
            {
                _autoLastDiscover = DateTime.Now;
                DiscoveryResult? d = DiagNet.Discover(1500);
                if (d != null && (_disc == null || _disc.Gateway != d.Gateway || _disc.Vin != d.Vin))
                {
                    _disc = d;
                    Log.Write("Collegamento automatico: annuncio HSFZ da " + d.Gateway + ", telaio " + (d.Vin.Length > 0 ? d.Vin : "(non letto)"));
                    RefreshState();
                    host = DiagNet.FindSsh(configured, d.Gateway, AutoPortTimeoutMs);
                }
            }
            if (host == null)
            {
                if (_disc != null && !_autoClosedLogged)
                {
                    _autoClosedLogged = true;
                    Log.Write("Collegamento automatico: testata trovata ma la porta SSH 22 e' chiusa su tutti gli indirizzi standard (aprila dalla pagina 2)");
                }
                return;
            }
            _sshOpen = true;
            BeginInvoke(new Action(() =>
            {
                if (_autoOff || _busy || (_hu != null && _hu.Connected) || _tHost.Focused || _tPassword.Focused)
                {
                    return;
                }
                Log.Write("Collegamento automatico: porta SSH 22 aperta su " + host);
                _autoRetryAt = DateTime.Now.AddSeconds(AutoRetrySeconds);
                ConnectTo(true);
            }));
        }
    }
}
