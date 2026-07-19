using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// 🧹 Réglages néfastes d'autres optimiseurs : détecte les tweaks DANGEREUX laissés par
    /// de mauvais guides / outils « boost FPS » (timer HPET forcé, récupération GPU désactivée,
    /// Defender coupé, fichier d'échange désactivé, TRIM SSD off…) et les remet aux valeurs
    /// saines de Windows. Tout est vérifié en local et réversible.
    /// </summary>
    internal static class Checkup
    {
        internal class Item
        {
            public string Name;
            public string Status;
            public bool Problem;
            public bool NeedReboot;
            public bool Security;
            public Action<Action<string, int>> Fix;
        }

        private const string MemMgr = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
        private const string GfxDrv = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        private const string DefPol = @"SOFTWARE\Policies\Microsoft\Windows Defender";

        public static List<Item> Analyze()
        {
            return new List<Item>
            {
                UsePlatformClock(),
                TdrDisabled(),
                DefenderDisabled(),
                PagefileDisabled(),
                TrimDisabled(),
                ScheduledDefragOff(),
                ClearPageFile(),
                LargeSystemCache(),
            };
        }

        private const string DefragTask = @"\Microsoft\Windows\Defrag\ScheduledDefrag";

        // Maintenance disque planifiée coupée (SSD non re-TRIMé, HDD non défragmenté) : classique des « debloat ».
        private static Item ScheduledDefragOff()
        {
            bool off = Sys.ScheduledTaskDisabled(DefragTask) == true;
            return new Item
            {
                Name = "Optimisation planifiée des lecteurs désactivée",
                Problem = off,
                Status = off ? "DÉSACTIVÉE — SSD plus re-TRIMé, HDD plus défragmenté (perfs disque qui se dégradent)"
                             : "active (bon)",
                Fix = delegate(Action<string, int> log)
                {
                    Sys.SetScheduledTask(DefragTask, true);
                    log("Optimisation planifiée des lecteurs réactivée.", 1);
                }
            };
        }

        // 1. bcdedit useplatformclock=Yes : force le HPET → LATENCE en plus (mythe des guides).
        private static Item UsePlatformClock()
        {
            bool on = false;
            try
            {
                NativeResult r = Sys.Run(Sys.Sys32("bcdedit.exe"), "/enum {current}");
                string o = (r.Output ?? "").ToLowerInvariant();
                int idx = o.IndexOf("useplatformclock");
                if (idx >= 0)
                {
                    string rest = o.Substring(idx, Math.Min(40, o.Length - idx));
                    on = rest.Contains("yes") || rest.Contains("true");
                }
            }
            catch { }
            return new Item
            {
                Name = "Timer HPET forcé (bcdedit useplatformclock)",
                Problem = on, NeedReboot = true,
                Status = on ? "ACTIVÉ — force le HPET, ajoute de la latence (à retirer)"
                            : "non forcé (bon)",
                Fix = delegate(Action<string, int> log)
                {
                    Sys.Run(Sys.Sys32("bcdedit.exe"), "/deletevalue useplatformclock");
                    log("Timer HPET forcé retiré (bcdedit). Redémarrage nécessaire.", 2);
                }
            };
        }

        // 2. TdrLevel=0 : DÉSACTIVE la récupération du pilote GPU → un accroc devient un FREEZE/crash.
        private static Item TdrDisabled()
        {
            bool bad = Sys.IntEquals(Sys.GetMachine(GfxDrv, "TdrLevel"), 0);
            return new Item
            {
                Name = "Récupération GPU désactivée (TdrLevel=0)",
                Problem = bad, NeedReboot = true,
                Status = bad ? "DÉSACTIVÉE — un accroc GPU fige tout au lieu de récupérer (dangereux)"
                             : "active (défaut Windows, bon)",
                Fix = delegate(Action<string, int> log)
                {
                    Sys.DelMachine(GfxDrv, "TdrLevel");
                    log("Récupération GPU (TDR) rétablie au défaut Windows. Redémarrage nécessaire.", 2);
                }
            };
        }

        // 3. Defender coupé par politique (scripts « debloat ») : PC sans protection.
        private static Item DefenderDisabled()
        {
            bool bad = Sys.IntEquals(Sys.GetMachine(DefPol, "DisableAntiSpyware"), 1);
            return new Item
            {
                Name = "Windows Defender désactivé par politique",
                Problem = bad, Security = true,
                Status = bad ? "DÉSACTIVÉ par un script — PC sans antivirus (risque)"
                             : "non bloqué par politique (bon)",
                Fix = delegate(Action<string, int> log)
                {
                    Sys.DelMachine(DefPol, "DisableAntiSpyware");
                    log("Blocage de Windows Defender retiré. Rouvre la Sécurité Windows pour le réactiver.", 1);
                }
            };
        }

        // 4. Fichier d'échange désactivé : cause de crashs / « out of memory » en jeu.
        private static Item PagefileDisabled()
        {
            bool auto = false, hasPageFile = false;
            try
            {
                using (var cs = new ManagementObjectSearcher("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem"))
                    foreach (ManagementObject mo in cs.Get())
                        auto = Convert.ToBoolean(mo["AutomaticManagedPagefile"]);
                using (var pf = new ManagementObjectSearcher("SELECT Name FROM Win32_PageFileUsage"))
                    foreach (ManagementObject mo in pf.Get()) { hasPageFile = true; break; }
            }
            catch { }
            bool bad = !auto && !hasPageFile;
            return new Item
            {
                Name = "Fichier d'échange (pagefile) désactivé",
                Problem = bad, NeedReboot = true,
                Status = bad ? "DÉSACTIVÉ — cause de plantages et d'« out of memory » en jeu"
                             : (auto ? "géré automatiquement par Windows (bon)" : "configuré manuellement (ok)"),
                Fix = delegate(Action<string, int> log)
                {
                    try
                    {
                        using (var cs = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
                            foreach (ManagementObject mo in cs.Get())
                            {
                                mo["AutomaticManagedPagefile"] = true;
                                mo.Put();
                            }
                        log("Fichier d'échange remis en gestion automatique Windows. Redémarrage nécessaire.", 2);
                    }
                    catch (Exception ex) { log("Pagefile : " + ex.Message, 3); }
                }
            };
        }

        // 5. TRIM SSD désactivé : use un SSD prématurément, ralentit les écritures.
        private static Item TrimDisabled()
        {
            bool off = false;
            try
            {
                NativeResult r = Sys.Run(Sys.Sys32("fsutil.exe"), "behavior query DisableDeleteNotify");
                foreach (string line in (r.Output ?? "").Split('\n'))
                    if (line.IndexOf("NTFS", StringComparison.OrdinalIgnoreCase) >= 0 && line.Contains("= 1")) off = true;
            }
            catch { }
            return new Item
            {
                Name = "TRIM SSD désactivé (DisableDeleteNotify=1)",
                Problem = off,
                Status = off ? "DÉSACTIVÉ — mauvais pour la durée de vie et la vitesse d'un SSD"
                             : "actif (bon pour les SSD)",
                Fix = delegate(Action<string, int> log)
                {
                    Sys.Run(Sys.Sys32("fsutil.exe"), "behavior set DisableDeleteNotify NTFS 0");
                    Sys.Run(Sys.Sys32("fsutil.exe"), "behavior set DisableDeleteNotify 0");
                    log("TRIM SSD réactivé.", 1);
                }
            };
        }

        // 6. ClearPageFileAtShutdown=1 : arrêt du PC TRÈS lent, aucun gain.
        private static Item ClearPageFile()
        {
            bool bad = Sys.IntEquals(Sys.GetMachine(MemMgr, "ClearPageFileAtShutdown"), 1);
            return new Item
            {
                Name = "Effacer le fichier d'échange à l'arrêt (lent)",
                Problem = bad,
                Status = bad ? "ACTIVÉ — rallonge fortement l'arrêt du PC pour rien"
                             : "désactivé (bon)",
                Fix = delegate(Action<string, int> log)
                {
                    Sys.SetMachine(MemMgr, "ClearPageFileAtShutdown", 0, RegistryValueKind.DWord);
                    log("« Effacer le fichier d'échange à l'arrêt » désactivé (arrêt rapide rétabli).", 1);
                }
            };
        }

        // 7. LargeSystemCache=1 : favorise le cache disque au détriment des jeux.
        private static Item LargeSystemCache()
        {
            bool bad = Sys.IntEquals(Sys.GetMachine(MemMgr, "LargeSystemCache"), 1);
            return new Item
            {
                Name = "Grand cache système (LargeSystemCache=1)",
                Problem = bad,
                Status = bad ? "ACTIVÉ — vole de la RAM aux jeux au profit du cache disque"
                             : "désactivé (bon pour le jeu)",
                Fix = delegate(Action<string, int> log)
                {
                    Sys.SetMachine(MemMgr, "LargeSystemCache", 0, RegistryValueKind.DWord);
                    log("Grand cache système désactivé (RAM rendue aux jeux).", 1);
                }
            };
        }
    }

    /// <summary>Panneau « Réglages néfastes » : détection + retour aux valeurs saines de Windows.</summary>
    internal class CheckupForm : Form
    {
        private readonly Action<string, int> _log;
        private CheckedListBox _list;
        private Label _summary;
        private Button _btnScan, _btnFix, _btnClose;
        private List<Checkup.Item> _items = new List<Checkup.Item>();

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        public CheckupForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Réglages néfastes";
            ClientSize = new Size(680, 460);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  🧹 Réglages néfastes laissés par d'autres « optimiseurs »",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Beaucoup de guides « boost FPS » et d'outils douteux laissent des réglages DANGEREUX "
                     + "(freezes, crashs, arrêt lent, PC sans antivirus). On les repère et on remet les valeurs "
                     + "saines de Windows. Les problèmes détectés sont pré-cochés ⚠.",
                Location = new Point(18, 58), Size = new Size(644, 44), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _list = new CheckedListBox
            {
                Location = new Point(18, 108), Size = new Size(644, 248), CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5f), IntegralHeight = false,
                HorizontalScrollbar = true
            };
            Controls.Add(_list);

            _summary = new Label
            {
                Location = new Point(18, 364), Size = new Size(644, 40),
                Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_summary);

            _btnScan = MakeBtn("Analyser à nouveau", 18, 412, 150, 38, false);
            _btnScan.Click += (s, e) => Scan();
            _btnFix = MakeBtn("CORRIGER LA SÉLECTION", 178, 412, 250, 38, true);
            _btnFix.Click += OnFix;
            _btnClose = MakeBtn("Fermer", 572, 412, 90, 38, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnScan); Controls.Add(_btnFix); Controls.Add(_btnClose);
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
            _btnScan.Enabled = !busy; _btnFix.Enabled = !busy; _list.Enabled = !busy;
        }

        private void Scan()
        {
            SetBusy(true);
            _summary.Text = "Analyse en cours...";
            Task.Run(() =>
            {
                List<Checkup.Item> items = Checkup.Analyze();
                try { BeginInvoke((Action)(() => Populate(items))); } catch { }
            });
        }

        private void Populate(List<Checkup.Item> items)
        {
            _items = items;
            _list.Items.Clear();
            int problems = 0;
            foreach (Checkup.Item it in items)
            {
                if (it.Problem) problems++;
                string prefix = it.Problem ? "⚠  " : "✔  ";
                _list.Items.Add(prefix + it.Name + "   —   " + it.Status, it.Problem);
            }
            _summary.Text = problems == 0
                ? "✔ Aucun réglage néfaste détecté — ton PC n'a pas été abîmé par un mauvais optimiseur."
                : problems + " réglage(s) néfaste(s) détecté(s) — clique sur CORRIGER LA SÉLECTION.";
            SetBusy(false);
        }

        private void OnFix(object sender, EventArgs e)
        {
            var sel = new List<Checkup.Item>();
            for (int i = 0; i < _list.Items.Count && i < _items.Count; i++)
                if (_list.GetItemChecked(i)) sel.Add(_items[i]);
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Coche au moins un réglage à corriger.", "DesTinGOOD",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            bool reboot = false;
            foreach (Checkup.Item it in sel) if (it.NeedReboot) reboot = true;
            string msg = "Remettre les valeurs saines de Windows pour " + sel.Count + " réglage(s) ?";
            if (reboot) msg += "\n\n• Un ou plusieurs points nécessitent un REDÉMARRAGE.";
            if (MessageBox.Show(this, msg, "Corriger", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            SetBusy(true);
            _summary.Text = "Correction en cours...";
            Task.Run(() =>
            {
                foreach (Checkup.Item it in sel)
                {
                    try { it.Fix(_log); }
                    catch (Exception ex) { _log("Correction « " + it.Name + " » : " + ex.Message, 3); }
                }
                _log("Réglages néfastes : " + sel.Count + " correction(s) appliquée(s).", 1);
                List<Checkup.Item> after = Checkup.Analyze();
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        Populate(after);
                        if (reboot)
                            MessageBox.Show(this, "Correction terminée. Redémarre le PC pour les points qui le demandent.",
                                "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch { }
            });
        }
    }
}
