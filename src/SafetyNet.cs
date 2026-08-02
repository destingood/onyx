using System;
using System.IO;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// FILET DE SÉCURITÉ GLOBAL : une erreur imprévue ne doit JAMAIS faire disparaître l'application
    /// sans explication. Tout ce qui échappe au code est écrit dans bt-erreurs.txt (à côté de l'exe)
    /// et présenté honnêtement à l'utilisateur : ce qui s'est passé, où c'est noté, et le fait que
    /// son PC n'a rien subi. Quand l'incident est rattrapable (thread d'interface), l'app CONTINUE.
    ///
    /// C'était le dernier point faible signalé par la revue de code indépendante : le Copilote
    /// appelait le routage sans aucun filet — un bug dans un outil aurait fermé toute l'application.
    /// </summary>
    internal static class SafetyNet
    {
        private static readonly object Gate = new object();
        private static bool _installed;

        private static string LogPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-erreurs.txt"); }
        }

        /// <summary>Écrit l'incident dans bt-erreurs.txt (le fichier reste petit : 200 derniers Ko).</summary>
        public static void Record(string origin, Exception ex)
        {
            if (ex == null) return;
            try
            {
                lock (Gate)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("=== ").Append(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")).Append("  [").Append(origin).Append("]\r\n");
                    try { sb.Append("ONYX v").Append(typeof(SafetyNet).Assembly.GetName().Version).Append("\r\n"); } catch { }
                    sb.Append(ex.GetType().Name).Append(" : ").Append(ex.Message).Append("\r\n");
                    if (!string.IsNullOrEmpty(ex.StackTrace)) sb.Append(ex.StackTrace).Append("\r\n");
                    if (ex.InnerException != null)
                        sb.Append("  → cause : ").Append(ex.InnerException.GetType().Name).Append(" : ").Append(ex.InnerException.Message).Append("\r\n");
                    sb.Append("\r\n");
                    File.AppendAllText(LogPath, sb.ToString(), new System.Text.UTF8Encoding(false));

                    var fi = new FileInfo(LogPath);
                    if (fi.Exists && fi.Length > 200 * 1024)
                    {
                        string all = File.ReadAllText(LogPath);
                        File.WriteAllText(LogPath, all.Substring(all.Length / 2), new System.Text.UTF8Encoding(false));
                    }
                }
            }
            catch { }
        }

        /// <summary>Message honnête montré à l'utilisateur (aucune formule creuse). PUR → testable.</summary>
        public static string UserMessage(Exception ex, bool fatal)
        {
            string what = ex == null ? "erreur inconnue" : ex.GetType().Name + " : " + ex.Message;
            var sb = new System.Text.StringBuilder();
            sb.Append(fatal ? "ONYX a rencontré une erreur qu'il n'a pas pu rattraper.\n\n"
                            : "ONYX a rencontré une erreur, mais il continue de fonctionner.\n\n");
            sb.Append("Détail technique : ").Append(what).Append("\n\n");
            sb.Append("• Ton PC n'a subi AUCUNE modification à cause de cette erreur.\n");
            sb.Append("• Le détail complet est noté dans « bt-erreurs.txt », à côté de l'application.\n");
            sb.Append(fatal ? "• Relance ONYX : si l'erreur revient au même endroit, envoie-moi ce fichier.\n"
                            : "• Si la même erreur revient, le fichier permettra de la corriger.\n");
            return sb.ToString();
        }

        /// <summary>Installe le filet sur les deux sources d'erreurs non gérées de WinForms.</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e)
                {
                    Record("interface", e.Exception);
                    try
                    {
                        MessageBox.Show(UserMessage(e.Exception, false), "ONYX — incident rattrapé",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    catch { }
                };
            }
            catch { }
            try
            {
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
                {
                    var ex = e.ExceptionObject as Exception;
                    Record("fond", ex);
                    try
                    {
                        MessageBox.Show(UserMessage(ex, true), "ONYX — erreur inattendue",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch { }
                };
            }
            catch { }
        }
    }
}
