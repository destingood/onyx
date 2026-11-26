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

        public static bool JeuOuvert()
        {
            foreach (string n in VeilleJeu.NomsProcessus())
                if (string.Equals(n, Processus, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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

        private static string Refus()
        {
            if (JeuOuvert()) return "Ferme Fortnite : le jeu réécrit ce fichier en quittant.";
            try
            {
                if (!File.Exists(Chemin)) return "Fichier de réglages introuvable : lance Fortnite une fois.";
                if ((File.GetAttributes(Chemin) & FileAttributes.ReadOnly) != 0)
                    return "Le fichier de réglages est en lecture seule : ONYX n'y touche pas (retire l'attribut toi-même si tu veux l'utiliser).";
            }
            catch (Exception ex) { return "Fichier de réglages illisible : " + ex.Message; }
            return null;
        }

        /// <summary>Texte du fichier et son encodage d'origine (BOM compris) ; null + raison sinon.</summary>
        private static string LitIni(out Encoding enc, out string raison)
        {
            enc = new UTF8Encoding(false); raison = null;
            try
            {
                if (!File.Exists(Chemin)) { raison = "Fichier de réglages introuvable : lance Fortnite une fois."; return null; }
                byte[] b = File.ReadAllBytes(Chemin);
                int saut = 0;
                if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { enc = new UTF8Encoding(true); saut = 3; }
                else if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { enc = new UnicodeEncoding(false, true); saut = 2; }
                else if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { enc = new UnicodeEncoding(true, true); saut = 2; }
                return enc.GetString(b, saut, b.Length - saut);
            }
            catch (Exception ex) { raison = "Fichier de réglages illisible : " + ex.Message; return null; }
        }

        private static void Ecrit(string chemin, string texte, Encoding enc)
        {
            string tmp = chemin + ".onyx.tmp";
            File.WriteAllText(tmp, texte, enc);   // l'encodage porte son BOM d'origine (ou aucun)
            File.Move(tmp, chemin, true);
        }

        private static void EcritEtat(List<Changement> etat)
        {
            string p = AppPaths.File(FichierEtat);
            if (etat.Count == 0) { if (File.Exists(p)) File.Delete(p); return; }
            Ecrit(p, Ecrire(etat), new UTF8Encoding(false));
        }

        private static void Sauvegarde()
        {
            string dossier = Sauvegardes.Dossier(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            Directory.CreateDirectory(dossier);
            File.Copy(Chemin, Path.Combine(dossier, "Fortnite-GameUserSettings-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".ini"), false);
        }
    }
}
