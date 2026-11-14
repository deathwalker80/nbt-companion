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
        private static void Main(string[] args)
        {
            using var single = new Mutex(true, "NBT-Companion-DeathWalkeR", out bool first);
            if (!first && Array.IndexOf(args, SelfUpdate.UpdatedArg) >= 0)
            {
                // started by the self-update: the previous instance is closing, wait for it
                try
                {
                    first = single.WaitOne(TimeSpan.FromSeconds(30));
                }
                catch (AbandonedMutexException)
                {
                    first = true;
                }
            }
            if (!first)
            {
                MessageBox.Show("NBT Companion " + L.T("e' gia' aperto.", "is already open."), "NBT Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SelfUpdate.Cleanup();
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
