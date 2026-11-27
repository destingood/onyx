using System;
using System.Collections.Generic;
using System.Globalization;

namespace BTOptimizer
{
    /// <summary>
    /// COMPTEUR EN JEU — PUR. Ce que l'utilisateur choisit d'afficher par-dessus le jeu, à quelle
    /// taille, à quel endroit et dans quelle couleur, plus les textes des mesures. La fenêtre
    /// (StatsOverlay.cs) ne fait que dessiner ce qui est décidé ici.
    /// Les ventilateurs n'y figurent pas : les lire demande un pilote noyau, qu'ONYX n'installe pas.
    /// </summary>
    internal static class CompteurJeu
    {
        // Identifiants des lignes, dans l'ORDRE d'affichage.
        public const string Bas1 = "low", Cpu = "cpu", CpuTemp = "cputemp", Gpu = "gpu", GpuTemp = "gputemp",
                            Vram = "vram", Watts = "gpuw", Ram = "ram", Reseau = "net", Heure = "heure",
                            Duree = "duree", Courbe = "courbe";

        public static readonly string[] Toutes = { Bas1, Cpu, CpuTemp, Gpu, GpuTemp, Vram, Watts, Ram, Reseau, Heure, Duree, Courbe };

        /// <summary>Le libellé de chaque case à cocher des réglages.</summary>
        public static string Libelle(string id)
        {
            switch (id)
            {
                case Bas1: return "Images les plus lentes (1% low)";
                case Cpu: return "Processeur (charge)";
                case CpuTemp: return "Température du processeur";
                case Gpu: return "Carte graphique (charge)";
                case GpuTemp: return "Température de la carte graphique";
                case Vram: return "Mémoire vidéo utilisée";
                case Watts: return "Puissance de la carte graphique (W)";
                case Ram: return "Mémoire (RAM)";
                case Reseau: return "Débit réseau (réception et envoi)";
                case Heure: return "Heure";
                case Duree: return "Durée de la partie";
                case Courbe: return "Courbe des FPS";
                default: return id;
            }
        }

        public const int NbPositions = 6;   // 0 haut-gauche · 1 haut-droit · 2 bas-gauche · 3 bas-droit · 4 haut-centre · 5 bas-centre
        public static readonly string[] Positions = { "Haut gauche", "Haut droit", "Bas gauche", "Bas droit", "Haut centre", "Bas centre" };
        public static readonly string[] Tailles = { "Petit", "Moyen", "Grand" };
        public static readonly string[] Couleurs = { "Automatique (vert, orange, rouge selon la valeur)", "Blanc", "Or", "Vert", "Bleu clair" };

        public sealed class Options
        {
            public bool Actif;
            public int Position = 1;
            public int Taille = 1;
            public int Couleur;
            /// <summary>Par défaut : ce que le compteur affichait avant d'être réglable.</summary>
            public HashSet<string> Lignes = new HashSet<string>(StringComparer.Ordinal) { Bas1, Cpu, CpuTemp, Gpu, GpuTemp, Ram, Courbe };

            public bool A(string id) { return Lignes.Contains(id); }
        }

