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
                       + "sortie du jeu) et ferme les applications listées plus bas. Interrupteur OFF = exclu : "
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
            note.SetBounds(16, y + 6, 588, 20);
            note.ForeColor = Theme.InkDimColor;
            note.Font = new Font("Segoe UI", 8f);
            note.Text = "Exemple : tu imprimes pendant que tu joues ? Mets « Spooler » sur OFF. "
                      + "Un service déjà désactivé par tes optimisations n'est de toute façon jamais touché.";
            y += 32;

            // ---------------- Applications de fond ------------------------------------------
            var titre = new Label();
            titre.SetBounds(16, y, 588, 22);
            titre.Font = new Font("Segoe UI Semibold", 10f);
            titre.Text = "Applications fermées pendant la partie";
            y += 24;

            var sous = new Label();
            sous.SetBounds(16, y, 588, 34);
            sous.ForeColor = Theme.InkDimColor;
            sous.Text = "Une fenêtre qui demande « enregistrer ? » n'est JAMAIS forcée : elle reste ouverte "
                      + "et le journal te la nomme. Rien n'est désinstallé, rien n'est relancé tout seul au "
                      + "retour sur le bureau.";
            y += 38;

            Controls.AddRange(new Control[] { intro, note, titre, sous });

            HashSet<string> exclApps = ApplisDeFond.ExclusionsChargees();
            foreach (ApplisDeFond.Categorie c in ApplisDeFond.Catalogue)
            {
                var swa = new NeonSwitch();
                swa.SetBounds(16, y, 46, 22);
                swa.SetCheckedSilent(!exclApps.Contains(c.Cle));   // ON = fermée pendant la partie
                swa.Tag = c.Cle;
                swa.CheckedChanged += (s, e) => SaveApps();
                _swApps[c.Cle] = swa;

                var nom = new Label();
                nom.SetBounds(70, y + 1, 534, 20);
                nom.Font = new Font("Segoe UI Semibold", 9f);
                nom.Text = c.Libelle;

                var pourquoi = new Label();
                pourquoi.SetBounds(70, y + 20, 534, 26);
                pourquoi.ForeColor = Theme.InkDimColor;
                pourquoi.Font = new Font("Segoe UI", 8f);
                pourquoi.Text = c.Pourquoi;

                Controls.AddRange(new Control[] { swa, nom, pourquoi });
                y += 46;
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
                     + " famille(s) d'applications fermée(s) pendant la partie (mémorisé).", 0);
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
