using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// PRÉRÉGLAGES CYBERPUNK 2077 — branchement : UserSettings.json du joueur (même chemin pour
    /// Steam, GOG et Epic), état dans bt-reglages-cyberpunk.txt (ce qu'ONYX a changé, valeur
    /// d'origine comprise), copie datée dans Documents\ONYX\Sauvegardes avant la première écriture.
    /// Mêmes refus que Fortnite : jeu ouvert, fichier absent ou en lecture seule.
    /// </summary>
    internal static partial class ReglagesCyberpunk
    {
        public const int SteamId = 1091500;
        private const string FichierEtat = "bt-reglages-cyberpunk.txt";
        private const string Processus = "Cyberpunk2077";
        private const string NomJeu = "Cyberpunk 2077";

        public static string Chemin
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CD Projekt Red", "Cyberpunk 2077", "UserSettings.json");
            }
        }

        /// <summary>Ce jeu de la bibliothèque est-il Cyberpunk 2077 (pas un DLC ni un outil) ?</summary>
        public static bool EstLeJeu(int steamId, string nom)
        {
            if (steamId == SteamId) return true;
            string n = (nom ?? "").Replace("™", "").Trim();
            return string.Equals(n, NomJeu, StringComparison.OrdinalIgnoreCase);
        }

        public static readonly IReglagesJeu Pilote = new PiloteCyberpunk();

        private sealed class PiloteCyberpunk : IReglagesJeu
        {
            public string Jeu { get { return NomJeu; } }
            public string Note
            {
                get
                {
                    return "Écrit dans UserSettings.json, le jeu fermé. Les valeurs sont une proposition d'ONYX, pas une mesure : "
                         + "« Mesurer en jeu » dit ce qu'elles valent sur ton PC. Jamais touchés : résolution, DLSS/FSR/XeSS, génération d'images, synchro verticale, limite d'images, Reflex.";
                }
            }
            public string FichierMesure { get { return "bt-reglages-cyberpunk-avant.txt"; } }
            public string NomMode(int m) { return ReglagesCyberpunk.NomMode((Mode)m); }
            public List<string> Apercu(int m, out List<string> absentes, out string raison)
            {
                absentes = new List<string>();
                Encoding enc;
                string json = FichierReglagesJeu.Lit(Chemin, NomJeu, out enc, out raison);
                if (json == null) return null;
                List<Changement> c = Prevoit(json, (Mode)m, out absentes);
                absentes = absentes.ConvertAll(Libelle);
                return c.ConvertAll(Ligne);
            }
            public string Applique(int m) { return ReglagesCyberpunk.Applique((Mode)m); }
            public string Retablit() { return ReglagesCyberpunk.Retablit(); }
            public bool ARetablir { get { return Etat().Count > 0; } }
        }

        public static List<Changement> Etat()
        {
            try { string p = AppPaths.File(FichierEtat); return File.Exists(p) ? Lire(File.ReadAllText(p, Encoding.UTF8)) : new List<Changement>(); }
            catch { return new List<Changement>(); }
        }

        public static string Applique(Mode m)
        {
            string refus = FichierReglagesJeu.Refus(Chemin, Processus, NomJeu);
            if (refus != null) return refus;
            Encoding enc; string raison;
            string json = FichierReglagesJeu.Lit(Chemin, NomJeu, out enc, out raison);
            if (json == null) return raison;
            List<string> absentes;
            List<Changement> c = Prevoit(json, m, out absentes);
            if (c.Count == 0) return "Rien à changer : le fichier est déjà réglé en « " + NomMode(m) + " ».";
            string nouveau = Applique(json, c);
            if (string.Equals(nouveau, json, StringComparison.Ordinal)) return "Rien n'a pu être écrit : format du fichier inattendu, il n'a pas été modifié.";
            List<Changement> etat = Etat();
            try
            {
                if (etat.Count == 0) FichierReglagesJeu.Sauvegarde(Chemin, "Cyberpunk2077-UserSettings");
                FichierReglagesJeu.Ecrit(Chemin, nouveau, enc);
                EcritEtat(Fusionne(etat, c));
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("ReglagesCyberpunk.Applique", ex);
                return "Écriture impossible, rien n'a changé : " + ex.Message;
            }
            var sb = new StringBuilder("« " + NomMode(m) + " » appliqué : " + c.Count + (c.Count > 1 ? " réglages écrits." : " réglage écrit."));
            foreach (Changement x in c) sb.Append("\r\n  ").Append(Ligne(x));
            if (absentes.Count > 0) sb.Append("\r\nAbsents ou inattendus dans le fichier, non touchés : ").Append(string.Join(", ", absentes.ConvertAll(Libelle))).Append('.');
            return sb.ToString();
        }

        public static string Retablit()
        {
            List<Changement> etat = Etat();
            if (etat.Count == 0) return "Rien à rétablir.";
            string refus = FichierReglagesJeu.Refus(Chemin, Processus, NomJeu);
            if (refus != null) return refus;
            Encoding enc; string raison;
            string json = FichierReglagesJeu.Lit(Chemin, NomJeu, out enc, out raison);
            if (json == null) return raison;
            string remis = Retablit(json, etat);
            Dictionary<string, string> v = Valeurs(json);
            int gardes = 0;
            foreach (Changement c in etat)
            {
                string actuel;
                if (!v.TryGetValue(Id(c.Section, c.Cle), out actuel) || !string.Equals(actuel, c.Apres, StringComparison.Ordinal)) gardes++;
            }
            try
            {
                if (!string.Equals(remis, json, StringComparison.Ordinal)) FichierReglagesJeu.Ecrit(Chemin, remis, enc);
                File.Delete(AppPaths.File(FichierEtat));
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("ReglagesCyberpunk.Retablit", ex);
                return "Écriture impossible, rien n'a changé : " + ex.Message;
            }
            int remises = etat.Count - gardes;
            return "Rétabli : " + remises + (remises > 1 ? " réglages remis" : " réglage remis") + " comme avant."
                + (gardes > 0 ? " " + gardes + (gardes > 1 ? " réglages changés" : " réglage changé") + " depuis dans le jeu : laissé" + (gardes > 1 ? "s" : "") + " tel quel." : "");
        }

        private static void EcritEtat(List<Changement> etat)
        {
            string p = AppPaths.File(FichierEtat);
            if (etat.Count == 0) { if (File.Exists(p)) File.Delete(p); return; }
            FichierReglagesJeu.Ecrit(p, Ecrire(etat), new UTF8Encoding(false));
        }
    }
}