        /// <summary>
        /// « 1;1 » (ancien format : actif ; coin) ou « 1;1;2;0;cpu,gpu,ram ». Un fichier ancien garde
        /// l'affichage d'avant. Une valeur hors bornes revient à la valeur par défaut.
        /// </summary>
        public static Options Lit(string texte)
        {
            var o = new Options();
            if (string.IsNullOrWhiteSpace(texte)) return o;
            string[] p = texte.Trim().Split(';');
            o.Actif = p[0].Trim() == "1";
            int v;
            if (p.Length > 1 && int.TryParse(p[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out v) && v < NbPositions) o.Position = v;
            if (p.Length > 2 && int.TryParse(p[2].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out v) && v < Tailles.Length) o.Taille = v;
            if (p.Length > 3 && int.TryParse(p[3].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out v) && v < Couleurs.Length) o.Couleur = v;
            if (p.Length > 4)
            {
                o.Lignes.Clear();
                foreach (string brut in p[4].Split(','))
                {
                    string id = brut.Trim();
                    if (Array.IndexOf(Toutes, id) >= 0) o.Lignes.Add(id);
                }
            }
            return o;
        }

        public static string Ecrit(Options o)
        {
            var l = new List<string>();
            foreach (string id in Toutes) if (o.Lignes.Contains(id)) l.Add(id);   // ordre stable
            return (o.Actif ? "1" : "0") + ";" + o.Position.ToString(CultureInfo.InvariantCulture) + ";"
                 + o.Taille.ToString(CultureInfo.InvariantCulture) + ";" + o.Couleur.ToString(CultureInfo.InvariantCulture) + ";"
                 + string.Join(",", l);
        }

        /// <summary>Facteur d'échelle : petit 0,85 · moyen 1 · grand 1,3.</summary>
        public static double Echelle(int taille) { return taille == 0 ? 0.85 : taille == 2 ? 1.3 : 1.0; }

        /// <summary>
        /// Nombre de lignes sous les FPS. La température n'a pas de ligne à elle : elle s'ajoute à
        /// droite de la charge ; seule, elle occupe la ligne.
        /// </summary>
        public static int NbLignes(Options o)
        {
            int n = 0;
            if (o.A(Cpu) || o.A(CpuTemp)) n++;
            if (o.A(Gpu) || o.A(GpuTemp)) n++;
            foreach (string id in new[] { Vram, Watts, Ram, Reseau, Heure, Duree }) if (o.A(id)) n++;
            return n;
        }

        /// <summary>Taille de la fenêtre à l'échelle 1 : 222 × (72 + 28 par ligne + 22 pour la courbe, sinon 6).</summary>
        public static void Dimensions(Options o, out int largeur, out int hauteur)
        {
            double k = Echelle(o.Taille);
            largeur = (int)Math.Round(222 * k);
            hauteur = (int)Math.Round((72 + 28 * NbLignes(o) + (o.A(Courbe) ? 22 : 6)) * k);
        }

        /// <summary>Coin haut-gauche de la fenêtre dans la zone de travail de l'écran.</summary>
        public static void Place(int position, int zx, int zy, int zl, int zh, int largeur, int hauteur, out int x, out int y)
        {
            const int marge = 16;
            bool droite = position == 1 || position == 3, centre = position == 4 || position == 5;
            bool bas = position == 2 || position == 3 || position == 5;
            x = centre ? zx + (zl - largeur) / 2 : droite ? zx + zl - largeur - marge : zx + marge;
            y = bas ? zy + zh - hauteur - marge : zy + marge;
        }

        // ------------------------------------------------------------------ Textes des mesures

        /// <summary>Débit en bits par seconde : « 850 kb/s », « 12,4 Mb/s », « 1,2 Gb/s ».</summary>
        public static string TexteDebit(double bitsParSeconde)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            if (double.IsNaN(bitsParSeconde) || bitsParSeconde < 0) return "—";
            if (bitsParSeconde < 1e6) return Math.Round(bitsParSeconde / 1e3).ToString("0", fr) + " kb/s";
            if (bitsParSeconde < 1e9) return (bitsParSeconde / 1e6).ToString(bitsParSeconde < 1e8 ? "0.0" : "0", fr) + " Mb/s";
            return (bitsParSeconde / 1e9).ToString("0.0", fr) + " Gb/s";
        }

        /// <summary>
        /// Débit entre deux relevés du compteur d'octets de la carte réseau. Un compteur qui recule
        /// (carte réinitialisée, sortie de veille) ne donne pas de mesure plutôt qu'un débit absurde.
        /// </summary>
        public static double Debit(long octetsAvant, long octetsApres, double secondes)
        {
            if (secondes <= 0 || octetsAvant < 0 || octetsApres < octetsAvant) return double.NaN;
            return (octetsApres - octetsAvant) * 8.0 / secondes;
        }

        /// <summary>« 5,2 / 12 Go ». Total inconnu : « 5,2 Go ».</summary>
        public static string TexteVram(long utiliseMo, long totalMo)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            if (utiliseMo <= 0) return "—";
            string u = (utiliseMo / 1024.0).ToString("0.0", fr);
            if (totalMo <= 0) return u + " Go";
            return u + " / " + Math.Round(totalMo / 1024.0).ToString("0", fr) + " Go";
        }

        /// <summary>« 42 min », « 1 h 05 ».</summary>
        public static string TexteDuree(TimeSpan d)
        {
            if (d < TimeSpan.Zero) d = TimeSpan.Zero;
            int h = (int)d.TotalHours;
            return h == 0 ? d.Minutes.ToString(CultureInfo.InvariantCulture) + " min"
                          : h.ToString(CultureInfo.InvariantCulture) + " h " + d.Minutes.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Durée de la partie : part quand un jeu apparaît au premier plan, repart de zéro quand un
        /// AUTRE jeu apparaît. Un Alt+Tab (plus de jeu devant) ne l'arrête pas ; au-delà de cinq
        /// minutes sans jeu, la partie est considérée finie.
        /// </summary>
        public sealed class Chrono
        {
            private string _jeu;
            private DateTime _debut, _vu;
            public static readonly TimeSpan Oubli = TimeSpan.FromMinutes(5);

            public TimeSpan? Marque(string jeu, DateTime maintenant)
            {
                if (!string.IsNullOrEmpty(jeu))
                {
                    if (!string.Equals(jeu, _jeu, StringComparison.OrdinalIgnoreCase)) { _jeu = jeu; _debut = maintenant; }
                    _vu = maintenant;
                }
                else if (_jeu != null && maintenant - _vu > Oubli) _jeu = null;
                return _jeu == null ? (TimeSpan?)null : maintenant - _debut;
            }
        }
    }
}
