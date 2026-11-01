using System;
using System.Windows.Forms;

namespace WebBlocker.Agent
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                var databasePath = AgentPaths.ResolveDatabasePath(args);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new AgentForm(databasePath));
            }
            catch (ArgumentException exception)
            {
                MessageBox.Show(exception.Message, "NetWard 网界卫士 - 被控端",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 2;
            }
            catch (Exception exception)
            {
                MessageBox.Show("NetWard Agent 无法启动：\r\n" + exception,
                    "NetWard 网界卫士 - 被控端", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
            }
        }
    }
}