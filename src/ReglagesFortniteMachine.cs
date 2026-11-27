using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// PRÉRÉGLAGES FORTNITE — branchement : GameUserSettings.ini du joueur, état dans
    /// bt-reglages-fortnite.txt (ce qu'ONYX a changé, valeur d'origine comprise), copie datée du
    /// fichier dans Documents\ONYX\Sauvegardes avant la première écriture. Refus si le jeu tourne
    /// (il réécrit le fichier en quittant) ou si le fichier est en lecture seule (choix du joueur :
    /// ONYX ne retire pas l'attribut). Rend toujours un compte-rendu de ce qui a été réellement écrit.
    /// </summary>
    internal static partial class ReglagesFortnite
    {
        private const string FichierEtat = "bt-reglages-fortnite.txt";
        private const string Processus = "FortniteClient-Win64-Shipping";

        public static string Chemin
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FortniteGame", "Saved", "Config", "WindowsClient", "GameUserSettings.ini");
            }
        }

        public static bool JeuOuvert() { return FichierReglagesJeu.JeuOuvert(Processus); }

        /// <summary>Ce que la fenêtre des préréglages utilise.</summary>
        public static readonly IReglagesJeu Pilote = new PiloteFortnite();

        private sealed class PiloteFortnite : IReglagesJeu
        {
            public string Jeu { get { return "Fortnite"; } }
            public string Note
            {
                get
                {
                    return "Écrit dans GameUserSettings.ini, Fortnite fermé. Les valeurs sont une proposition d'ONYX, pas une mesure : "
                         + "« Mesurer en jeu » dit ce qu'elles valent sur ton PC. Jamais touchés : résolution, limite d'images, plein écran, moteur de rendu, DLSS/TSR.";
                }
            }
            public string FichierMesure { get { return "bt-reglages-fortnite-avant.txt"; } }
            public string NomMode(int m) { return ReglagesFortnite.NomMode((Mode)m); }
            public List<string> Apercu(int m, out List<string> absentes, out string raison)
            {
                List<Changement> c = ReglagesFortnite.Apercu((Mode)m, out absentes, out raison);
                absentes = absentes.ConvertAll(Libelle);
                return c == null ? null : c.ConvertAll(Ligne);
            }
            public string Applique(int m) { return ReglagesFortnite.Applique((Mode)m); }
            public string Retablit() { return ReglagesFortnite.Retablit(); }
            public bool ARetablir { get { return Etat().Count > 0; } }
            public bool Situation(out int? mode, out int limite, out bool vsync)
            {
                mode = null; limite = 0; vsync = false;
                Encoding enc; string raison;
                string ini = LitIni(out enc, out raison);
                if (ini == null) return false;
                var n = new List<int>(); List<string> absentes;
                for (int m = 0; m < 4; m++) n.Add(Prevoit(ini, (Mode)m, out absentes).Count);
                mode = ObjectifFps.ModeActuel(n);
                limite = LimiteImages(ini, out vsync);
                return true;
            }
        }

        /// <summary>Changements enregistrés par le dernier préréglage (vide : rien à rétablir).</summary>
        public static List<Changement> Etat()
        {
            try { string p = AppPaths.File(FichierEtat); return File.Exists(p) ? Lire(File.ReadAllText(p, Encoding.UTF8)) : new List<Changement>(); }
            catch { return new List<Changement>(); }
        }

        /// <summary>Aperçu sans rien écrire ; null + raison si le fichier est illisible.</summary>
        public static List<Changement> Apercu(Mode m, out List<string> absentes, out string raison)
        {
            absentes = new List<string>();
            Encoding enc;
            string ini = LitIni(out enc, out raison);
            return ini == null ? null : Prevoit(ini, m, out absentes);
        }

        public static string Applique(Mode m)
        {
            string refus = Refus();
            if (refus != null) return refus;
            Encoding enc; string raison;
            string ini = LitIni(out enc, out raison);
            if (ini == null) return raison;
            List<string> absentes;
            List<Changement> c = Prevoit(ini, m, out absentes);
            if (c.Count == 0) return "Rien à changer : le fichier est déjà réglé en « " + NomMode(m) + " ».";
            List<Changement> etat = Etat();
            try
            {
                if (etat.Count == 0) Sauvegarde();
                Ecrit(Chemin, Applique(ini, c), enc);
                EcritEtat(Fusionne(etat, c));
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("ReglagesFortnite.Applique", ex);
                return "Écriture impossible, rien n'a changé : " + ex.Message;
            }
            var sb = new StringBuilder("« " + NomMode(m) + " » appliqué : " + c.Count + (c.Count > 1 ? " réglages écrits." : " réglage écrit."));
            foreach (Changement x in c) sb.Append("\r\n  ").Append(Ligne(x));
            if (absentes.Count > 0) sb.Append("\r\nAbsents du fichier, non ajoutés : ").Append(string.Join(", ", absentes.ConvertAll(Libelle))).Append('.');
            return sb.ToString();
        }

        public static string Retablit()
        {
            List<Changement> etat = Etat();
            if (etat.Count == 0) return "Rien à rétablir.";
            string refus = Refus();
            if (refus != null) return refus;
            Encoding enc; string raison;
            string ini = LitIni(out enc, out raison);
            if (ini == null) return raison;
            string remis = Retablit(ini, etat);
            int gardes = 0;
            Dictionary<string, string> v = Valeurs(ini);
            foreach (Changement c in etat)
            {
                string actuel;
                if (!v.TryGetValue(Id(c.Section, c.Cle), out actuel) || !Pareil(actuel, c.Apres)) gardes++;
            }
            try
            {
                if (!string.Equals(remis, ini, StringComparison.Ordinal)) Ecrit(Chemin, remis, enc);
                File.Delete(AppPaths.File(FichierEtat));
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("ReglagesFortnite.Retablit", ex);
                return "Écriture impossible, rien n'a changé : " + ex.Message;
            }
            int remises = etat.Count - gardes;
            return "Rétabli : " + remises + (remises > 1 ? " réglages remis" : " réglage remis") + " comme avant."
                + (gardes > 0 ? " " + gardes + (gardes > 1 ? " réglages changés" : " réglage changé") + " depuis dans le jeu : laissé" + (gardes > 1 ? "s" : "") + " tel quel." : "");
        }

        // ------------------------------------------------------------------ fichier

        private static string Refus() { return FichierReglagesJeu.Refus(Chemin, Processus, "Fortnite"); }

        private static string LitIni(out Encoding enc, out string raison) { return FichierReglagesJeu.Lit(Chemin, "Fortnite", out enc, out raison); }

        private static void Ecrit(string chemin, string texte, Encoding enc) { FichierReglagesJeu.Ecrit(chemin, texte, enc); }

        private static void EcritEtat(List<Changement> etat)
        {
            string p = AppPaths.File(FichierEtat);
            if (etat.Count == 0) { if (File.Exists(p)) File.Delete(p); return; }
            Ecrit(p, Ecrire(etat), new UTF8Encoding(false));
        }

        private static void Sauvegarde() { FichierReglagesJeu.Sauvegarde(Chemin, "Fortnite-GameUserSettings"); }
    }
}
