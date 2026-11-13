using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NbtCompanion.Core
{
    public sealed class DiscoveryResult
    {
        public string Gateway { get; set; } = "";
        public string Vin { get; set; } = "";
        public string LocalAddress { get; set; } = "";
        public string RawIdent { get; set; } = "";
    }

    /// <summary>Diagnostic network: link-local adapters, HSFZ vehicle announcement (UDP 6811), TCP port probe.</summary>
    public static class DiagNet
    {
        public const int AnnouncePort = 6811;

        /// <summary>Default SSH addresses tried after the configured/discovered one.</summary>
        public static readonly string[] DefaultHosts = { "169.254.199.99", "169.254.199.119" };

        private static bool _adapterErrorLogged;

        public static List<IPAddress> LinkLocalAddresses()
        {
            var list = new List<IPAddress>();
            NetworkInterface[] all;
            try
            {
                all = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (NetworkInformationException ex)
            {
                if (!_adapterErrorLogged)
                {
                    _adapterErrorLogged = true;
                    Log.Write("Elenco delle schede di rete non leggibile: " + ex.Message);
                }
                return list;
            }
            foreach (NetworkInterface ni in all)
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }
                foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }
                    byte[] b = ua.Address.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254)
                    {
                        list.Add(ua.Address);
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// HSFZ identification: 6 bytes (length 0, control word 0x0011) broadcast to 169.254.255.255:6811 from every
        /// link-local address. The answer is "...BMWVIN" + 17 characters of VIN.
        /// </summary>
        public static DiscoveryResult? Discover(int timeoutMs)
        {
            byte[] request = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x11 };
            foreach (IPAddress local in LinkLocalAddresses())
            {
                try
                {
                    using var udp = new UdpClient(new IPEndPoint(local, 0));
                    udp.EnableBroadcast = true;
                    udp.Client.ReceiveTimeout = timeoutMs;
                    udp.Send(request, request.Length, new IPEndPoint(IPAddress.Parse("169.254.255.255"), AnnouncePort));
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] answer = udp.Receive(ref remote);
                    var r = ParseIdent(answer);
                    r.Gateway = remote.Address.ToString();
                    r.LocalAddress = local.ToString();
                    return r;
                }
                catch (SocketException)
                {
                    // no answer on this adapter
                }
            }
            return null;
        }

        public static DiscoveryResult ParseIdent(byte[] answer)
        {
            var r = new DiscoveryResult();
            if (answer.Length > 6)
            {
                string ident = Encoding.Latin1.GetString(answer, 6, answer.Length - 6);
                r.RawIdent = ident;
                int i = ident.IndexOf("BMWVIN", StringComparison.Ordinal);
                if (i >= 0 && ident.Length >= i + 6 + 17)
                {
                    r.Vin = ident.Substring(i + 6, 17);
                }
            }
            return r;
        }

        public static bool PortOpen(string host, int port, int timeoutMs)
        {
            try
            {
                using var c = new TcpClient();
                Task t = c.ConnectAsync(host, port);
                return t.Wait(timeoutMs) && c.Connected;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>First address with port 22 open: configured one, discovered gateway, then the known defaults.</summary>
        public static string? FindSsh(string configured, string? gateway, int timeoutMs)
        {
            var hosts = new List<string>();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                hosts.Add(configured.Trim());
            }
            if (!string.IsNullOrWhiteSpace(gateway))
            {
                hosts.Add(gateway!);
            }
            hosts.AddRange(DefaultHosts);
            foreach (string h in hosts.Distinct())
            {
                if (PortOpen(h, 22, timeoutMs))
                {
                    return h;
                }
            }
            return null;
        }

        /// <summary>Waits until port 22 answers on one of the addresses (after an unlock the unit reboots).</summary>
        public static string? WaitForSsh(string configured, string? gateway, int seconds, CancellationToken ct)
        {
            DateTime end = DateTime.Now.AddSeconds(seconds);
            while (DateTime.Now < end && !ct.IsCancellationRequested)
            {
                string? h = FindSsh(configured, gateway, 1500);
                if (h != null)
                {
                    return h;
                }
                Thread.Sleep(3000);
            }
            return null;
        }
    }
}
