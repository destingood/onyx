using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// LES CURSEURS D'AFTERBURNER, DANS ONYX. Quatre réglages, dans l'ordre où ils comptent :
    /// limite de puissance, limite de température, décalage cœur, décalage mémoire.
    ///
    /// Les bornes de chaque curseur sont LUES SUR LA CARTE, jamais codées en dur : une carte qui
    /// n'accepte pas un réglage n'en affiche pas le curseur, avec la raison écrite à côté. C'est la
    /// différence entre un panneau qui promet et un panneau qui dit ce que le matériel peut faire.
    ///
    /// Rien n'est appliqué en ouvrant la fenêtre ni en bougeant un curseur : il faut cliquer
    /// « Appliquer ». Et rien n'est réappliqué au démarrage — les décalages disparaissent au
    /// redémarrage, ce qui est la meilleure porte de sortie quand un réglage rend la machine
    /// instable.
    /// </summary>
    internal class GpuTuningForm : Form
    {
        private readonly Action<string, int> _log;
        private readonly List<Font> _fonts = new List<Font>();

        private GpuLimits.Etat _lim;
        private NvOverclock.Etat _oc;

        private TrackBar _puissance, _temp, _coeur, _memoire;
        private Label _lPuissance, _lTemp, _lCoeur, _lMemoire, _etat;
        private Panel _corps;

        private Font Own(Font f) { _fonts.Add(f); return f; }

        public GpuTuningForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Load += (s, e) => Recharge();
        }

        private void Build()
        {
            Text = "ONYX — Réglages carte graphique";
            ClientSize = new Size(680, 620);
            MinimumSize = new Size(600, 540);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = Own(new Font("Segoe UI", 9f));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Réglages carte graphique", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = Own(new Font("Segoe UI Semibold", 12f)), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var bas = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(14, 12, 14, 12) };
            var appliquer = new Button { Text = "Appliquer", Dock = DockStyle.Right, Width = 130, Height = 32 };
            appliquer.Click += (s, e) => Applique();
            var usine = new Button { Text = "Tout remettre d'usine", Dock = DockStyle.Left, Width = 180, Height = 32 };
            usine.Click += (s, e) => Usine();
            bas.Controls.Add(appliquer);
            bas.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 10 });
            bas.Controls.Add(usine);
            Controls.Add(bas);

            _corps = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16, 14, 16, 8) };
            Controls.Add(_corps);
            _corps.BringToFront();
        }

        // ------------------------------------------------------------------ construction des curseurs

        private int _y;

        private void Titre(string texte, string explication)
        {
            _corps.Controls.Add(new Label
            {
                Text = texte, Left = 0, Top = _y, Width = 620, Height = 20,
                Font = Own(new Font("Segoe UI Semibold", 10f))
            });
            _y += 21;
            var l = new Label
            {
                Text = explication, Left = 0, Top = _y, Width = 620, AutoSize = false,
                Height = 34, ForeColor = Color.FromArgb(90, 95, 105)
            };
            _corps.Controls.Add(l);
            _y += 36;
        }

        /// <summary>Un curseur avec ses bornes réelles et sa valeur lisible à droite.</summary>
        private TrackBar Curseur(int min, int max, int valeur, out Label lecture)
        {
            var t = new TrackBar
            {
                Left = 0, Top = _y, Width = 480, Minimum = min, Maximum = max,
                Value = Math.Max(min, Math.Min(max, valeur)),
                TickStyle = TickStyle.None, LargeChange = Math.Max(1, (max - min) / 10)
            };
            lecture = new Label
            {
                Left = 492, Top = _y + 8, Width = 140, Height = 22,
                Font = Own(new Font("Segoe UI Semibold", 10f))
            };
            _corps.Controls.Add(t);
            _corps.Controls.Add(lecture);
            _y += 46;
            return t;
        }

        private void Indisponible(string raison)
        {
            _corps.Controls.Add(new Label
            {
                Text = "Indisponible sur cette carte — " + raison, Left = 0, Top = _y,
                Width = 620, Height = 34, ForeColor = Color.FromArgb(150, 90, 30)
            });
            _y += 40;
        }

        // ------------------------------------------------------------------ état

        private void Recharge()
        {
            _corps.Controls.Clear();
            _y = 0;
            _puissance = _temp = _coeur = _memoire = null;

            _lim = GpuLimits.Lire();
            _oc = NvOverclock.Lire();

            _etat = new Label
            {
                Left = 0, Top = _y, Width = 630, Height = 36,
                ForeColor = Color.FromArgb(60, 65, 75),
                Text = "Rien n'est appliqué tant que tu n'as pas cliqué « Appliquer ». Les décalages de "
                     + "fréquence disparaissent au redémarrage."
            };
            _corps.Controls.Add(_etat);
            _y += 44;

            // 1) LIMITE DE PUISSANCE — le plafond qui décide vraiment de la fréquence tenue.
            Titre("Limite de puissance",
                "La carte monte en fréquence tant qu'elle a du budget. Élargir ce plafond n'ajoute pas "
                + "de performance si elle ne l'atteint jamais — mais le retire comme frein si elle le touche.");
            if (_lim.Lu)
            {
                int pmin = GpuLimits.Pourcent(_lim.MinW, _lim.DefautW);
                int pmax = GpuLimits.Pourcent(_lim.MaxW, _lim.DefautW);
                _puissance = Curseur(pmin, pmax, _lim.Pourcent, out _lPuissance);
                _puissance.Scroll += (s, e) => MajPuissance();
                MajPuissance();
            }
            else Indisponible("les limites de puissance n'ont pas pu être lues.");

            // 2) LIMITE DE TEMPÉRATURE
            Titre("Limite de température",
                "Au-delà, la carte réduit sa fréquence pour se protéger. La monter garde la fréquence "
                + "plus longtemps ; ça se paie en chaleur et en bruit.");
            if (_lim.TempCibleC > 0)
            {
                int tmax = _lim.TempMaxC > 0 ? _lim.TempMaxC : 90;
                _temp = Curseur(60, tmax, _lim.TempCibleC, out _lTemp);
                _temp.Scroll += (s, e) => MajTemp();
                MajTemp();
            }
            else Indisponible("la limite de température n'est pas exposée.");

            // 3-4) DÉCALAGES DE FRÉQUENCE
            Titre("Décalage de fréquence du cœur",
                "Décale toute la courbe vers le haut. C'est le seul réglage qui fait réellement gagner "
                + "des images — et le seul qui peut rendre la machine instable. Monte par paliers de 15 MHz.");
            if (_oc.Disponible && _oc.CoeurMax > 0)
            {
                _coeur = Curseur(_oc.CoeurMin, _oc.CoeurMax, _oc.CoeurMhz, out _lCoeur);
                _coeur.Scroll += (s, e) => MajCoeur();
                MajCoeur();
            }
            else Indisponible(_oc.Motif ?? "le pilote n'expose pas de plage.");

            Titre("Décalage de fréquence de la mémoire",
                "Même principe pour la mémoire vidéo. Elle encaisse en général davantage que le cœur, "
                + "mais ses erreurs sont silencieuses : elles se corrigent toutes seules en coûtant du débit.");
            if (_oc.Disponible && _oc.MemoireMax > 0)
            {
                _memoire = Curseur(_oc.MemoireMin, _oc.MemoireMax, _oc.MemoireMhz, out _lMemoire);
                _memoire.Scroll += (s, e) => MajMemoire();
                MajMemoire();
            }
            else Indisponible(_oc.Motif ?? "le pilote n'expose pas de plage.");

            // La tension : dite indisponible SEULEMENT parce que la carte l'a dit.
            if (!_oc.TensionExposee)
            {
                Titre("Tension (+mV)",
                    "Non proposée : cette carte n'expose aucun point de tension modifiable par le pilote. "
                    + "Un curseur qui ne fait rien serait pire que pas de curseur.");
            }
        }

        private void MajPuissance()
        {
            int w = GpuLimits.Watts(_puissance.Value, _lim.DefautW, _lim.MinW, _lim.MaxW);
            _lPuissance.Text = _puissance.Value + " %  •  " + w + " W";
        }

        private void MajTemp() { _lTemp.Text = _temp.Value + " °C"; }
        private void MajCoeur() { _lCoeur.Text = NvOverclock.Signe(_coeur.Value) + " MHz"; }
        private void MajMemoire() { _lMemoire.Text = NvOverclock.Signe(_memoire.Value) + " MHz"; }

        // ------------------------------------------------------------------ actions

        private void Applique()
        {
            bool oc = (_coeur != null && _coeur.Value != _oc.CoeurMhz)
                   || (_memoire != null && _memoire.Value != _oc.MemoireMhz);

            // On ne demande confirmation QUE pour ce qui peut réellement casser quelque chose.
            if (oc && MessageBox.Show(this,
                    "Appliquer un décalage de fréquence ?\n\n"
                    + "• Trop haut, ça produit des artefacts à l'écran, fait planter le pilote, ou ferme le jeu.\n"
                    + "• Le décalage DISPARAÎT au redémarrage : si la machine devient instable, redémarre.\n"
                    + "• Ferme Afterburner ou Precision X1 avant : deux outils ne peuvent pas piloter la courbe "
                    + "en même temps.",
                    "Décalage de fréquence", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            int faits = 0, echecs = 0;

            if (_puissance != null && _puissance.Value != _lim.Pourcent)
            {
                int w = GpuLimits.Watts(_puissance.Value, _lim.DefautW, _lim.MinW, _lim.MaxW);
                if (GpuLimits.AppliquerPuissance(w, _log)) faits++; else echecs++;
            }
            if (_temp != null && _temp.Value != _lim.TempCibleC)
            {
                if (GpuLimits.AppliquerTemp(_temp.Value, _log)) faits++; else echecs++;
            }
            if (oc)
            {
                int c = _coeur != null ? _coeur.Value : _oc.CoeurMhz;
                int m = _memoire != null ? _memoire.Value : _oc.MemoireMhz;
                if (NvOverclock.Appliquer(c, m, _log)) faits++; else echecs++;
            }

            if (faits == 0 && echecs == 0)
            {
                MessageBox.Show(this, "Aucun curseur n'a bougé : rien n'a été envoyé à la carte.",
                    "Réglages carte graphique", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // La vérité vient d'une RELECTURE de la carte, pas de ce qu'on croit avoir écrit.
            Recharge();
            MessageBox.Show(this,
                echecs == 0
                    ? faits + " réglage(s) appliqué(s). Les valeurs affichées ont été relues sur la carte."
                    : faits + " réglage(s) appliqué(s), " + echecs + " refusé(s) par le pilote. "
                      + "Le détail est dans le journal d'ONYX.",
                "Réglages carte graphique", MessageBoxButtons.OK,
                echecs == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void Usine()
        {
            if (MessageBox.Show(this,
                    "Remettre la puissance et les décalages de fréquence à leurs valeurs d'usine ?\n\n"
                    + "La limite de température n'a pas de valeur d'usine interrogeable : elle n'est pas touchée.",
                    "Remise d'usine", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            if (_lim.Lu) GpuLimits.ReinitialiserPuissance(_log);
            if (_oc.Disponible) NvOverclock.Reinitialiser(_log);
            Recharge();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var f in _fonts) { try { f.Dispose(); } catch { } }
            base.Dispose(disposing);
        }
    }
}
