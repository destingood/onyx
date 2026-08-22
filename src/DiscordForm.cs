using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Panneau Discord : les réglages de Discord qui coûtent des ressources en jeu.
    /// Honnête par conception — on n'agit QUE sur ce que Windows et le fichier de
    /// configuration de Discord exposent réellement (démarrage auto, zone de notification,
    /// démarrage réduit), tout est sauvegardé et réversible. Les réglages qui ne vivent
    /// que dans l'interface de Discord (accélération matérielle, overlay en jeu, QoS) sont
    /// annoncés comme tels et pointés — jamais simulés.
    /// </summary>
    internal class DiscordForm : Form
    {
        private readonly Action<string, int> _log;
        private readonly List<Font> _ownedFonts = new List<Font>();
        private CheckBox _cStartup, _cTray, _cMinimized;
        private Label _state;
        private Button _btnApply, _btnOpen, _btnClose;
        private string _settingsPath;

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "Discord";

        private Font Own(Font f) { _ownedFonts.Add(f); return f; }

        /// <summary>Sauvegarde de la ligne de commande de démarrage retirée (pour rétablir).</summary>
        private static string BackupPath
        {
            get { return AppPaths.File("bt-discord-backup.txt"); }
        }

        public DiscordForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Load += (s, e) => Reload();
        }

        /// <summary>settings.json de Discord (stable/PTB/Canary — la première variante installée).</summary>
        private static string FindSettings()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            foreach (string flavour in new[] { "discord", "discordptb", "discordcanary" })
            {
                string p = Path.Combine(appData, flavour, "settings.json");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private void Build()
        {
            Text = "ONYX — Discord";
            ClientSize = new Size(620, 470);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 430);
            BackColor = Color.FromArgb(245, 246, 248);
            Font = Own(new Font("Segoe UI", 9f));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Discord — ce qui pèse pendant tes parties", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = Own(new Font("Segoe UI Semibold", 13f)), TextAlign = ContentAlignment.MiddleLeft
            });

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 8) };

            _state = new Label
            {
                Dock = DockStyle.Top, Height = 44, Font = Own(new Font("Segoe UI Semibold", 9.75f)),
                ForeColor = Color.FromArgb(50, 70, 130), Text = "Détection de Discord…"
            };

            var opts = new Panel { Dock = DockStyle.Top, Height = 132, Padding = new Padding(0, 8, 0, 0) };
            _cStartup = MakeCheck("Démarrer Discord avec Windows",
                "Décoché : Discord ne se lance plus au démarrage (tu l'ouvres quand tu en as besoin).\r\n"
                + "C'est le réglage qui libère le plus de mémoire au boot. La commande de démarrage est sauvegardée.", 0);
            _cTray = MakeCheck("Réduire dans la zone de notification au lieu de quitter",
                "Coché : la croix réduit Discord au lieu de le fermer (comportement habituel).", 34);
            _cMinimized = MakeCheck("Démarrer réduit",
                "Coché : au démarrage, Discord n'ouvre pas sa fenêtre (moins de travail d'affichage).", 68);
            opts.Controls.Add(_cStartup); opts.Controls.Add(_cTray); opts.Controls.Add(_cMinimized);

            var note = new Label
            {
                Dock = DockStyle.Top, Height = 150, ForeColor = Color.FromArgb(70, 74, 82),
                Text = "À régler dans Discord même (honnêteté : ces options ne sont pas exposées à "
                     + "l'extérieur de l'application, ONYX ne fera donc pas semblant de les changer) :\r\n\r\n"
                     + "•  Accélération matérielle — Paramètres ⚙ → Avancés. La couper rend le GPU à ton jeu ;\r\n"
                     + "    c'est LE réglage Discord qui compte le plus si tu joues en 1440p/4K.\r\n"
                     + "•  Overlay en jeu — Paramètres ⚙ → Overlay de jeu. Il s'injecte dans le jeu :\r\n"
                     + "    à couper si tu cherches des FPS ou si un anticheat fait des siennes.\r\n"
                     + "•  Qualité de service (QoS) — Paramètres ⚙ → Voix et vidéo. À couper si ton ping\r\n"
                     + "    monte en vocal : beaucoup de box gèrent mal ce marquage de paquets."
            };

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(16, 8, 16, 10) };
            _btnClose = new Button { Text = "Fermer", Width = 100, Dock = DockStyle.Right };
            _btnClose.Click += (s, e) => Close();
            _btnOpen = new Button { Text = "Ouvrir Discord", Width = 140, Dock = DockStyle.Right };
            _btnOpen.Click += (s, e) => OpenDiscord();
            _btnApply = new Button
            {
                Text = "Appliquer", Width = 130, Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat, BackColor = Theme.AccentColor, ForeColor = Color.FromArgb(16, 13, 9),
                Font = Own(new Font("Segoe UI Semibold", 9.5f))
            };
            _btnApply.FlatAppearance.BorderSize = 0;
            _btnApply.Click += (s, e) => Apply();
            bar.Controls.Add(_btnClose); bar.Controls.Add(new Panel { Width = 8, Dock = DockStyle.Right });
            bar.Controls.Add(_btnOpen); bar.Controls.Add(new Panel { Width = 8, Dock = DockStyle.Right });
            bar.Controls.Add(_btnApply);

            body.Controls.Add(note); body.Controls.Add(opts); body.Controls.Add(_state);
            Controls.Add(body); Controls.Add(bar); Controls.Add(banner);
            Theme.Apply(this);
        }

        private CheckBox MakeCheck(string text, string tip, int y)
        {
            var c = new CheckBox { Text = text, Location = new Point(2, y), Size = new Size(560, 30), AutoSize = false };
            var t = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };
            t.SetToolTip(c, tip);
            return c;
        }

        private void Reload()
        {
            _settingsPath = FindSettings();
            bool installed = _settingsPath != null;
            bool running = false;
            long ramMb = 0;
            try
            {
                foreach (Process p in Process.GetProcesses())
                    using (p)
                        if (p.ProcessName.StartsWith("Discord", StringComparison.OrdinalIgnoreCase))
                        { running = true; try { ramMb += p.WorkingSet64 / (1024 * 1024); } catch { } }
            }
            catch { }

            if (!installed)
            {
                _state.Text = "Discord ne semble pas installé sur ce PC (aucun fichier de configuration trouvé).";
                _cStartup.Enabled = _cTray.Enabled = _cMinimized.Enabled = _btnApply.Enabled = false;
                return;
            }

            _state.Text = "Discord détecté." + (running
                ? "  Il tourne actuellement et occupe environ " + ramMb + " Mo de mémoire."
                : "  Il n'est pas lancé.")
                + (running ? "\r\nLes changements de configuration prendront effet au prochain démarrage de Discord." : "");

            JsonNode root = ReadSettings();
            _cTray.Checked = GetBool(root, "MINIMIZE_TO_TRAY", true);
            _cMinimized.Checked = GetBool(root, "START_MINIMIZED", false);
            // Le démarrage auto est vrai si Discord l'a inscrit dans Windows OU l'a noté dans sa config.
            _cStartup.Checked = RunEntryExists() || GetBool(root, "OPEN_ON_STARTUP", false);
        }

        private static bool RunEntryExists()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunValue) != null;
            }
            catch { return false; }
        }

        private JsonNode ReadSettings()
        {
            try
            {
                if (_settingsPath == null || !File.Exists(_settingsPath)) return null;
                return JsonNode.Parse(File.ReadAllText(_settingsPath));
            }
            catch { return null; }   // fichier illisible/corrompu : on n'y touchera pas
        }

        private static bool GetBool(JsonNode root, string key, bool fallback)
        {
            try
            {
                JsonNode v = root != null ? root[key] : null;
                return v != null ? v.GetValue<bool>() : fallback;
            }
            catch { return fallback; }
        }

        private void Apply()
        {
            int done = 0;
            var problems = new List<string>();

            // 1) Configuration de Discord (lecture/modification/écriture en préservant les autres clés).
            JsonNode root = ReadSettings();
            if (root == null)
            {
                problems.Add("configuration de Discord illisible — elle n'a PAS été modifiée");
            }
            else
            {
                try
                {
                    File.Copy(_settingsPath, _settingsPath + ".bak", true);   // sauvegarde avant écriture
                    root["MINIMIZE_TO_TRAY"] = _cTray.Checked;
                    root["START_MINIMIZED"] = _cMinimized.Checked;
                    root["OPEN_ON_STARTUP"] = _cStartup.Checked;
                    File.WriteAllText(_settingsPath, root.ToJsonString(
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                    done++;
                }
                catch (Exception ex) { problems.Add("écriture de la configuration : " + ex.Message); }
            }

            // 2) Démarrage avec Windows (effet immédiat, contrairement à la config lue au lancement).
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k != null)
                    {
                        if (_cStartup.Checked)
                        {
                            if (k.GetValue(RunValue) == null && File.Exists(BackupPath))
                                k.SetValue(RunValue, File.ReadAllText(BackupPath).Trim());   // rétablissement
                        }
                        else
                        {
                            object cur = k.GetValue(RunValue);
                            if (cur != null)
                            {
                                try { File.WriteAllText(BackupPath, cur.ToString()); } catch { }
                                k.DeleteValue(RunValue, false);
                            }
                        }
                        done++;
                    }
                }
            }
            catch (Exception ex) { problems.Add("démarrage automatique : " + ex.Message); }

            if (_log != null)
                _log("Discord : " + done + " réglage(s) appliqué(s)"
                     + (problems.Count > 0 ? " — " + string.Join(" ; ", problems) : ""), problems.Count > 0 ? 2 : 1);

            MessageBox.Show(this,
                problems.Count == 0
                    ? "Réglages appliqués.\r\n\r\nRedémarre Discord pour que sa configuration soit relue.\r\n"
                      + "Tout est réversible : reviens ici et décoche."
                    : "Réglages partiellement appliqués :\r\n\r\n• " + string.Join("\r\n• ", problems),
                "ONYX — Discord", MessageBoxButtons.OK,
                problems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            Reload();
        }

        private void OpenDiscord()
        {
            try
            {
                string upd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                          "Discord", "Update.exe");
                if (File.Exists(upd))
                    Process.Start(new ProcessStartInfo(upd, "--processStart Discord.exe") { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo("https://discord.com/app") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Impossible d'ouvrir Discord : " + ex.Message,
                    "ONYX — Discord", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            foreach (Font f in _ownedFonts) { try { f.Dispose(); } catch { } }
            _ownedFonts.Clear();
            base.OnFormClosed(e);
        }
    }
}
