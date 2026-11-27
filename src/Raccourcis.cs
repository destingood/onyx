using System;
using System.Collections.Generic;
using System.Globalization;

namespace BTOptimizer
{
    /// <summary>
    /// RACCOURCIS CLAVIER GLOBAUX (logique PURE) : quatre actions, une combinaison chacune,
    /// modifiables et désactivables, gardées dans bt-raccourcis.txt (« modejeu=Ctrl+Alt+G »).
    /// Une combinaison doit avoir Ctrl ou Alt : une touche seule, ou Maj seule, volerait la frappe
    /// dans le jeu. Deux actions ne peuvent pas partager la même combinaison.
    /// </summary>
    internal static class Raccourcis
    {
        public enum Action { ModeJeu, Afficher, Compteur, Capture }

        public const uint ModAlt = 0x1, ModCtrl = 0x2, ModMaj = 0x4;

        public sealed class Combo
        {
            public uint Mods;
            /// <summary>Code de touche virtuelle Windows (A–Z, 0–9, F1–F12) ; 0 = désactivé.</summary>
            public int Touche;
            public bool Vide { get { return Touche == 0; } }
            public bool MemeQue(Combo o) { return o != null && !Vide && !o.Vide && Mods == o.Mods && Touche == o.Touche; }
        }

        public static readonly string[] Codes = { "modejeu", "afficher", "compteur", "capture" };
        public static readonly string[] Libelles = { "Mode Jeu (activer / couper)", "Afficher ou ranger ONYX", "Compteur en jeu", "Capture d'écran du jeu" };
        private static readonly char[] Defauts = { 'G', 'O', 'F', 'C' };

        public static int Nombre { get { return Codes.Length; } }

        public static Combo Defaut(Action a) { return new Combo { Mods = ModCtrl | ModAlt, Touche = Defauts[(int)a] }; }

        public static Combo[] TousParDefaut()
        {
            var t = new Combo[Nombre];
            for (int i = 0; i < Nombre; i++) t[i] = Defaut((Action)i);
            return t;
        }

        /// <summary>Touche acceptée : lettre, chiffre ou F1–F12.</summary>
        public static bool ToucheValide(int vk)
        {
            return (vk >= 'A' && vk <= 'Z') || (vk >= '0' && vk <= '9') || (vk >= 0x70 && vk <= 0x7B);
        }

        /// <summary>null si la combinaison convient, sinon la raison en clair.</summary>
        public static string Refus(Combo c)
        {
            if (c == null || c.Vide) return null;
            if (!ToucheValide(c.Touche)) return "Choisis une lettre, un chiffre ou une touche F1 à F12.";
            bool fonction = c.Touche >= 0x70 && c.Touche <= 0x7B;
            if ((c.Mods & (ModCtrl | ModAlt)) == 0 && !fonction) return "Ajoute Ctrl ou Alt : une touche seule servirait aussi dans le jeu.";
            if ((c.Mods & (ModCtrl | ModAlt)) == 0 && fonction) return "Ajoute Ctrl ou Alt : les touches F servent dans beaucoup de jeux.";
            if (c.Mods == ModAlt && c.Touche == 0x73) return "Alt+F4 ferme la fenêtre active.";
            return null;
        }

        /// <summary>Index de la première action qui a déjà cette combinaison (hors <paramref name="sauf"/>), sinon -1.</summary>
        public static int Conflit(Combo[] tous, Combo c, int sauf)
        {
            if (tous == null || c == null || c.Vide) return -1;
            for (int i = 0; i < tous.Length; i++)
                if (i != sauf && c.MemeQue(tous[i])) return i;
            return -1;
        }

        public static string Texte(Combo c)
        {
            if (c == null || c.Vide) return "Désactivé";
            var p = new List<string>();
            if ((c.Mods & ModCtrl) != 0) p.Add("Ctrl");
            if ((c.Mods & ModAlt) != 0) p.Add("Alt");
            if ((c.Mods & ModMaj) != 0) p.Add("Maj");
            p.Add(NomTouche(c.Touche));
            return string.Join("+", p);
        }

        public static string NomTouche(int vk)
        {
            if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x6F).ToString(CultureInfo.InvariantCulture);
            if ((vk >= 'A' && vk <= 'Z') || (vk >= '0' && vk <= '9')) return ((char)vk).ToString();
            return "?";
        }

        /// <summary>Lit « Ctrl+Alt+G », « Alt+F9 », « désactivé ». Texte illisible : null.</summary>
        public static Combo Lit(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return null;
            if (string.Equals(s, "désactivé", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "aucun", StringComparison.OrdinalIgnoreCase)) return new Combo();
            var c = new Combo();
            string[] p = s.Split('+');
            for (int i = 0; i < p.Length; i++)
            {
                string t = p[i].Trim();
                if (i < p.Length - 1)
                {
                    if (t.Equals("ctrl", StringComparison.OrdinalIgnoreCase)) c.Mods |= ModCtrl;
                    else if (t.Equals("alt", StringComparison.OrdinalIgnoreCase)) c.Mods |= ModAlt;
                    else if (t.Equals("maj", StringComparison.OrdinalIgnoreCase) || t.Equals("shift", StringComparison.OrdinalIgnoreCase)) c.Mods |= ModMaj;
                    else return null;
                    continue;
                }
                t = t.ToUpperInvariant();
                int n;
                if (t.Length == 1 && ToucheValide(t[0])) c.Touche = t[0];
                else if (t.Length >= 2 && t[0] == 'F' && int.TryParse(t.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= 1 && n <= 12) c.Touche = 0x6F + n;
                else return null;
            }
            return c;
        }

        /// <summary>Lit le fichier : les lignes inconnues, illisibles, refusées ou en conflit gardent le défaut.</summary>
        public static Combo[] LitFichier(string texte)
        {
            Combo[] t = TousParDefaut();
            foreach (string ligne in (texte ?? "").Split('\n'))
            {
                int eg = ligne.IndexOf('=');
                if (eg <= 0) continue;
                int i = Array.IndexOf(Codes, ligne.Substring(0, eg).Trim().ToLowerInvariant());
                if (i < 0) continue;
                Combo c = Lit(ligne.Substring(eg + 1));
                if (c == null || Refus(c) != null) continue;
                t[i] = c;
            }
            // Conflit dans le fichier : la seconde action revient à son défaut, ou se désactive.
            for (int i = 0; i < t.Length; i++)
                if (Conflit(t, t[i], i) >= 0 && Conflit(t, t[i], i) < i)
                {
                    Combo d = Defaut((Action)i);
                    t[i] = Conflit(t, d, i) < 0 ? d : new Combo();
                }
            return t;
        }

        public static string EcritFichier(Combo[] t)
        {
            var l = new List<string>();
            for (int i = 0; i < Nombre; i++)
                l.Add(Codes[i] + "=" + Texte(t != null && i < t.Length ? t[i] : Defaut((Action)i)).Replace("Désactivé", "désactivé"));
            return string.Join("\n", l) + "\n";
        }
    }
}
