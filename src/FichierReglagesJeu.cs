using System;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Fichier de réglages d'un jeu (Fortnite, Cyberpunk 2077) : mêmes règles pour tous. Refus si
    /// le jeu tourne (il réécrit le fichier en quittant), si le fichier manque ou s'il est en
    /// lecture seule (choix du joueur : ONYX ne retire pas l'attribut). Encodage d'origine gardé,
    /// écriture par fichier temporaire puis remplacement, copie datée dans Documents\ONYX\Sauvegardes.
    /// </summary>
    internal static class FichierReglagesJeu
    {
        public static bool JeuOuvert(string processus)
        {
            foreach (string n in VeilleJeu.NomsProcessus())
                if (string.Equals(n, processus, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Raison de ne pas écrire, ou null.</summary>
        public static string Refus(string chemin, string processus, string jeu)
        {
            if (JeuOuvert(processus)) return "Ferme " + jeu + " : le jeu réécrit ce fichier en quittant.";
            try
            {
                if (!File.Exists(chemin)) return "Fichier de réglages introuvable : lance " + jeu + " une fois.";
                if ((File.GetAttributes(chemin) & FileAttributes.ReadOnly) != 0)
                    return "Le fichier de réglages est en lecture seule : ONYX n'y touche pas (retire l'attribut toi-même si tu veux l'utiliser).";
            }
            catch (Exception ex) { return "Fichier de réglages illisible : " + ex.Message; }
            return null;
        }

        /// <summary>Texte du fichier et son encodage d'origine (BOM compris) ; null + raison sinon.</summary>
        public static string Lit(string chemin, string jeu, out Encoding enc, out string raison)
        {
            enc = new UTF8Encoding(false); raison = null;
            try
            {
                if (!File.Exists(chemin)) { raison = "Fichier de réglages introuvable : lance " + jeu + " une fois."; return null; }
                byte[] b = File.ReadAllBytes(chemin);
                int saut = 0;
                if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { enc = new UTF8Encoding(true); saut = 3; }
                else if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { enc = new UnicodeEncoding(false, true); saut = 2; }
                else if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { enc = new UnicodeEncoding(true, true); saut = 2; }
                return enc.GetString(b, saut, b.Length - saut);
            }
            catch (Exception ex) { raison = "Fichier de réglages illisible : " + ex.Message; return null; }
        }

        public static void Ecrit(string chemin, string texte, Encoding enc)
        {
            string tmp = chemin + ".onyx.tmp";
            File.WriteAllText(tmp, texte, enc);   // l'encodage porte son BOM d'origine (ou aucun)
            File.Move(tmp, chemin, true);
        }

        /// <summary>Copie datée avant la première écriture : « Fortnite-GameUserSettings-20261127-101500.ini ».</summary>
        public static void Sauvegarde(string chemin, string prefixe)
        {
            string dossier = Sauvegardes.Dossier(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            Directory.CreateDirectory(dossier);
            File.Copy(chemin, Path.Combine(dossier, prefixe + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + Path.GetExtension(chemin)), false);
        }
    }

    /// <summary>Ce que la fenêtre des préréglages demande à un jeu ; les modes vont de 0 (Compétition) à 3 (Qualité).</summary>
    internal interface IReglagesJeu
    {
        string Jeu { get; }
        string Note { get; }
        string FichierMesure { get; }
        string NomMode(int mode);
        /// <summary>Lignes « réglage : avant → après » ; null + raison si le fichier est illisible.</summary>
        System.Collections.Generic.List<string> Apercu(int mode, out System.Collections.Generic.List<string> absentes, out string raison);
        string Applique(int mode);
        string Retablit();
        bool ARetablir { get; }
    }
}
