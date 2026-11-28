using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// PRÉRÉGLAGES BLACK OPS 6 — branchement : le jeu garde DEUX copies de ses réglages
    /// (s.1.0.cod24.txt0 et .txt1, plus un petit .m qu'ONYX ne touche pas). On lit la plus récente
    /// et on écrit la même chose dans les deux, comme le conseillent les guides du jeu : sinon le
    /// jeu pourrait relire l'ancienne. Refus si Black Ops 6 ou Call of Duty HQ tourne, si une copie
    /// manque ou est en lecture seule. État dans bt-reglages-cod.txt, copies datées des deux
    /// fichiers dans Documents\ONYX\Sauvegardes avant la première écriture.
    /// </summary>
    internal static partial class ReglagesCod
    {
        private const string FichierEtat = "bt-reglages-cod.txt";
        private const string NomJeu = "Black Ops 6";
        private static readonly string[] Processus = { "cod24-cod", "sp24-cod", "cod" };

        /// <summary>Dossier « players » qui contient les réglages (BO6 récent, puis l'ancien emplacement commun).</summary>
        public static string Dossier
        {
            get
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                foreach (string d in new[] { "Call of Duty BO6", "Call of Duty" })
                {
                    string p = Path.Combine(docs, d, "players");
                    if (File.Exists(Path.Combine(p, "s.1.0.cod24.txt0"))) return p;
                }
                return Path.Combine(docs, "Call of Duty BO6", "players");
            }
        }

        private static string[] Copies()
        {
            string d = Dossier;
            return new[] { Path.Combine(d, "s.1.0.cod24.txt0"), Path.Combine(d, "s.1.0.cod24.txt1") };
        }

        /// <summary>Texte de la copie la plus récente ; null + raison sinon.</summary>
        private static string LitRecent(out Encoding enc, out string raison)
        {
            enc = new UTF8Encoding(false); raison = null;
            string choisi = null;
            DateTime date = DateTime.MinValue;
            foreach (string c in Copies())
            {
                try { if (File.Exists(c) && File.GetLastWriteTimeUtc(c) > date) { date = File.GetLastWriteTimeUtc(c); choisi = c; } }
                catch { }
            }
            if (choisi == null) { raison = "Réglages de Black Ops 6 introuvables : lance le jeu une fois."; return null; }
            return FichierReglagesJeu.Lit(choisi, NomJeu, out enc, out raison);
        }

        private static string Refus()
        {
            foreach (string p in Processus)
                if (FichierReglagesJeu.JeuOuvert(p)) return "Ferme Black Ops 6 et Call of Duty HQ : le jeu réécrit ses réglages en quittant.";
            foreach (string c in Copies())
            {
                string r = FichierReglagesJeu.Refus(c, "\u0001", NomJeu);   // processus déjà vérifiés ci-dessus
                if (r != null) return r;
            }
            return null;
        }

        private static void EcritCopies(string texte, Encoding enc)
        {
            foreach (string c in Copies()) FichierReglagesJeu.Ecrit(c, texte, enc);
        }

        private static string AvisNuage(string texte)
        {
            return Nuage(texte)
                ? "\r\nTes réglages Call of Duty sont synchronisés dans le cloud : si le jeu remet ses anciennes valeurs au lancement, coupe cette synchro dans ses options (Compte et réseau), puis réapplique."
                : "";
        }

        public static readonly IReglagesJeu Pilote = new PiloteCod();

        private sealed class PiloteCod : IReglagesJeu
        {
            public string Jeu { get { return NomJeu; } }
            public string Note
            {
                get
                {
                    return "Écrit dans les deux copies des réglages du jeu (s.1.0.cod24.txt0 et .txt1), jeu fermé. Valeurs proposées par ONYX, choisies parmi les crans du menu : "
                         + "« Mesurer en jeu » dit ce qu'elles valent. Résolution et mode d'affichage : seulement si tu les choisis. Jamais touchés : textures, DLSS/FSR/XeSS, génération d'images, synchro verticale, limite d'images, Reflex.";
                }
            }
            public string FichierMesure { get { return "bt-reglages-cod-avant.txt"; } }
            public string NomMode(int m) { return ReglagesCod.NomMode((Mode)m); }
            public List<string> Apercu(int m, ChoixAffichage a, out List<string> absentes, out string raison)
            {
                absentes = new List<string>();
                Encoding enc;
                string texte = LitRecent(out enc, out raison);
                if (texte == null) return null;
                List<Changement> c = Prevoit(texte, m, a, out absentes);
                absentes = absentes.ConvertAll(Libelle);
                return c.ConvertAll(Ligne);
            }
            public string Applique(int m, ChoixAffichage a) { return ReglagesCod.Applique(m, a); }
            public OptionsAffichage Affichage(out string raison)
            {
                Encoding enc;
                string texte = LitRecent(out enc, out raison);
                if (texte == null) return null;
                string mode;
                List<string> modes = ModesAffichage(texte, out mode);
                var o = new OptionsAffichage { ResolutionActuelle = ResolutionActuelle(texte), ModeActuel = mode };
                if (o.ResolutionActuelle != null) o.Resolutions = DisplayInfo.Resolutions(null, Ecran(texte));
                if (modes != null) foreach (string x in modes) o.Modes.Add(new KeyValuePair<string, string>(x, NomAffichage(x)));
                o.Remarque = "La résolution sert surtout en plein écran : en plein écran fenêtré, le jeu suit en général celle du bureau."
                    + (o.ResolutionActuelle == null ? " Résolution absente ou inattendue dans le fichier : non proposée." : "")
                    + (modes == null ? " Mode d'affichage absent ou inattendu dans le fichier : non proposé." : "");
                return o;
            }
            public string Retablit() { return ReglagesCod.Retablit(); }
            public bool ARetablir { get { return Etat().Count > 0; } }
            public bool Situation(out int? mode, out int limite, out bool vsync)
            {
                mode = null; limite = 0; vsync = false;
                Encoding enc; string raison;
                string texte = LitRecent(out enc, out raison);
                if (texte == null) return false;
                var n = new List<int>(); List<string> absentes;
                for (int m = 0; m < 4; m++) n.Add(Prevoit(texte, (Mode)m, out absentes).Count);
                mode = ObjectifFps.ModeActuel(n);
                limite = LimiteImages(texte, out vsync);
                return true;
            }
        }

        public static List<Changement> Etat()
        {
            try { string p = AppPaths.File(FichierEtat); return File.Exists(p) ? Lire(File.ReadAllText(p, Encoding.UTF8)) : new List<Changement>(); }
            catch { return new List<Changement>(); }
        }

        /// <summary>Préréglage graphique (mode -1 : aucun) puis affichage choisi.</summary>
        private static List<Changement> Prevoit(string texte, int m, ChoixAffichage a, out List<string> absentes)
        {
            List<string> abs1 = new List<string>(), abs2;
            List<Changement> c = new List<Changement>();
            if (m >= 0) c = Prevoit(texte, (Mode)m, out abs1);
            c.AddRange(PrevoitAffichage(texte, a, out abs2));
            abs1.AddRange(abs2);
            absentes = abs1;
            return c;
        }

        public static string Applique(Mode m) { return Applique((int)m, null); }

        public static string Applique(int m, ChoixAffichage a)
        {
            string refus = Refus();
            if (refus != null) return refus;
            Encoding enc; string raison;
            string texte = LitRecent(out enc, out raison);
            if (texte == null) return raison;
            List<string> absentes;
            List<Changement> c = Prevoit(texte, m, a, out absentes);
            string quoi = m >= 0 ? "« " + NomMode((Mode)m) + " »" + (a != null && !a.Vide ? " et affichage" : "") : "Affichage";
            if (c.Count == 0) return "Rien à changer : les réglages sont déjà ainsi." + AvisNuage(texte);
            string nouveau = Applique(texte, c);
            if (string.Equals(nouveau, texte, StringComparison.Ordinal)) return "Rien n'a pu être écrit : format du fichier inattendu, il n'a pas été modifié.";
            List<Changement> etat = Etat();
            try
            {
                if (etat.Count == 0) foreach (string copie in Copies()) FichierReglagesJeu.Sauvegarde(copie, "BlackOps6-s.1.0.cod24");
                EcritCopies(nouveau, enc);
                EcritEtat(Fusionne(etat, c));
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("ReglagesCod.Applique", ex);
                return "Écriture impossible : " + ex.Message + " Vérifie les deux fichiers ; « Rétablir » ou la copie de Documents\\ONYX\\Sauvegardes remettent l'état d'avant.";
            }
            var sb = new StringBuilder(quoi + " appliqué : " + c.Count + (c.Count > 1 ? " réglages écrits." : " réglage écrit."));
            foreach (Changement x in c) sb.Append("\r\n  ").Append(Ligne(x));
            if (absentes.Count > 0) sb.Append("\r\nAbsents ou inattendus dans le fichier, non touchés : ").Append(string.Join(", ", absentes.ConvertAll(Libelle))).Append('.');
            sb.Append(AvisNuage(texte));
            return sb.ToString();
        }

        public static string Retablit()
        {
            List<Changement> etat = Etat();
            if (etat.Count == 0) return "Rien à rétablir.";
            string refus = Refus();
            if (refus != null) return refus;
            Encoding enc; string raison;
            string texte = LitRecent(out enc, out raison);
            if (texte == null) return raison;
            string remis = Retablit(texte, etat);
            Dictionary<string, string> v = Valeurs(texte);
            int gardes = 0;
            foreach (Changement c in etat)
            {
                string actuel;
                if (!v.TryGetValue(c.Cle, out actuel) || !string.Equals(actuel, c.Apres, StringComparison.Ordinal)) gardes++;
            }
            try
            {
                if (!string.Equals(remis, texte, StringComparison.Ordinal)) EcritCopies(remis, enc);
                File.Delete(AppPaths.File(FichierEtat));
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("ReglagesCod.Retablit", ex);
                return "Écriture impossible : " + ex.Message + " La copie de Documents\\ONYX\\Sauvegardes garde l'état d'avant.";
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
