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

        public BoostConfigForm(Action<string, int> log)
        {
            _log = log;
            BuildUi();
            Theme.Apply(this);
        }

        private void BuildUi()
        {
            Text = "Mode jeu — services coupés & exclusions — DesTinGOOD";
            ClientSize = new Size(560, 128 + GameBoost.AffectedServices.Length * 30);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            var intro = new Label();
            intro.SetBounds(16, 10, 528, 48);
            intro.Text = "Pendant une partie, le MODE JEU suspend ces services de fond (ils sont relancés à la "
                       + "sortie du jeu). Interrupteur OFF = exclu : le service n'est jamais touché. "
                       + "Choix mémorisé, appliqué à la prochaine activation.";
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
                desc.SetBounds(222, y + 2, 322, 20);
                desc.ForeColor = Theme.InkDimColor;
                string d;
                desc.Text = Explain.TryGetValue(svc, out d) ? d : "Service de fond";

                Controls.AddRange(new Control[] { sw, name, desc });
                y += 30;
            }

            var note = new Label();
            note.SetBounds(16, y + 6, 528, 34);
            note.ForeColor = Theme.InkDimColor;
            note.Font = new Font("Segoe UI", 8f);
            note.Text = "Exemple : tu imprimes pendant que tu joues ? Mets « Spooler » sur OFF. "
                      + "Un service déjà désactivé par tes optimisations n'est de toute façon jamais touché.";

            Controls.AddRange(new Control[] { intro, note });
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
