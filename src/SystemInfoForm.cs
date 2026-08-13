using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Inventaire matériel + diagnostic santé avec corrections en un clic.</summary>
    internal class SystemInfoForm : Form
    {
        private static readonly Color Accent = Theme.AccentColor;

        private readonly Action<string, int> _log;
        private ListView _list;
        private Panel _diagWrap;
        private TableLayoutPanel _diagTable;
        private ToolTip _tip;
        private List<ComponentInfo.Section> _sections;
        private string _diagText = "";
        private List<Font> _ownedFonts;   // polices allouées une fois, libérées au Dispose
        private Font _diagFont;           // réutilisée par chaque libellé de constat
        private Font _fixFont;            // réutilisée par chaque bouton de correction

        private Font Own(Font f) { _ownedFonts.Add(f); return f; }

        public SystemInfoForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Load += (s, e) => Reload();
        }

        private void Build()
        {
            Text = "ONYX — Composants & diagnostic";
            ClientSize = new Size(660, 580);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 480);
            BackColor = Color.FromArgb(245, 246, 248);
            _ownedFonts = new List<Font>();
            _diagFont = Own(new Font("Segoe UI", 9.5f));
            _fixFont = Own(new Font("Segoe UI", 8.75f));
            Font = Own(new Font("Segoe UI", 9f));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            _tip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Composants & diagnostic santé", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = Own(new Font("Segoe UI Semibold", 13f)), TextAlign = ContentAlignment.MiddleLeft
            });

            // ---- Panneau diagnostic (constats + boutons de correction) ----
            _diagWrap = new Panel { Dock = DockStyle.Top, Height = 176, Padding = new Padding(12, 6, 12, 6) };
            var diagHead = new Label
            {
                Text = "Diagnostic santé — corrige en un clic", Dock = DockStyle.Top, Height = 22,
                Font = Own(new Font("Segoe UI Semibold", 10f)), ForeColor = Color.FromArgb(50, 70, 130)
            };
            _diagTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows, BackColor = Color.Transparent
            };
            _diagTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _diagTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _diagWrap.Controls.Add(_diagTable);
            _diagWrap.Controls.Add(diagHead);
            _diagWrap.Controls.SetChildIndex(_diagTable, 0);

            // ---- Inventaire ----
            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false,
                HeaderStyle = ColumnHeaderStyle.None, ShowGroups = true, Font = Own(new Font("Segoe UI", 9.5f))
            };
            _list.Columns.Add("Propriété", 230);
            _list.Columns.Add("Valeur", 400);
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 6) };
            host.Controls.Add(_list);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(12, 7, 12, 7) };
            var refresh = MakeBtn("Rafraîchir", 110, DockStyle.Left);
            refresh.Click += (s, e) => Reload();
            var export = MakeBtn("Exporter (.txt)", 130, DockStyle.Left);
            export.Click += OnExport;
            var copy = MakeBtn("Copier", 100, DockStyle.Left);
            copy.Click += (s, e) => { try { Clipboard.SetText(_diagText + "\n" + ComponentInfo.ToText(_sections)); } catch { } };
            var close = MakeBtn("Fermer", 100, DockStyle.Right);
            close.Click += (s, e) => Close();
            bottom.Controls.Add(new Label { Dock = DockStyle.Fill });
            bottom.Controls.Add(export); bottom.Controls.Add(copy); bottom.Controls.Add(refresh);
            bottom.Controls.Add(close);

            Controls.Add(banner);
            Controls.Add(_diagWrap);
            Controls.Add(host);
            Controls.Add(bottom);
            Controls.SetChildIndex(banner, 3);
            Controls.SetChildIndex(_diagWrap, 2);
            Controls.SetChildIndex(bottom, 1);
            Controls.SetChildIndex(host, 0);

            Theme.Apply(this);
        }

        private static Button MakeBtn(string text, int w, DockStyle dock)
        {
            var b = new Button { Text = text, Width = w, Dock = dock, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(4, 0, 4, 0) };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private static Color DiagColor(int level)
        {
            bool d = Theme.Dark;
            if (level == 2) return d ? Color.FromArgb(240, 100, 100) : Color.FromArgb(200, 40, 40);
            if (level == 1) return d ? Color.FromArgb(240, 185, 70) : Color.FromArgb(190, 120, 0);
            return d ? Color.FromArgb(90, 205, 150) : Color.FromArgb(0, 140, 80);
        }

        private Button MakeFixButton(Diagnostics.Finding fd)
        {
            bool primary = fd.Level == 2;
            var b = new Button
            {
                Text = fd.FixLabel, Width = 122, Height = 26, Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 3, 2, 3),
                BackColor = primary ? Accent : (Theme.Dark ? Color.FromArgb(44, 48, 56) : Color.White),
                ForeColor = primary ? Color.White : (Theme.Dark ? Color.FromArgb(210, 216, 222) : Color.FromArgb(40, 44, 52)),
                Font = _fixFont, Tag = fd
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(120, 126, 134);
            b.Click += (s, e) => OnFix((Diagnostics.Finding)((Button)s).Tag);
            return b;
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (_diagTable != null) _diagTable.Enabled = !busy;
        }

        private volatile bool _busy;

        private void Reload()
        {
            if (_busy) return;
            _busy = true;
            SetBusy(true);
            // Collecte LOURDE (≈15 requêtes WMI + nvidia-smi) déportée hors du thread interface :
            // sinon la fenêtre gèle plusieurs secondes à l'ouverture, à chaque « Rafraîchir » et
            // après chaque correction. Les mutations de contrôles restent sur le thread interface.
            Task.Run(() =>
            {
                List<Diagnostics.Finding> findings;
                List<ComponentInfo.Section> sections;
                try { findings = new List<Diagnostics.Finding>(Diagnostics.Run()); }
                catch { findings = new List<Diagnostics.Finding>(); }
                try { sections = ComponentInfo.Gather(); }
                catch { sections = new List<ComponentInfo.Section>(); }
                try { BeginInvoke((Action)(() => ApplyReload(findings, sections))); } catch { }
            });
        }

        // Sur le thread interface uniquement : constats (Labels/Boutons) + inventaire (ListView).
        private void ApplyReload(List<Diagnostics.Finding> findings, List<ComponentInfo.Section> sections)
        {
            // ---- Diagnostic ----
            _diagTable.SuspendLayout();
            // Libère les anciens contrôles : Controls.Clear() détache sans disposer
            // (chaque Label/Button possède un handle natif + une entrée ToolTip).
            var stale = new List<Control>();
            foreach (Control c in _diagTable.Controls) stale.Add(c);
            _diagTable.Controls.Clear();
            _diagTable.RowStyles.Clear();
            _diagTable.RowCount = 0;
            foreach (Control c in stale) { try { _tip.SetToolTip(c, string.Empty); } catch { } c.Dispose(); }
            var db = new System.Text.StringBuilder();
            int r = 0;
            try
            {
                foreach (Diagnostics.Finding fd in findings)
                {
                    string icon = fd.Level == 2 ? "✗" : (fd.Level == 1 ? "!" : "✓");
                    db.AppendLine("  [" + icon + "] " + fd.Text);
                    var lbl = new Label
                    {
                        Text = icon + "   " + fd.Text, Dock = DockStyle.Fill, AutoEllipsis = true,
                        TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent,
                        ForeColor = DiagColor(fd.Level), Margin = new Padding(2, 1, 6, 1),
                        Font = _diagFont
                    };
                    _tip.SetToolTip(lbl, fd.Text);
                    _diagTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
                    _diagTable.Controls.Add(lbl, 0, r);
                    if (fd.Fix != FixKind.None)
                        _diagTable.Controls.Add(MakeFixButton(fd), 1, r);
                    r++;
                }
                _diagTable.RowCount = r;
            }
            catch { }
            _diagText = db.Length > 0 ? "[Diagnostic]\n" + db.ToString() : "";
            _diagTable.ResumeLayout();

            // ---- Inventaire ----
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();
            _sections = sections;
            foreach (ComponentInfo.Section sec in _sections)
            {
                var g = new ListViewGroup(sec.Title) { HeaderAlignment = HorizontalAlignment.Left };
                _list.Groups.Add(g);
                foreach (string[] row in sec.Rows)
                {
                    var it = new ListViewItem(row[0]) { Group = g };
                    it.SubItems.Add(row[1]);
                    _list.Items.Add(it);
                }
            }
            _list.EndUpdate();
            SetBusy(false);
            _busy = false;
        }

        private void OnFix(Diagnostics.Finding fd)
        {
            try
            {
                switch (fd.Fix)
                {
                    case FixKind.CleanDisk:
                        using (var f = new CleanupForm(_log)) f.ShowDialog(this);
                        Reload();
                        break;

                    case FixKind.Timer1ms:
                        Native.SetTimer1ms(true);
                        if (_log != null) _log("Timer forcé à 1 ms depuis le diagnostic.", 1);
                        Reload();
                        break;

                    case FixKind.DisableVbs:
                        DisableVbs();
                        break;

                    case FixKind.OpenRestore:
                        EnableRestore();
                        break;

                    case FixKind.WindowsUpdate:
                        if (!StartShell("ms-settings:windowsupdate", null))
                            StartShell("control.exe", "/name Microsoft.WindowsUpdate");
                        break;

                    case FixKind.DisableCoreSync:
                        DisableCoreSync();
                        break;

                    case FixKind.DisableSdm:
                        DisableSdm();
                        break;

                    case FixKind.DisplaySettings:
                        // Page « Affichage avancé » (choix de l'écran + fréquence de rafraîchissement)
                        if (!StartShell("ms-settings:display-advanced", null))
                            StartShell("ms-settings:display", null);
                        break;

                    case FixKind.BiosGuide:
                        // XMP/EXPO se règle dans le firmware : rien à corriger depuis Windows,
                        // mais on ne laisse pas l'utilisateur devant un constat sans issue.
                        using (var bg = new BiosGuideForm(_log)) bg.ShowDialog(this);
                        break;

                    case FixKind.CleanJunk:
                        FindJunk();
                        break;

                    case FixKind.ReapplyTweaks:
                        ReapplyDrifted();
                        break;

                    case FixKind.DeviceManager:
                        StartShell("devmgmt.msc", null);
                        break;

                    case FixKind.NvLatencySafe:
                        NvLatencySafe();
                        break;

                    case FixKind.AudioPanel:
                        // Ces réglages vivent dans des clés protégées par le système, et un format
                        // audio malformé rend un périphérique muet : ONYX ouvre le panneau natif
                        // plutôt que d'écrire à l'aveugle.
                        StartShell("mmsys.cpl", null);
                        break;

                    case FixKind.CleanPowerPlans:
                        CleanPowerPlans();
                        break;

                    case FixKind.CbsReport:
                        ShowCbsReport();
                        break;
                }
            }
            catch (Exception ex) { if (_log != null) _log("Action impossible : " + ex.Message, 3); }
        }

        private bool StartShell(string file, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true };
                if (args != null) psi.Arguments = args;
                System.Diagnostics.Process.Start(psi);
                return true;
            }
            catch { return false; }
        }

        private void EnableRestore()
        {
            // Le constat vient de la stratégie HKLM\...\Policies\...\SystemRestore\DisableSR = 1,
            // que la fenêtre « Protection système » ne peut PAS outrepasser. On lève d'abord le
            // blocage (suppression des valeurs de stratégie), puis on ouvre le dialogue.
            if (MessageBox.Show(this,
                    "La restauration système est bloquée par une stratégie.\n\n"
                    + "Lever le blocage puis ouvrir la fenêtre « Protection système » pour l'activer ?\n"
                    + "(Réversible : la stratégie peut être remise.)",
                    "Restauration système", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;
            try
            {
                Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR");
                Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableConfig");
                if (_log != null) _log("Blocage de la restauration système levé (stratégie supprimée).", 1);
            }
            catch (Exception ex) { if (_log != null) _log("Impossible de lever le blocage : " + ex.Message, 3); }
            StartShell("SystemPropertiesProtection.exe", null);
            Reload();
        }

        /// <summary>
        /// Traduit le journal CBS : POURQUOI SFC a échoué, et quels fichiers il n'a pas pu réparer.
        /// Lecture seule — ce bouton ne répare rien, il explique.
        /// </summary>
        private void ShowCbsReport()
        {
            SetBusy(true);
            Task.Run(() =>
            {
                string texte;
                try
                {
                    string contenu = CbsLog.Fin(CbsLog.CheminDefaut, 2 * 1024 * 1024);
                    texte = CbsLog.Texte(CbsLog.Analyse(contenu), CbsLog.FichiersNonReparables(contenu, 12));
                }
                catch (Exception ex) { texte = "Lecture du journal impossible : " + ex.Message; }
                string t = texte;
                try { BeginInvoke((Action)(() =>
                {
                    SetBusy(false);
                    MessageBox.Show(this,
                        t + "\n\nMarche à suivre : répare d'abord l'IMAGE (menu Système → « Réparer Windows », qui "
                          + "lance DISM), puis relance SFC. Dans cet ordre uniquement : SFC pioche ses fichiers de "
                          + "remplacement dans le magasin de composants, donc il échoue tant que ce magasin est abîmé.",
                        "Journal de réparation Windows (CBS)", MessageBoxButtons.OK, MessageBoxIcon.Information);
                })); } catch { }
            });
        }

        /// <summary>
        /// Supprime les plans d'alimentation en double laissés par les scripts d'optimisation.
        /// Le plan ACTIF et les plans intégrés de Windows ne sont jamais proposés.
        /// </summary>
        private void CleanPowerPlans()
        {
            List<PowerPlans.Plan> doublons;
            try { doublons = PowerPlans.Doublons(PowerPlans.Lister()); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Lecture impossible : " + ex.Message, "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (doublons.Count == 0)
            {
                MessageBox.Show(this, "Aucun doublon : tes plans d'alimentation sont propres.",
                    "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Reload();
                return;
            }

            var liste = new System.Text.StringBuilder();
            foreach (var p in doublons) liste.Append("• ").Append(p.Nom).Append("   (").Append(p.Guid).Append(")\n");
            if (MessageBox.Show(this,
                    "Supprimer ces " + doublons.Count + " plan(s) d'alimentation en double ?\n\n"
                    + liste
                    + "\n• Ce sont des copies laissées par des scripts « boost » : chaque exécution de "
                    + "powercfg -duplicatescheme en crée une nouvelle, même nom, GUID différent.\n"
                    + "• Ton plan ACTIF n'est pas dans la liste, ni les plans intégrés de Windows.\n"
                    + "• Réversible : `powercfg -restoredefaultschemes` régénère les plans de Windows.",
                    "Plans d'alimentation en double",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            SetBusy(true);
            Task.Run(() =>
            {
                int n = 0;
                try { n = PowerPlans.Supprimer(doublons, _log); }
                catch (Exception ex) { if (_log != null) _log("Nettoyage des plans : échec (" + ex.Message + ").", 3); }
                int fait = n;
                try { BeginInvoke((Action)(() =>
                {
                    SetBusy(false);
                    MessageBox.Show(this, fait + " plan(s) supprimé(s)"
                        + (fait < doublons.Count ? ", " + (doublons.Count - fait) + " ont résisté (voir le journal)." : "."),
                        "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Reload();
                })); } catch { }
            });
        }

        /// <summary>
        /// Repasse le pilote NVIDIA sur le profil SÛR : latence toujours réduite, mais la file de
        /// rendu est rendue au jeu — c'est elle qui amortit les à-coups du processeur.
        /// </summary>
        private void NvLatencySafe()
        {
            double cpuAvg, gpuAvg; DateTime quand;
            bool mesure = Bottleneck.LastMeasure(out cpuAvg, out gpuAvg, out quand);
            string constat = mesure
                ? "Ta dernière mesure en jeu : CPU " + Math.Round(cpuAvg) + " %, GPU " + Math.Round(gpuAvg) + " %.\n\n"
                : "";
            if (MessageBox.Show(this,
                    constat
                    + "Remettre le pilote NVIDIA sur le profil SÛR ?\n\n"
                    + "• Mode faible latence : « Activé » au lieu d'« Ultra ».\n"
                    + "• Images pré-rendues : rendues au jeu (c'est ce tampon qui absorbe les à-coups du processeur).\n"
                    + "• Performances maximales : conservé, il ne coûte aucune image.\n\n"
                    + "Tu perds quelques millisecondes de latence de souris, tu récupères des images et surtout de la "
                    + "STABILITÉ. Réversible : le bouton « Ultra » reste disponible dans Overclock & pilote.",
                    "Profil pilote NVIDIA", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            SetBusy(true);
            Task.Run(() =>
            {
                bool ok = false;
                try { ok = NvProfile.Applique(NvProfile.Kind.Sur, _log); }
                catch (Exception ex) { if (_log != null) _log("Profil NVIDIA : échec (" + ex.Message + ").", 3); }
                bool done = ok;
                try { BeginInvoke((Action)(() =>
                {
                    SetBusy(false);
                    MessageBox.Show(this, done
                        ? "Profil sûr appliqué. Relance ton jeu : l'effet est visible dès le prochain lancement."
                        : "Le profil n'a pas pu être appliqué (nvidiaProfileInspector introuvable ou refusé) — voir le journal.",
                        "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Reload();
                })); } catch { }
            });
        }

        /// <summary>
        /// Ré-applique les réglages que Windows a annulés. On repasse par le moteur habituel
        /// (sauvegarde du registre comprise) : aucune écriture « à la main » qui contournerait le
        /// filet de sécurité. La liste exacte est montrée avant d'agir.
        /// </summary>
        private void ReapplyDrifted()
        {
            List<TweakDrift.Drifted> drift;
            try { drift = TweakDrift.Detect(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Lecture impossible : " + ex.Message, "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (drift.Count == 0)
            {
                MessageBox.Show(this, "Plus aucune dérive : tous les réglages appliqués sont en place.",
                    "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Reload();
                return;
            }

            bool reboot = false;
            foreach (var d in drift) if (d.Reboot) reboot = true;
            if (MessageBox.Show(this,
                    TweakDrift.Format(drift, 15) + "\n"
                    + "Les ré-appliquer maintenant ?\n\n"
                    + "• Ce sont des réglages que TU avais déjà appliqués : Windows les a remis par défaut de son côté.\n"
                    + "• Une sauvegarde du registre est créée avant toute écriture, comme pour une application normale.\n"
                    + (reboot ? "• Certains ne prendront effet qu'après un redémarrage.\n" : ""),
                    "Réglages annulés par Windows",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            var ids = new HashSet<string>(TweakDrift.Ids(drift), StringComparer.Ordinal);
            var selection = new List<Tweak>();
            try { foreach (var t in Catalog.All()) if (t != null && ids.Contains(t.Id)) selection.Add(t); }
            catch { }
            if (selection.Count == 0)
            {
                MessageBox.Show(this, "Aucun de ces réglages n'a été retrouvé dans le catalogue.",
                    "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true);
            Task.Run(() =>
            {
                EngineResult res = null;
                try { res = Engine.Run(selection, true, true, false, _log ?? delegate { }); }
                catch (Exception ex) { if (_log != null) _log("Ré-application : échec (" + ex.Message + ").", 3); }
                EngineResult r = res;
                try { BeginInvoke((Action)(() =>
                {
                    SetBusy(false);
                    string msg = r == null
                        ? "La ré-application a échoué — voir le journal."
                        : r.Ok + " réglage(s) ré-appliqué(s)" + (r.Ko > 0 ? ", " + r.Ko + " échec(s)" : "") + "."
                          + (r.RebootNeeded ? "\n\nUn redémarrage est nécessaire pour que tout prenne effet." : "");
                    MessageBox.Show(this, msg, "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Reload();
                })); } catch { }
            });
        }

        /// <summary>
        /// Disque de données presque plein : avant de conseiller « désinstalle des jeux », on cherche
        /// le poids mort — journaux partis en boucle, restes de téléchargements Steam abandonnés. Le
        /// balayage est BORNÉ (8 s) et déporté hors du thread interface. Rien n'est supprimé sans que
        /// l'utilisateur ait lu la liste exacte de ce qui va partir.
        /// </summary>
        private void FindJunk()
        {
            SetBusy(true);
            Task.Run(() =>
            {
                List<JunkScan.Item> items;
                try { items = JunkScan.Scan(JunkScan.DefaultMinBytes, 8000, _log); }
                catch (Exception ex)
                {
                    if (_log != null) _log("Recherche du poids mort : échec (" + ex.Message + ").", 3);
                    items = new List<JunkScan.Item>();
                }
                try { BeginInvoke((Action)(() => ShowJunk(items))); } catch { }
            });
        }

        private void ShowJunk(List<JunkScan.Item> items)
        {
            SetBusy(false);
            if (items == null || items.Count == 0)
            {
                MessageBox.Show(this,
                    "Aucun poids mort trouvé : pas de journal obèse (≥ 1 Go) ni de téléchargement Steam abandonné.\n\n"
                    + "L'espace est donc occupé par de vraies données. Le classement « Où sont passés mes Go ? » "
                    + "te dira quels dossiers pèsent le plus.",
                    "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this,
                    JunkScan.Format(items, 12) + "\n"
                    + "Supprimer définitivement ces éléments ?\n\n"
                    + "• Journaux : de simples fichiers de trace, sans valeur une fois l'incident passé. Seuls ceux qui ne sont plus écrits depuis "
                    + JunkScan.LogIdleDays + " jours sont listés (une appli vivante n'est jamais touchée).\n"
                    + "• Restes Steam : morceaux d'un téléchargement figé depuis plus de " + JunkScan.DownloadIdleDays
                    + " jours. Steam les retéléchargera tout seul si tu relances ce jeu.\n"
                    + "• IRRÉVERSIBLE : ces éléments ne passent pas par la corbeille.\n"
                    + "• Ferme Steam avant de valider si des restes Steam sont listés.",
                    "Libérer " + SteamGames.Human(JunkScan.Total(items)),
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            SetBusy(true);
            Task.Run(() =>
            {
                long freed = 0;
                List<string> failed = new List<string>();
                try { freed = JunkScan.Delete(items, out failed, _log); }
                catch (Exception ex) { if (_log != null) _log("Suppression : échec (" + ex.Message + ").", 3); }
                long f2 = freed;
                List<string> ko = failed;
                try { BeginInvoke((Action)(() =>
                {
                    SetBusy(false);
                    string msg = SteamGames.Human(f2) + " libérés.";
                    if (ko != null && ko.Count > 0)
                        msg += "\n\n" + ko.Count + " élément(s) n'ont pas pu être supprimés (fichier en cours d'utilisation "
                             + "— ferme l'application concernée, ou Steam, puis relance) :\n• " + string.Join("\n• ", ko.ToArray());
                    MessageBox.Show(this, msg, "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Reload();
                })); } catch { }
            });
        }

        private void DisableCoreSync()
        {
            if (MessageBox.Show(this,
                    "Désactiver Samsung CoreSync ?\n\n"
                    + "• CoreSync synchronise l'éclairage arrière (Core Lighting) des moniteurs Odyssey en capturant l'écran en continu : cause connue de saccades, pertes de FPS et d'input lag en jeu.\n"
                    + "• Action : fermeture de l'appli + désactivation de son lancement au démarrage de Windows.\n"
                    + "• L'éclairage reste réglable sans logiciel, directement dans le menu du moniteur (Jeu → Éclairage Core), en couleur fixe.\n"
                    + "• Réversible : relance l'appli CoreSync, ou réactive-la dans Gestionnaire des tâches → Applications de démarrage.",
                    "Samsung CoreSync", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;
            // Disable() ferme l'appli (Kill + WaitForExit) et touche démarrage/registre :
            // hors thread interface pour ne pas figer la fenêtre ~3 s après le clic.
            SetBusy(true);
            Task.Run(() =>
            {
                int n = 0;
                try { n = CoreSyncCheck.Disable(_log); }
                catch (Exception ex) { if (_log != null) _log("CoreSync : échec (" + ex.Message + ").", 3); }
                try { BeginInvoke((Action)(() =>
                {
                    SetBusy(false);
                    if (n > 0)
                        MessageBox.Show(this, "Samsung CoreSync neutralisé (" + n + " action(s)) : appli fermée et/ou démarrage automatique coupé.",
                            "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else
                        MessageBox.Show(this, "Rien à faire : CoreSync ne tournait pas et aucun démarrage automatique n'a été trouvé.\n"
                            + "Si les saccades persistent, désactive aussi CoreSync dans le menu du moniteur (Jeu → Éclairage Core).",
                            "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Reload();
                })); } catch { }
            });
        }

        private void DisableSdm()
        {
            if (MessageBox.Show(this,
                    "Neutraliser Samsung Display Manager (et le service MAPT s'il est installé) ?\n\n"
                    + "• Cette appli compagnon n'est pas nécessaire : CoreSync / l'éclairage est géré par le moniteur lui-même (menu OSD).\n"
                    + "• MAPT est un service B2B (pont réseau via la prise LAN du moniteur), inutile à la maison.\n"
                    + "• Action : fermeture de l'appli, retrait du démarrage automatique (Run, raccourcis, tâches planifiées), arrêt + désactivation du service MAPT.\n"
                    + "• Réversible : relance l'appli, réactive la tâche dans le Planificateur, ou remets le service en « Manuel » (services.msc).",
                    "Samsung Display Manager", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;
            SetBusy(true);
            Task.Run(() =>
            {
                int n = 0;
                try { n = SdmCheck.Disable(_log); }
                catch (Exception ex) { if (_log != null) _log("SDM : échec (" + ex.Message + ").", 3); }
                try { BeginInvoke((Action)(() =>
                {
                    SetBusy(false);
                    MessageBox.Show(this,
                        n > 0 ? "Samsung Display Manager neutralisé (" + n + " action(s))."
                              : "Rien à faire : l'appli ne tournait pas et aucun démarrage automatique ni service MAPT n'a été trouvé.",
                        "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Reload();
                })); } catch { }
            });
        }

        private void DisableVbs()
        {
            if (MessageBox.Show(this,
                    "Désactiver l'intégrité de la mémoire (VBS / HVCI) ?\n\n"
                    + "• Gain : moins de latence et plus de FPS en jeu.\n"
                    + "• Contrepartie : c'est une protection de sécurité de Windows. La désactiver réduit la protection contre certains pilotes malveillants.\n"
                    + "• Réversible : une sauvegarde .reg est créée ; tu peux réactiver via « Rétablir » dans l'app.\n"
                    + "• Un redémarrage est nécessaire pour appliquer.",
                    "Intégrité de la mémoire", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            Tweak t = Catalog.All().Find(x => x.Id == "vbs_off");
            if (t == null) { if (_log != null) _log("Optimisation « vbs_off » introuvable.", 3); return; }

            SetBusy(true);
            Task.Run(() =>
            {
                EngineResult res = Engine.Run(new List<Tweak> { t }, true, true, false, _log);
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        SetBusy(false);
                        Reload();
                        bool ok = res != null && !res.PrepFailed && res.Ok > 0 && res.Ko == 0;
                        if (ok)
                            MessageBox.Show(this, "Intégrité de la mémoire désactivée.\nRedémarre pour que le changement prenne effet.",
                                "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        else
                        {
                            string why = res == null ? "erreur inconnue"
                                : (res.PrepFailed ? res.PrepError : "l'écriture registre a échoué (droits administrateur requis)");
                            MessageBox.Show(this, "Échec : " + why + ".\nAucun changement appliqué.", "ONYX",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }));
                }
                catch { }
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_tip != null) { try { _tip.RemoveAll(); _tip.Dispose(); } catch { } _tip = null; }
                if (_ownedFonts != null)
                {
                    foreach (Font fnt in _ownedFonts) { try { fnt.Dispose(); } catch { } }
                    _ownedFonts.Clear();
                }
            }
            base.Dispose(disposing);
        }

        private void OnExport(object sender, EventArgs e)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "bt-composants.txt");
                System.IO.File.WriteAllText(path, _diagText + "\n" + ComponentInfo.ToText(_sections), new System.Text.UTF8Encoding(false));
                if (_log != null) _log("Composants exportés : " + path, 1);
                System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex) { if (_log != null) _log("Export impossible : " + ex.Message, 3); }
        }
    }
}
