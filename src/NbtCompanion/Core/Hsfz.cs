using System;
using System.IO;
using System.Net.Sockets;

namespace NbtCompanion.Core
{
    /// <summary>What the head unit answers about its energy mode and state. Null fields: not read.</summary>
    public sealed class HuModeReading
    {
        public byte? EnergyMode { get; set; }
        public byte? State { get; set; }
        public string EnergyText { get; set; } = "";
        public string StateText { get; set; } = "";

        /// <summary>True only when the head unit answered EnergyMode 00 (normal).</summary>
        public bool EnergyNormal
        {
            get { return EnergyMode == 0x00; }
        }

        /// <summary>True when the head unit answered an EnergyMode other than 00 (03 = Flash: black screen).</summary>
        public bool EnergyNotNormal
        {
            get { return EnergyMode.HasValue && EnergyMode.Value != 0x00; }
        }

        public string Summary()
        {
            return "EnergyMode (22 100A): " + EnergyText + " | stato (22 F186): " + StateText;
        }
    }

    /// <summary>
    /// READ-ONLY diagnostic reads of the head unit over BMW HSFZ (TCP 6801), the same framing Xayanix's script uses:
    /// u32 payload length | u16 control (0x0001) | tester 0xF4 | head unit 0x63 | UDS. Only ReadDataByIdentifier (22)
    /// is ever sent: no session change, no routine, no write. 22 100A = EnergyMode (00 normal, 03 Flash),
    /// 22 F186 = state (01 application, 02 programming, 03 flash transition).
    /// </summary>
    public static class Hsfz
    {
        public const int Port = 6801;
        public const byte Tester = 0xF4;
        public const byte HeadUnitAddress = 0x63;

        public static readonly byte[] ReadEnergyMode = { 0x22, 0x10, 0x0A };
        public static readonly byte[] ReadState = { 0x22, 0xF1, 0x86 };

        public static HuModeReading ReadModes(string host, int timeoutMs = 3000)
        {
            var r = new HuModeReading();
            using var client = new TcpClient();
            try
            {
                if (!client.ConnectAsync(host, Port).Wait(timeoutMs))
                {
                    r.EnergyText = r.StateText = L.T("nessuna connessione a ", "no connection to ") + host + ":" + Port;
                    return r;
                }
            }
            catch (Exception ex)
            {
                r.EnergyText = r.StateText = L.T("nessuna connessione a ", "no connection to ") + host + ":" + Port + " (" + Inner(ex).Message + ")";
                return r;
            }
            client.NoDelay = true;
            NetworkStream s = client.GetStream();
            s.ReadTimeout = timeoutMs;
            s.WriteTimeout = timeoutMs;
            r.EnergyMode = ReadByte(s, ReadEnergyMode, out string et);
            r.EnergyText = et;
            r.State = ReadByte(s, ReadState, out string st);
            r.StateText = st;
            return r;
        }

        private static Exception Inner(Exception ex)
        {
            return ex is AggregateException a && a.InnerException != null ? a.InnerException : ex;
        }

        /// <summary>Sends one 22 xx xx and returns the first data byte of the positive answer, or null with the reason.</summary>
        private static byte? ReadByte(NetworkStream s, byte[] request, out string text)
        {
            if (request.Length != 3 || request[0] != 0x22)
            {
                throw new ArgumentException("only ReadDataByIdentifier is allowed");
            }
            try
            {
                s.Write(Frame(request));
                DateTime end = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < end)
                {
                    byte[] uds = ReceiveUds(s, out ushort control);
                    if (control == 0x0002 || uds.Length == 0 || uds[0] == 0x7E)
                    {
                        continue; // ack of our own request, or a tester-present answer
                    }
                    string hex = BitConverter.ToString(uds).Replace("-", " ");
                    if (uds.Length >= 3 && uds[0] == 0x7F && uds[2] == 0x78)
                    {
                        continue; // response pending
                    }
                    if (uds.Length >= 4 && uds[0] == 0x62 && uds[1] == request[1] && uds[2] == request[2])
                    {
                        text = uds[3].ToString("X2") + " (" + hex + ")";
                        return uds[3];
                    }
                    if (uds.Length >= 3 && uds[0] == 0x7F && uds[1] == 0x22)
                    {
                        text = L.T("rifiutato, NRC ", "refused, NRC ") + uds[2].ToString("X2") + " (" + hex + ")";
                        return null;
                    }
                    // an answer to something else: keep waiting for ours
                }
                text = L.T("nessuna risposta", "no answer");
                return null;
            }
            catch (Exception ex)
            {
                text = L.T("errore: ", "error: ") + Inner(ex).Message;
                return null;
            }
        }

        public static byte[] Frame(byte[] uds)
        {
            int len = uds.Length + 2;
            var f = new byte[6 + len];
            f[0] = (byte)(len >> 24);
            f[1] = (byte)(len >> 16);
            f[2] = (byte)(len >> 8);
            f[3] = (byte)len;
            f[4] = 0x00;
            f[5] = 0x01;
            f[6] = Tester;
            f[7] = HeadUnitAddress;
            Array.Copy(uds, 0, f, 8, uds.Length);
            return f;
        }

        private static byte[] ReceiveUds(NetworkStream s, out ushort control)
        {
            byte[] h = ReadExactly(s, 6);
            int len = (h[0] << 24) | (h[1] << 16) | (h[2] << 8) | h[3];
            control = (ushort)((h[4] << 8) | h[5]);
            if (len < 0 || len > 1 << 20)
            {
                throw new IOException("HSFZ frame length " + len);
            }
            byte[] p = ReadExactly(s, len);
            if (p.Length < 2)
            {
                return Array.Empty<byte>();
            }
            return p.AsSpan(2).ToArray();
        }

        private static byte[] ReadExactly(NetworkStream s, int n)
        {
            var b = new byte[n];
            int got = 0;
            while (got < n)
            {
                int k = s.Read(b, got, n - got);
                if (k <= 0)
                {
                    throw new IOException("HSFZ closed");
                }
                got += k;
            }
            return b;
        }
    }
}
