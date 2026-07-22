using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// 📡 Optimisation de la carte réseau : désactive les réglages du pilote qui ajoutent de
    /// la latence (modération d'interruptions, contrôle de flux, Ethernet écoénergétique).
    /// Ne touche QU'AUX réglages réellement exposés par ta carte, valeurs d'origine sauvegardées,
    /// entièrement réversible. L'effet s'applique au redémarrage de la carte.
    /// </summary>
    internal class NetAdapterForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private Label _summary;
        private Button _btnOptimize, _btnRevert, _btnScan, _btnClose;
        private List<Adapter> _adapters = new List<Adapter>();

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private const string ClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        // Réglages gérés : clé pilote (standardisée) -> (libellé, valeur « latence min »).
        private static readonly string[][] Managed =
        {
            new[] { "*InterruptModeration", "Modération d'interruptions", "0" },
            new[] { "*FlowControl",         "Contrôle de flux",           "0" },
            new[] { "*EEE",                 "Ethernet écoénergétique (EEE)", "0" },
            new[] { "EnableGreenEthernet",  "Green Ethernet (Realtek)",   "0" },
        };

        private class Prop { public string Key, Label, Optimal, Current; }
        private class Adapter
        {
            public string SubKey, Name, Connection; public bool Connected;
            public List<Prop> Props = new List<Prop>();
            public bool AllOptimal { get { return Props.All(p => p.Current == p.Optimal); } }
        }

        public NetAdapterForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Carte réseau (latence)";
            ClientSize = new Size(660, 470);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  📡 Carte réseau — moins de latence en jeu",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Certains réglages du pilote réseau économisent l'énergie au prix de la latence. Les désactiver "
                     + "réduit le ping/gigue (surtout en Ethernet), au prix d'un peu plus de CPU/consommation. "
                     + "Seuls les réglages exposés par ta carte sont modifiés ; valeurs d'origine sauvegardées.",
                Location = new Point(18, 58), Size = new Size(624, 46), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _list = new ListView
            {
                Location = new Point(18, 110), Size = new Size(624, 248),
                View = View.Details, FullRowSelect = true, GridLines = false, MultiSelect = false
            };
            _list.Columns.Add("Carte réseau", 250);
            _list.Columns.Add("Réglage", 200);
            _list.Columns.Add("État", 174);
            Controls.Add(_list);

            _summary = new Label
            {
                Location = new Point(18, 364), Size = new Size(624, 22),
                Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_summary);

            _btnScan = MakeBtn("Ré-analyser", 18, 394, 120, 38, false);
            _btnScan.Click += (s, e) => Scan();
            _btnOptimize = MakeBtn("Optimiser pour la latence", 148, 394, 230, 38, true);
            _btnOptimize.Click += (s, e) => Apply(true);
            _btnRevert = MakeBtn("Rétablir les valeurs d'origine", 388, 394, 170, 38, false);
            _btnRevert.Click += (s, e) => Apply(false);
            _btnClose = MakeBtn("Fermer", 568, 394, 74, 38, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnScan); Controls.Add(_btnOptimize); Controls.Add(_btnRevert); Controls.Add(_btnClose);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 9.5f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnScan.Enabled = !busy; _btnOptimize.Enabled = !busy; _btnRevert.Enabled = !busy; _list.Enabled = !busy;
        }

        private static string BackupPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-nic-backup.txt");
        }

        private void Scan()
        {
            SetBusy(true);
            _summary.Text = "Analyse des cartes réseau...";
            Task.Run(() =>
            {
                try
                {
                    var adapters = ReadAdapters();
                    UiSafe.Post(this, () => Populate(adapters));
                }
                catch { UiSafe.Post(this, () => SetBusy(false)); }
            });
        }

        private List<Adapter> ReadAdapters()
        {
            var result = new List<Adapter>();
            // Association GUID d'interface -> (nom de connexion, connecté ?).
            var byGuid = new Dictionary<string, NetworkInterface>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                    if (!byGuid.ContainsKey(ni.Id)) byGuid[ni.Id] = ni;
            }
            catch { }

            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(ClassKey))
                {
                    if (cls == null) return result;
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        if (sub.Length != 4) continue;   // NNNN uniquement
                        using (RegistryKey k = cls.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            string desc = Convert.ToString(k.GetValue("DriverDesc"));
                            if (string.IsNullOrEmpty(desc)) continue;

                            var props = new List<Prop>();
                            foreach (string[] m in Managed)
                            {
                                object v = k.GetValue(m[0]);
                                if (v == null) continue;   // réglage non exposé par cette carte
                                props.Add(new Prop { Key = m[0], Label = m[1], Optimal = m[2], Current = Convert.ToString(v) });
                            }
                            if (props.Count == 0) continue;

                            string netCfg = Convert.ToString(k.GetValue("NetCfgInstanceId"));
                            NetworkInterface ni; bool connected = false; string conn = "";
                            if (netCfg != null && byGuid.TryGetValue(netCfg, out ni))
                            {
                                conn = ni.Name;
                                connected = ni.OperationalStatus == OperationalStatus.Up;
                            }
                            result.Add(new Adapter { SubKey = sub, Name = desc, Connection = conn, Connected = connected, Props = props });
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        private void Populate(List<Adapter> adapters)
        {
            _adapters = adapters;
            _list.Items.Clear();
            int optimizable = 0;
            foreach (Adapter a in adapters)
            {
                bool first = true;
                foreach (Prop p in a.Props)
                {
                    bool opt = p.Current == p.Optimal;
                    if (!opt) optimizable++;
                    var it = new ListViewItem(first ? a.Name + (a.Connected ? "  (connectée)" : "") : "");
                    it.SubItems.Add(p.Label);
                    it.SubItems.Add(opt ? "✔ latence min" : "à optimiser (=" + p.Current + ")");
                    if (!opt) it.ForeColor = Color.FromArgb(200, 110, 0);
                    _list.Items.Add(it);
                    first = false;
                }
            }
            if (adapters.Count == 0)
                _summary.Text = "Aucun réglage de latence exposé par tes cartes réseau (fréquent en Wi-Fi). Rien à faire ici.";
            else
                _summary.Text = optimizable == 0
                    ? "✔ Tes cartes sont déjà réglées pour la latence minimale."
                    : optimizable + " réglage(s) à optimiser. « Optimiser » puis redémarrage de la carte pour appliquer.";
            _btnOptimize.Enabled = optimizable > 0;
            SetBusy(false);
        }

        // Sauvegarde/restauration des valeurs d'origine (fichier local).
        private static Dictionary<string, string> LoadBackup()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(BackupPath()))
                    foreach (string line in File.ReadAllLines(BackupPath()))
                    {
                        string[] p = line.Split('\t');
                        if (p.Length >= 3) d[p[0] + "|" + p[1]] = p[2];
                    }
            }
            catch { }
            return d;
        }

        private static void SaveBackup(Dictionary<string, string> d)
        {
            try
            {
                var lines = d.Select(kv => kv.Key.Replace("|", "\t") + "\t" + kv.Value).ToArray();
                File.WriteAllLines(BackupPath(), lines);
            }
            catch { }
        }

        private void Apply(bool optimize)
        {
            var toRestart = new List<Adapter>();
            SetBusy(true);
            Task.Run(() =>
            {
                Dictionary<string, string> backup = LoadBackup();
                int changed = 0;
                foreach (Adapter a in _adapters)
                {
                    bool touched = false;
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(ClassKey + "\\" + a.SubKey, true))
                    {
                        if (k == null) continue;
                        foreach (Prop p in a.Props)
                        {
                            string id = a.SubKey + "|" + p.Key;
                            try
                            {
                                if (optimize)
                                {
                                    if (p.Current == p.Optimal) continue;
                                    if (!backup.ContainsKey(id)) backup[id] = p.Current;   // valeur d'origine
                                    k.SetValue(p.Key, p.Optimal, RegistryValueKind.String);
                                    changed++; touched = true;
                                }
                                else
                                {
                                    string orig;
                                    if (!backup.TryGetValue(id, out orig)) continue;
                                    k.SetValue(p.Key, orig, RegistryValueKind.String);
                                    backup.Remove(id);
                                    changed++; touched = true;
                                }
                            }
                            catch (Exception ex) { if (_log != null) _log(a.Name + " (" + p.Key + ") : " + ex.Message, 2); }
                        }
                    }
                    if (touched) toRestart.Add(a);
                }
                SaveBackup(backup);
                int cc = changed;
                UiSafe.Post(this, () => AfterApply(optimize, cc, toRestart));
            });
        }

        private void AfterApply(bool optimize, int changed, List<Adapter> toRestart)
        {
            if (_log != null) _log("Carte réseau : " + changed + " réglage(s) " + (optimize ? "optimisé(s)" : "rétabli(s)") + ".", 1);
            if (changed == 0) { SetBusy(false); MessageBox.Show(this, "Aucun changement.", "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            var connected = toRestart.Where(a => a.Connected && !string.IsNullOrEmpty(a.Connection)).ToList();
            bool restart = false;
            if (connected.Count > 0)
                restart = MessageBox.Show(this,
                    "Réglages écrits. Pour les appliquer maintenant, la (les) carte(s) connectée(s) doivent redémarrer "
                    + "→ BRÈVE coupure de connexion (quelques secondes).\n\nRedémarrer la carte maintenant ?\n"
                    + "(Non = effet au prochain redémarrage du PC.)",
                    "Appliquer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

            if (restart)
            {
                Task.Run(() =>
                {
                    foreach (Adapter a in connected)
                    {
                        try
                        {
                            Sys.Run(Sys.Sys32("netsh.exe"), "interface set interface name=\"" + a.Connection + "\" admin=disabled");
                            System.Threading.Thread.Sleep(1200);
                            Sys.Run(Sys.Sys32("netsh.exe"), "interface set interface name=\"" + a.Connection + "\" admin=enabled");
                            if (_log != null) _log("Carte « " + a.Connection + " » redémarrée.", 1);
                        }
                        catch (Exception ex) { if (_log != null) _log("Redémarrage carte : " + ex.Message, 2); }
                    }
                    System.Threading.Thread.Sleep(1500);
                    UiSafe.Post(this, Scan);
                });
            }
            else
            {
                SetBusy(false);
                Scan();
                MessageBox.Show(this, "Réglages enregistrés. Effet au prochain redémarrage du PC (ou de la carte).",
                    "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
