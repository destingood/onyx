using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// « QUOI DE NEUF » : au premier lancement d'une NOUVELLE version, montre les nouveautés —
    /// lues dans le CHANGELOG embarqué dans l'exe (zéro maintenance : le journal existe déjà).
    /// Une seule fois par version (bt-lastver.txt), jamais à la première installation (pas de
    /// leçon d'histoire à un nouveau venu), jamais pendant les tests UI.
    /// </summary>
    internal static class WhatsNew
    {
        private static string StampPath
        {
            get { return AppPaths.File("bt-lastver.txt"); }
        }

        private static string CurrentVersion()
        {
            try { var v = typeof(WhatsNew).Assembly.GetName().Version; return v.Major + "." + v.Minor.ToString("00"); }
            catch { return ""; }
        }

        /// <summary>Le CHANGELOG embarqué, ou null.</summary>
        private static string ReadChangelog()
        {
            try
            {
                using (var s = typeof(WhatsNew).Assembly.GetManifestResourceStream("CHANGELOG.md"))
                {
                    if (s == null) return null;
                    using (var r = new StreamReader(s, System.Text.Encoding.UTF8)) return r.ReadToEnd();
                }
            }
            catch { return null; }
        }

        /// <summary>Les N premières entrées « ## vX — … » du texte. Pur et testable.</summary>
        public static string TopSections(string changelog, int count)
        {
            if (string.IsNullOrEmpty(changelog)) return "";
            var sb = new System.Text.StringBuilder();
            int seen = 0;
            foreach (var raw in changelog.Replace("\r", "").Split('\n'))
            {
                if (raw.StartsWith("## "))
                {
                    seen++;
                    if (seen > count) break;
                    if (seen > 1) sb.Append('\n');
                    sb.Append(raw.Substring(3).Trim()).Append('\n');
                    continue;
                }
                if (seen > 0) sb.Append(raw).Append('\n');
            }
            return sb.ToString().Trim();
        }

        /// <summary>À appeler au démarrage : montre la fenêtre si la version a changé. Silencieux sinon.</summary>
        public static void ShowIfUpdated(IWin32Window owner)
        {
            try
            {
                if (Environment.GetEnvironmentVariable("BT_UISHOT") != null
                    || Environment.GetEnvironmentVariable("BT_UITEST") == "1") return;
                string cur = CurrentVersion();
                if (cur.Length == 0) return;
                string old = null;
                try { if (File.Exists(StampPath)) old = File.ReadAllText(StampPath).Trim(); } catch { }
                try { File.WriteAllText(StampPath, cur); } catch { }
                if (string.IsNullOrEmpty(old) || old == cur) return;   // 1re installation, ou déjà vue
                string text = TopSections(ReadChangelog(), 2);
                if (text.Length == 0) return;
                ShowDialogBox(owner, cur, old, text);
            }
            catch { }
        }

        private static void ShowDialogBox(IWin32Window owner, string cur, string old, string text)
        {
            using (var f = new Form
            {
                Text = "ONYX — quoi de neuf (v" + old + " → v" + cur + ")",
                Width = 720, Height = 520, StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                BackColor = FpsUi.BgMain, ForeColor = FpsUi.Dim
            })
            {
                var box = new TextBox
                {
                    Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                    Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
                    BackColor = FpsUi.BgMain, ForeColor = FpsUi.Dim, Font = FpsUi.Small,
                    Text = text.Replace("\n", Environment.NewLine)
                };
                var ok = new Button
                {
                    Text = "C'est noté !", Dock = DockStyle.Bottom, Height = 40,
                    FlatStyle = FlatStyle.Flat, ForeColor = FpsUi.Gold, DialogResult = DialogResult.OK
                };
                var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 8), BackColor = FpsUi.BgMain };
                pad.Controls.Add(box);
                f.Controls.Add(pad); f.Controls.Add(ok);
                f.AcceptButton = ok;
                try { box.SelectionStart = 0; box.SelectionLength = 0; } catch { }
                f.ShowDialog(owner);
            }
        }
    }
}
