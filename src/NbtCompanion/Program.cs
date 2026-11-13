using System;
using System.Threading;
using System.Windows.Forms;
using NbtCompanion.Core;
using NbtCompanion.UI;

namespace NbtCompanion
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            using var single = new Mutex(true, "NBT-Companion-DeathWalkeR", out bool first);
            if (!first)
            {
                MessageBox.Show("NBT Companion " + L.T("e' gia' aperto.", "is already open."), "NBT Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                Log.Write("ERRORE imprevisto (interfaccia): " + e.Exception);
                MessageBox.Show(e.Exception.Message, "NBT Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("ERRORE imprevisto: " + e.ExceptionObject);
            Application.Run(new MainForm());
        }
    }
}
