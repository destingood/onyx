using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Mode jeu : ce qui est suspendu pendant une partie, et les exclusions.
    //  Chaque service a un interrupteur : ON = le mode jeu peut le suspendre,
    //  OFF = exclu (jamais touché). Choix persisté, appliqué à la prochaine
    //  activation du mode jeu. Parité « services affectés / exclusions ».
    // ----------------------------------------------------------------------
    internal class BoostConfigForm : Form
    {
        private static readonly Dictionary<string, string> Explain = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "SysMain",         "Préchargement des applications (Superfetch)" },
            { "WSearch",         "Indexation des fichiers pour la recherche Windows" },
            { "Spooler",         "File d'impression (imprimantes)" },
            { "DiagTrack",       "Télémétrie et diagnostics Microsoft" },
            { "WMPNetworkSvc",   "Partage multimédia du Lecteur Windows Media" },
            { "MapsBroker",      "Cartes hors connexion" },
            { "dmwappushservice","Messages de routage WAP" },
        };

        private readonly Action<string, int> _log;
        private readonly Dictionary<string, NeonSwitch> _sw = new Dictionary<string, NeonSwitch>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NeonSwitch> _swApps = new Dictionary<string, NeonSwitch>(StringComparer.OrdinalIgnoreCase);

        public BoostConfigForm(Action<string, int> log)
        {
            _log = log;
            BuildUi();
            Theme.Apply(this);
        }

        private void BuildUi()
        {
            Text = "Mode jeu — ce qui s'arrête pendant la partie — ONYX";
            ClientSize = new Size(620, 190 + GameBoost.AffectedServices.Length * 30
                                       + ApplisDeFond.Catalogue.Length * 46);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            var intro = new Label();
            intro.SetBounds(16, 10, 588, 48);
            intro.Text = "Pendant une partie, le MODE JEU suspend ces services de fond (ils sont relancés à la "
                       + "sortie du jeu) et met en mode Efficacité les applications listées plus bas. Interrupteur OFF = exclu : "
                       + "jamais touché. Choix mémorisé, appliqué à la prochaine activation.";
            intro.ForeColor = Theme.InkDimColor;

            HashSet<string> excluded = GameBoost.LoadExclusions();
            int y = 66;
            foreach (string svc in GameBoost.AffectedServices)
            {
                var sw = new NeonSwitch();
                sw.SetBounds(16, y, 46, 22);
                sw.SetCheckedSilent(!excluded.Contains(svc));   // ON = suspendable
                sw.Tag = svc;
                sw.CheckedChanged += (s, e) => SaveNow();
                _sw[svc] = sw;

                var name = new Label();
                name.SetBounds(70, y + 1, 150, 20);
                name.Font = new Font("Segoe UI Semibold", 9f);
                name.Text = svc;

                var desc = new Label();
                desc.SetBounds(222, y + 2, 382, 20);
                desc.ForeColor = Theme.InkDimColor;
                string d;
                desc.Text = Explain.TryGetValue(svc, out d) ? d : GameBoost.FriendlyName(svc);

                Controls.AddRange(new Control[] { sw, name, desc });
                y += 30;
            }

            var note = new Label();
            note.SetBounds(16, y + 4, 588, 32);
            note.ForeColor = Theme.InkDimColor;
            note.Font = new Font("Segoe UI", 8f);
            note.Text = "Exemple : tu imprimes pendant que tu joues ? Mets « Spooler » sur OFF. "
                      + "Un service déjà désactivé par tes optimisations n'est de toute façon jamais touché.";
            y += 42;

            // ---------------- Applications de fond ------------------------------------------
            var titre = new Label();
            titre.SetBounds(16, y, 588, 22);
            titre.Font = new Font("Segoe UI Semibold", 10f);
            titre.Text = "Applications mises en mode Efficacité pendant la partie";
            y += 24;

            var sous = new Label();
            sous.SetBounds(16, y, 588, 34);
            sous.ForeColor = Theme.InkDimColor;
            sous.Text = "Rien n'est fermé : ces applications passent en priorité basse (mode Efficacité, comme "
                      + "dans le Gestionnaire des tâches) et retrouvent leur priorité d'origine à la sortie "
                      + "du jeu. Rien n'est désinstallé.";
            y += 38;

            Controls.AddRange(new Control[] { intro, note, titre, sous });

            HashSet<string> exclApps = ApplisDeFond.ExclusionsChargees();
            foreach (ApplisDeFond.Categorie c in ApplisDeFond.Catalogue)
            {
                var swa = new NeonSwitch();
                swa.SetBounds(16, y, 46, 22);
                swa.SetCheckedSilent(!exclApps.Contains(c.Cle));   // ON = mise en Efficacité pendant la partie
                swa.Tag = c.Cle;
                swa.CheckedChanged += (s, e) => SaveApps();
                _swApps[c.Cle] = swa;

                var nom = new Label();
                nom.SetBounds(70, y + 1, 534, 20);
                nom.Font = new Font("Segoe UI Semibold", 9f);
                nom.Text = c.Libelle;

                var pourquoi = new Label();
                pourquoi.SetBounds(70, y + 20, 534, 31);
                pourquoi.ForeColor = Theme.InkDimColor;
                pourquoi.Font = new Font("Segoe UI", 8f);
                pourquoi.Text = c.Pourquoi;

                Controls.AddRange(new Control[] { swa, nom, pourquoi });
                y += 52;
            }

            var swl = new NeonSwitch();
            swl.SetBounds(16, y + 6, 46, 22);
            swl.SetCheckedSilent(GameBoost.LanceurPrioritaire);
            swl.CheckedChanged += (s, e) =>
            {
                GameBoost.LanceurPrioritaire = swl.Checked;
                if (_log != null) _log("Mode jeu : priorité du lanceur " + (swl.Checked ? "relevée" : "laissée telle quelle") + " pendant la partie (mémorisé).", 0);
            };
            var nl = new Label();
            nl.SetBounds(70, y + 7, 534, 20);
            nl.Font = new Font("Segoe UI Semibold", 9f);
            nl.Text = "Lanceur un cran au-dessus (Steam, Epic, EA, Battle.net, Ubisoft, GOG)";
            var pl = new Label();
            pl.SetBounds(70, y + 26, 534, 32);
            pl.ForeColor = Theme.InkDimColor;
            pl.Font = new Font("Segoe UI", 8f);
            pl.Text = "Priorité « au-dessus de la normale » pendant la partie, rendue à la sortie. Le jeu n'y gagne "
                    + "pas d'images : seuls l'overlay et les téléchargements du lanceur restent plus réactifs.";
            Controls.AddRange(new Control[] { swl, nl, pl });
            y += 58;

            // Garder la liste de côté ou la reprendre sur un autre PC (exclusions seulement).
            var exporte = OnyxDesign.Button("Enregistrer la liste…");
            exporte.SetBounds(16, y + 12, 190, 34);
            exporte.Click += (s, e) => ExporteListe();
            var importe = OnyxDesign.Button("Charger une liste…");
            importe.SetBounds(214, y + 12, 190, 34);
            importe.Click += (s, e) => ImporteListe();
            Controls.AddRange(new Control[] { exporte, importe });
            ClientSize = new Size(620, y + 62);
        }

        private static string DossierListes()
        {
            string d = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            try { if (!string.IsNullOrEmpty(d)) d = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(d, "ONYX")).FullName; } catch { }
            return d ?? "";
        }

        private void ExporteListe()
        {
            using (var d = new SaveFileDialog
            {
                Title = "Enregistrer la liste du Mode Jeu", Filter = "Liste ONYX (*.txt)|*.txt", DefaultExt = "txt", AddExtension = true,
                FileName = "mode-jeu-onyx.txt", InitialDirectory = DossierListes()
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    System.IO.File.WriteAllText(d.FileName, ListeModeJeu.Ecrit(GameBoost.LoadExclusions(), ApplisDeFond.ExclusionsChargees()));
                    if (_log != null) _log("Mode jeu : liste enregistrée dans " + d.FileName + ".", 0);
                }
                catch (Exception ex) { MessageBox.Show(this, "Enregistrement impossible : " + ex.Message, "Mode jeu", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void ImporteListe()
        {
            using (var d = new OpenFileDialog { Title = "Charger une liste du Mode Jeu", Filter = "Liste ONYX (*.txt)|*.txt", InitialDirectory = DossierListes() })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                ListeModeJeu.Contenu c = null;
                try
                {
                    if (new System.IO.FileInfo(d.FileName).Length <= 200000)
                    {
                        var applis = new List<string>();
                        foreach (ApplisDeFond.Categorie k in ApplisDeFond.Catalogue) applis.Add(k.Cle);
                        c = ListeModeJeu.Lit(System.IO.File.ReadAllText(d.FileName), GameBoost.AffectedServices, applis);
                    }
                }
                catch { }
                if (c == null) { MessageBox.Show(this, ListeModeJeu.Bilan(null), "Mode jeu", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (MessageBox.Show(this, ListeModeJeu.Bilan(c) + "\n\nRemplacer tes réglages actuels par cette liste ?", "Mode jeu",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                foreach (KeyValuePair<string, NeonSwitch> kv in _sw) kv.Value.SetCheckedSilent(!c.Services.Contains(kv.Key));
                foreach (KeyValuePair<string, NeonSwitch> kv in _swApps) kv.Value.SetCheckedSilent(!c.Applis.Contains(kv.Key));
                SaveNow();
                SaveApps();
            }
        }

        private void SaveApps()
        {
            var excluded = new List<string>();
            foreach (KeyValuePair<string, NeonSwitch> kv in _swApps)
                if (!kv.Value.Checked) excluded.Add(kv.Key);
            ApplisDeFond.EnregistrerExclusions(excluded);
            if (_log != null)
                _log("Mode jeu : " + (ApplisDeFond.Catalogue.Length - excluded.Count)
                     + " famille(s) d'applications en mode Efficacité pendant la partie (mémorisé).", 0);
        }

        private void SaveNow()
        {
            var excluded = new List<string>();
            foreach (KeyValuePair<string, NeonSwitch> kv in _sw)
                if (!kv.Value.Checked) excluded.Add(kv.Key);
            GameBoost.SaveExclusions(excluded);
            if (_log != null)
                _log("Mode jeu : " + excluded.Count + " service(s) exclu(s) de la suspension (mémorisé).", 0);
        }
    }
}
