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
    /// Réglages néfastes d'autres optimiseurs : détecte les tweaks DANGEREUX laissés par
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
            /// <summary>La vérification n'a PAS pu aboutir. Distinct de « pas de problème » :
            /// un détecteur qui n'a rien pu lire ne doit ni accuser, ni rassurer. Reste faux
            /// pour les contrôles qui lisent le registre, où l'absence de valeur est une
            /// réponse en soi (Windows applique son défaut).</summary>
            public bool Indetermine;
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
                DefragServiceOff(),
                FastStartupSansHibernation(),
                ServicesInterdits(),
                ClearPageFile(),
                LargeSystemCache(),
                UpdateBlocked(),
                AutoMaintenanceOff(),
            };
        }

        // Windows Update BLOQUÉ (outils type « Windows Update Blocker ») : plus AUCUNE mise à
        // jour, y compris de sécurité — le PC accumule des failles connues et non corrigées.
        private static Item UpdateBlocked()
        {
            bool off = false;
            try { off = Sys.ServiceDisabled("wuauserv"); } catch { }
            return new Item
            {
                Name = "Windows Update bloqué (service désactivé)",
                Problem = off,
                Security = true,
                Status = off ? "BLOQUÉ — plus aucune mise à jour, MÊME de sécurité (failles connues non corrigées)"
                             : "actif (bon)",
                Fix = delegate (Action<string, int> log)
                {
                    Sys.ConfigureService("wuauserv", "demand", false, false);
                    log("Windows Update réactivé (démarrage à la demande — la valeur normale de Windows).", 1);
                }
            };
        }

        // Maintenance automatique coupée (MaintenanceDisabled=1, courant dans les kits « boost ») :
        // le re-TRIM SSD, la défrag HDD et les nettoyages nocturnes ne passent plus.
        private static Item AutoMaintenanceOff()
        {
            bool off = Sys.IntEquals(Sys.GetMachine(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "MaintenanceDisabled"), 1);
            return new Item
            {
                Name = "Maintenance automatique de Windows désactivée",
                Problem = off,
                Status = off ? "DÉSACTIVÉE — re-TRIM SSD, défrag et nettoyages nocturnes ne tournent plus (perfs qui se dégradent avec le temps)"
                             : "active (bon)",
                Fix = delegate (Action<string, int> log)
                {
                    Sys.DelMachine(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "MaintenanceDisabled");
                    log("Maintenance automatique réactivée (valeur par défaut de Windows).", 1);
                }
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

        // Services qu'aucune liste de « debloat » ne devrait couper : une page de Windows en meurt.
        // Le constat double le garde-fou automatique — celui-ci répare au lancement, celui-là rend
        // la chose VISIBLE et explique laquelle des pages était cassée.
        private static Item ServicesInterdits()
        {
            List<ServiceGuard.Regle> v = ServiceGuard.Violations();
            bool bad = v.Count > 0;
            return new Item
            {
                Name = "Service désactivé dont une page de Windows a besoin",
                Problem = bad,
                Status = bad ? ServiceGuard.Texte(v)
                             : "aucun (les services critiques sont au moins en démarrage manuel)",
                Fix = delegate(Action<string, int> log)
                {
                    int n = ServiceGuard.Soigne(log);
                    log(n + " service(s) remis en démarrage manuel : ils ne tournent pas au repos, "
                      + "mais Windows peut les lancer à la demande.", 1);
                }
            };
        }

        // ÉTAT IMPOSSIBLE : démarrage rapide activé alors que la veille prolongée est coupée.
        // Le démarrage rapide range la session système dans hiberfil.sys — sans veille prolongée,
        // ce fichier n'existe pas. Windows ne produit JAMAIS cette combinaison lui-même ; elle
        // vient toujours d'un outil (ONYX inclus, avant correction) qui a écrit HiberbootEnabled
        // en direct sans regarder l'autre valeur. La page « Marche/Arrêt » des Paramètres, qui
        // affiche ces deux options côte à côte, peut planter en lisant cet état incohérent.
        private static Item FastStartupSansHibernation()
        {
            bool fastOn = Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled"), 1);
            bool hiberOn = Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled"), 1);
            bool bad = fastOn && !hiberOn;
            return new Item
            {
                Name = "Démarrage rapide activé sans veille prolongée (état incohérent)",
                Problem = bad,
                Status = bad ? "INCOHÉRENT — le démarrage rapide a besoin de la veille prolongée, qui est coupée : il ne fonctionne pas, et la page « Marche/Arrêt » de Windows peut planter"
                             : "cohérent",
                Fix = delegate(Action<string, int> log)
                {
                    // On aligne sur le choix DÉJÀ fait : la veille prolongée est coupée, donc on
                    // coupe le démarrage rapide. C'est le sens le moins intrusif — l'inverse
                    // recréerait un hiberfil.sys de plusieurs Go sans que l'utilisateur l'ait demandé.
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0, RegistryValueKind.DWord);
                    log("Démarrage rapide désactivé pour rétablir la cohérence (la veille prolongée était coupée).", 1);
                }
            };
        }

        // Le SERVICE derrière l'optimisation, distinct de la tâche planifiée ci-dessus. Les listes
        // de « services inutiles à désactiver » le citent régulièrement. Une fois coupé, ni
        // l'optimisation planifiée, ni « defrag /O », ni dfrgui ne fonctionnent : le SSD n'est plus
        // re-TRIMé, et l'utilisateur ne reçoit qu'un message d'erreur sans rapport apparent.
        // « Manuel » suffit : Windows démarre le service quand il en a besoin.
        private static Item DefragServiceOff()
        {
            bool off = Sys.ServiceDisabled("defragsvc");
            return new Item
            {
                Name = "Service « Optimiser les lecteurs » (defragsvc) désactivé",
                Problem = off,
                Status = off ? "DÉSACTIVÉ — l'outil d'optimisation ne peut plus démarrer du tout (ni TRIM, ni défragmentation)"
                             : "correct (démarrage manuel)",
                Fix = delegate(Action<string, int> log)
                {
                    Sys.ConfigureService("defragsvc", "demand", false, false);
                    log("Service « Optimiser les lecteurs » remis en démarrage manuel.", 1);
                }
            };
        }

        // 1. bcdedit useplatformclock=Yes : force le HPET → LATENCE en plus (mythe des guides).
        private static Item UsePlatformClock()
        {
            // Même piège que pour le TRIM : l'ABSENCE de « useplatformclock » dans la sortie ne
            // prouve rien si bcdedit n'a pas répondu du tout — sans élévation il rend « Accès
            // refusé », ce que l'ancien code lisait comme « non forcé (bon) ».
            //
            // On se fie au CODE DE RETOUR, pas au texte : bcdedit traduit ses messages, et chercher
            // un mot anglais dans la sortie aurait marqué toutes les machines françaises comme
            // non vérifiables. Ses échecs rendent 1, ses succès 0 — dans toutes les langues.
            bool on = false, lu = false;
            try
            {
                NativeResult r = Sys.Run(Sys.Sys32("bcdedit.exe"), "/enum {current}");
                string o = (r.Output ?? "").ToLowerInvariant();
                lu = r.ExitCode == 0 && o.Length > 0;
                int idx = o.IndexOf("useplatformclock");
                if (idx >= 0)
                {
                    lu = true;
                    string rest = o.Substring(idx, Math.Min(40, o.Length - idx));
                    on = rest.Contains("yes") || rest.Contains("true");
                }
            }
            catch { }
            return new Item
            {
                Name = "Timer HPET forcé (bcdedit useplatformclock)",
                Problem = on, Indetermine = !lu, NeedReboot = true,
                Status = !lu ? "impossible à vérifier — bcdedit n'a pas rendu la configuration de démarrage"
                            : on ? "ACTIVÉ — force le HPET, ajoute de la latence (à retirer)"
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
            // Ce contrôle conclut « désactivé » à partir de DEUX ABSENCES : pas de gestion
            // automatique, et aucun fichier d'échange listé. Or une requête WMI qui échoue produit
            // exactement les mêmes deux absences. Sans distinguer les cas, l'app accusait
            // l'utilisateur d'avoir coupé son fichier d'échange alors qu'elle n'avait rien pu lire
            // — et le verdict descend jusqu'au score de santé, qui perdait des points pour un
            // problème inventé.
            //
            // Le pire est que la panne de WMI est PRÉCISÉMENT le terrain de cette fenêtre : les
            // machines qu'un « optimiseur » a saccagées sont celles où le service Winmgmt a des
            // chances d'avoir été désactivé.
            bool auto = false, hasPageFile = false, lu = false;
            try
            {
                using (var cs = new ManagementObjectSearcher("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem"))
                    foreach (ManagementObject mo in cs.Get())
                    {
                        auto = Convert.ToBoolean(mo["AutomaticManagedPagefile"]);
                        lu = true;   // WMI a répondu : à partir d'ici, une absence veut dire quelque chose
                    }
                using (var pf = new ManagementObjectSearcher("SELECT Name FROM Win32_PageFileUsage"))
                    foreach (ManagementObject mo in pf.Get()) { hasPageFile = true; break; }
            }
            catch { lu = false; }
            bool bad = lu && !auto && !hasPageFile;
            return new Item
            {
                Name = "Fichier d'échange (pagefile) désactivé",
                Problem = bad, Indetermine = !lu, NeedReboot = true,
                Status = !lu ? "impossible à vérifier — Windows n'a pas répondu (le service WMI est peut-être désactivé)"
                             : bad ? "DÉSACTIVÉ — cause de plantages et d'« out of memory » en jeu"
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
            // « Aucune ligne ne dit = 1 » ne vaut « TRIM actif » que si fsutil a effectivement
            // répondu. S'il n'a rien rendu, l'ancien code annonçait « actif (bon) » : une bonne
            // nouvelle fabriquée à partir d'une absence de réponse.
            bool off = false, lu = false;
            try
            {
                NativeResult r = Sys.Run(Sys.Sys32("fsutil.exe"), "behavior query DisableDeleteNotify");
                foreach (string line in (r.Output ?? "").Split('\n'))
                {
                    if (line.IndexOf("DisableDeleteNotify", StringComparison.OrdinalIgnoreCase) >= 0) lu = true;
                    if (line.IndexOf("NTFS", StringComparison.OrdinalIgnoreCase) >= 0 && line.Contains("= 1")) off = true;
                }
            }
            catch { }
            return new Item
            {
                Name = "TRIM SSD désactivé (DisableDeleteNotify=1)",
                Problem = off, Indetermine = !lu,
                Status = !lu ? "impossible à vérifier — fsutil n'a pas répondu"
                             : off ? "DÉSACTIVÉ — mauvais pour la durée de vie et la vitesse d'un SSD"
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

        private static readonly Color Accent = Theme.AccentColor;

        public CheckupForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "ONYX — Réglages néfastes";
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
                Text = "  Réglages néfastes laissés par d'autres « optimiseurs »",
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
            int problems = 0, inconnus = 0;
            foreach (Checkup.Item it in items)
            {
                if (it.Problem) problems++;
                else if (it.Indetermine) inconnus++;
                // Trois états, pas deux : un contrôle qui n'a pas pu lire n'a pas sa place
                // derrière une coche verte.
                string prefix = it.Problem ? "⚠  " : it.Indetermine ? "?  " : "✔  ";
                _list.Items.Add(prefix + it.Name + "   —   " + it.Status, it.Problem);
            }
            // « Aucun réglage néfaste » est une affirmation : on ne la fait que si TOUS les
            // contrôles ont abouti. Sinon on dit ce qu'on sait, et ce qu'on ignore.
            _summary.Text = problems > 0
                ? problems + " réglage(s) néfaste(s) détecté(s) — clique sur CORRIGER LA SÉLECTION."
                : inconnus == 0
                ? "✔ Aucun réglage néfaste détecté — ton PC n'a pas été abîmé par un mauvais optimiseur."
                : "Aucun réglage néfaste parmi les contrôles qui ont abouti, mais " + inconnus
                  + " n'ont pas pu être vérifiés (marqués « ? ») — je ne peux pas te garantir que tout est sain.";
            SetBusy(false);
        }

        private void OnFix(object sender, EventArgs e)
        {
            var sel = new List<Checkup.Item>();
            for (int i = 0; i < _list.Items.Count && i < _items.Count; i++)
                if (_list.GetItemChecked(i)) sel.Add(_items[i]);
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Coche au moins un réglage à corriger.", "ONYX",
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
                                "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch { }
            });
        }
    }
}
