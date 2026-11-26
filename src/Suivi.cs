using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BTOptimizer
{
    /// <summary>Un favori (jeu ou actu). Cle = Offre.Cle pour un jeu, le lien pour une actu.</summary>
    internal sealed class Favori { public string Cle, Source, Url, Titre; public DateTime Date; }

    /// <summary>
    /// Favoris (logique PURE) : format du fichier, une ligne par favori
    /// « Cle \t Source \t yyyy-MM-dd \t Url \t Titre ». Le disque vit dans SuiviVeille (Magasin).
    /// </summary>
    internal static class Favoris
    {
        public static List<Favori> Lire(string texte)
        {
            var res = new List<Favori>();
            if (string.IsNullOrEmpty(texte)) return res;
            var vues = new HashSet<string>();
            foreach (string brute in texte.Split('\n'))
            {
                string[] c = brute.TrimEnd('\r').Split('\t');
                if (c.Length < 5 || c[0].Trim().Length == 0) continue;
                if (!vues.Add(c[0])) continue;
                DateTime d;
                if (!DateTime.TryParseExact(c[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) d = DateTime.MinValue;
                res.Add(new Favori { Cle = c[0], Source = c[1], Date = d, Url = c[3], Titre = c[4] });
            }
            return res;
        }

        public static string Ecrire(IEnumerable<Favori> favoris)
        {
            var sb = new StringBuilder();
            if (favoris == null) return "";
            foreach (var f in favoris)
            {
                if (f == null || string.IsNullOrWhiteSpace(f.Cle)) continue;
                sb.Append(Champ(f.Cle)).Append('\t').Append(Champ(f.Source)).Append('\t')
                  .Append(f.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(Champ(f.Url)).Append('\t').Append(Champ(f.Titre)).Append('\n');
            }
            return sb.ToString();
        }

        public static bool Contient(List<Favori> favoris, string cle)
        {
            return favoris != null && cle != null && favoris.Exists(f => f.Cle == cle);
        }

        /// <summary>Ajoute en tête ou retire ; vrai si le favori est présent après l'appel.</summary>
        public static bool Bascule(List<Favori> favoris, Favori f)
        {
            if (favoris == null || f == null || string.IsNullOrWhiteSpace(f.Cle)) return false;
            if (favoris.RemoveAll(x => x.Cle == f.Cle) > 0) return false;
            favoris.Insert(0, f);
            return true;
        }

        public static Favori DepuisOffre(Offre o, DateTime aujourdhui)
        {
            return new Favori { Cle = o.Cle, Source = o.Source, Url = o.Url, Titre = o.Nom, Date = aujourdhui.Date };
        }

        /// <summary>Un champ ne doit jamais casser le format : tabulations et fins de ligne → espace.</summary>
        internal static string Champ(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }

    /// <summary>Un jeu Steam suivi. RemiseNotifiee et GratuitNotifie empêchent une deuxième notification pour la même promo.</summary>
    internal sealed class JeuSuivi { public int AppId; public string Nom; public int RemiseNotifiee; public string GratuitNotifie = ""; public string FinNotifiee = ""; }

    /// <summary>
    /// Jeux suivis (logique PURE) : format du fichier « AppId \t RemiseNotifiee \t GratuitNotifie \t Nom \t FinNotifiee »
    /// (5e colonne ajoutée en 16.07, après le nom : les versions précédentes l'ignorent)
    /// et règle de notification. Une seule alerte par promo, même après un redémarrage ; une fiche
    /// manquante (erreur réseau) ne change rien.
    /// </summary>
    internal static class Suivi
    {
        public const int Max = 50;
        public sealed class Alerte { public string Titre, Texte, Url; public int AppId; }

        public static List<JeuSuivi> Lire(string texte)
        {
            var res = new List<JeuSuivi>();
            if (string.IsNullOrEmpty(texte)) return res;
            var vus = new HashSet<int>();
            foreach (string brute in texte.Split('\n'))
            {
                string[] c = brute.TrimEnd('\r').Split('\t');
                int id, remise;
                if (c.Length < 4 || !int.TryParse(c[0], NumberStyles.None, CultureInfo.InvariantCulture, out id) || id <= 0) continue;
                if (!vus.Add(id)) continue;
                if (!int.TryParse(c[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out remise) || remise > 100 || remise < AAmorcer) remise = 0;
                res.Add(new JeuSuivi { AppId = id, RemiseNotifiee = remise, GratuitNotifie = c[2], Nom = c[3], FinNotifiee = c.Length > 4 ? c[4] : "" });
            }
            return res;
        }

        public static string Ecrire(IEnumerable<JeuSuivi> jeux)
        {
            var sb = new StringBuilder();
            if (jeux == null) return "";
            foreach (var j in jeux)
            {
                if (j == null || j.AppId <= 0) continue;
                sb.Append(j.AppId.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(j.RemiseNotifiee.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(Favoris.Champ(j.GratuitNotifie)).Append('\t')
                  .Append(Favoris.Champ(j.Nom)).Append('\t')
                  .Append(Favoris.Champ(j.FinNotifiee)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Remise pas encore connue (jeu ajouté depuis la bibliothèque) : le premier contrôle l'amorce sans alerte.</summary>
        public const int AAmorcer = -1;

        /// <summary>Jeu Steam installé, remise inconnue : suivi « à amorcer » pour qu'une promo déjà en cours ne notifie pas.</summary>
        public static bool AjouteInstalle(List<JeuSuivi> jeux, int appId, string nom)
        {
            if (!Ajoute(jeux, new Offre { Source = "steam", SteamAppId = appId, Nom = nom })) return false;
            jeux[jeux.Count - 1].RemiseNotifiee = AAmorcer;
            return true;
        }

        /// <summary>Faux si déjà suivi, hors Steam ou si Max est atteint. Une promo en cours ne déclenche pas d'alerte immédiate.</summary>
        public static bool Ajoute(List<JeuSuivi> jeux, Offre o)
        {
            if (jeux == null || o == null || o.SteamAppId <= 0) return false;
            if (jeux.Exists(j => j.AppId == o.SteamAppId) || jeux.Count >= Max) return false;
            jeux.Add(new JeuSuivi { AppId = o.SteamAppId, Nom = Favoris.Champ(o.Nom).Trim(), RemiseNotifiee = Math.Max(0, o.Remise) });
            return true;
        }

        public static bool Retire(List<JeuSuivi> jeux, int appId)
        {
            return jeux != null && jeux.RemoveAll(j => j.AppId == appId) > 0;
        }

        /// <summary>Fenêtre de l'alerte « la promo se termine » : moins de 48 h avant la fin.</summary>
        public static readonly TimeSpan AvantFin = TimeSpan.FromHours(48);

        public static List<Alerte> Decide(List<JeuSuivi> jeux, List<Offre> fiches, List<Offre> gratuits)
        {
            return Decide(jeux, fiches, gratuits, DateTime.UtcNow);
        }

        /// <summary>Clé d'une fin de promo (une seule alerte « se termine » par promo).</summary>
        public static string CleFin(DateTime finUtc)
        {
            return finUtc.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        }

        public static List<Alerte> Decide(List<JeuSuivi> jeux, List<Offre> fiches, List<Offre> gratuits, DateTime maintenantUtc)
        {
            var res = new List<Alerte>();
            if (jeux == null) return res;
            foreach (var j in jeux)
            {
                // Promo Steam : seulement une remise plus forte que la dernière notifiée.
                Offre f = fiches == null ? null : fiches.Find(x => x != null && x.SteamAppId == j.AppId);
                if (f != null)
                {
                    if (!string.IsNullOrWhiteSpace(f.Nom)) j.Nom = Favoris.Champ(f.Nom).Trim();
                    if (j.RemiseNotifiee == AAmorcer) j.RemiseNotifiee = Math.Max(0, f.Remise);   // premier contrôle : aucune alerte
                    else if (f.Remise > 0 && f.PrixCts >= 0 && f.Remise > j.RemiseNotifiee)
                    {
                        bool bientot = FinProche(f, maintenantUtc);
                        res.Add(new Alerte
                        {
                            AppId = j.AppId,
                            Titre = j.Nom + " : −" + f.Remise.ToString(CultureInfo.InvariantCulture) + " %",
                            Texte = Offres.Prix(f.PrixCts) + " au lieu de " + Offres.Prix(f.PrixOrigineCts) + " sur Steam"
                                  + (bientot ? ", jusqu'au " + JusquAu(f.Fin.Value) : ""),
                            Url = f.Url,
                        });
                        j.RemiseNotifiee = f.Remise;
                        if (bientot) j.FinNotifiee = CleFin(f.Fin.Value);   // la même promo ne notifie pas une 2e fois
                    }
                    else if (f.Remise > 0 && f.PrixCts >= 0 && FinProche(f, maintenantUtc) && j.FinNotifiee != CleFin(f.Fin.Value))
                    {
                        // Promo déjà signalée (ou en cours au moment du suivi) qui finit bientôt : dernier rappel.
                        res.Add(new Alerte
                        {
                            AppId = j.AppId,
                            Titre = j.Nom + " : la promo se termine bientôt",
                            Texte = "−" + f.Remise.ToString(CultureInfo.InvariantCulture) + " % (" + Offres.Prix(f.PrixCts) + ") sur Steam jusqu'au " + JusquAu(f.Fin.Value),
                            Url = f.Url,
                        });
                        j.FinNotifiee = CleFin(f.Fin.Value);
                    }
                    else if (f.Remise == 0) j.RemiseNotifiee = 0;
                }

                // Gratuit ailleurs (Epic, GamerPower) : rapprochement par nom, une alerte par offre.
                if (gratuits == null) continue;
                string n = Offres.NomNormalise(j.Nom);
                if (n.Length == 0) continue;
                foreach (var g in gratuits)
                {
                    if (g == null || g.PrixCts != 0 || g.Cle == j.GratuitNotifie || Offres.NomNormalise(g.Nom) != n) continue;
                    string plat = g.Plateformes != null && g.Plateformes.Length > 0 ? g.Plateformes[0] : null;
                    string texte = plat != null ? "Sur " + plat : "Offre gratuite";
                    if (g.Fin.HasValue) texte += " jusqu'au " + g.Fin.Value.ToString("dd/MM", CultureInfo.InvariantCulture);
                    res.Add(new Alerte { AppId = j.AppId, Titre = j.Nom + " est gratuit", Texte = texte, Url = g.Url });
                    j.GratuitNotifie = g.Cle;
                    break;
                }
            }
            return res;
        }

        private static bool FinProche(Offre f, DateTime maintenantUtc)
        {
            return f.Fin.HasValue && f.Fin.Value > maintenantUtc && f.Fin.Value - maintenantUtc <= AvantFin;
        }

        private static string JusquAu(DateTime finUtc)
        {
            return finUtc.ToLocalTime().ToString("dd'/'MM 'à' HH'h'mm", CultureInfo.InvariantCulture);
        }

        /// <summary>Au plus max notifications : au-delà, les max - 1 premières et une alerte de synthèse.</summary>
        public static List<Alerte> Regroupe(List<Alerte> alertes, int max)
        {
            var res = new List<Alerte>();
            if (alertes == null || max <= 0) return res;
            if (alertes.Count <= max) { res.AddRange(alertes); return res; }
            for (int i = 0; i < max - 1; i++) res.Add(alertes[i]);
            int reste = alertes.Count - (max - 1);
            res.Add(new Alerte { Titre = reste.ToString(CultureInfo.InvariantCulture) + " autres jeux suivis en promo", Texte = "Ouvre Bons plans › Suivis", Url = null });
            return res;
        }
    }
}
