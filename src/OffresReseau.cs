using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BTOptimizer
{
    /// <summary>
    /// Bons plans et Actus (MACHINE) : HTTP, cache disque et espacement des appels.
    /// User-Agent « ONYX », 12 s au plus, 2 Mo au plus, 1 s au moins entre deux requêtes vers le
    /// même hôte, aucune donnée de l'utilisateur hors du texte tapé dans la recherche (envoyé aux stores interrogés).
    /// Toute erreur donne null ou une liste vide : rien ne remonte jusqu'à l'interface.
    /// La décision (URL, lecture des réponses) vit dans OffresSources et Actus.
    /// </summary>
    internal static class OffresReseau
    {
        private const int MaxOctets = 2 * 1024 * 1024;
        private static readonly TimeSpan Espacement = TimeSpan.FromSeconds(1);
        private static readonly HttpClient Http = Construit();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Portes = new ConcurrentDictionary<string, SemaphoreSlim>();
        private static readonly ConcurrentDictionary<string, DateTime> DernierAppel = new ConcurrentDictionary<string, DateTime>();
        private static readonly ConcurrentDictionary<string, bool> Erreurs = new ConcurrentDictionary<string, bool>();
        private static int _purgeFaite;

        private static HttpClient Construit()
        {
            var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
            {
                Timeout = TimeSpan.FromSeconds(12),
                MaxResponseContentBufferSize = MaxOctets,
            };
            h.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ONYX");
            return h;
        }

        /// <summary>Vrai si le dernier appel à cette source ("steam", "epic", "gamerpower", "actus") a échoué.</summary>
        public static bool DerniereErreur(string source)
        {
            bool e;
            return source != null && Erreurs.TryGetValue(source, out e) && e;
        }

        /// <summary>Contenu d'une URL, avec cache (cacheMinutes = 0 : toujours un appel). Échec → cache même périmé, sinon null.</summary>
        public static async Task<string> Texte(string url, int cacheMinutes)
        {
            var r = await Lit(url, cacheMinutes).ConfigureAwait(false);
            return r.Texte;
        }

        /// <summary>Recherche Steam → identifiants → fiches (prix, remise, tags), dans l'ordre de la recherche.</summary>
        public static async Task<List<Offre>> RechercheSteam(Offres.Filtre f, bool promos, bool nouveautes, int debut)
        {
            f = f ?? new Offres.Filtre();
            try
            {
                string url = OffresSources.UrlRechercheSteam(f.Texte, f.Tag, f.PlafondCts, promos, nouveautes, debut);
                var r = await Lit(url, 30).ConfigureAwait(false);
                if (r.Texte == null) { Erreurs["steam"] = true; return new List<Offre>(); }
                var ids = OffresSources.IdsRechercheSteam(r.Texte);
                var fiches = await Fiches(ids, 30).ConfigureAwait(false);
                Erreurs["steam"] = r.Echec || fiches.Echec;
                var parId = new Dictionary<int, Offre>();
                foreach (var o in fiches.Offres) parId[o.SteamAppId] = o;
                var res = new List<Offre>();
                foreach (int id in ids)
                {
                    Offre o;
                    if (parId.TryGetValue(id, out o)) res.Add(o);
                }
                return res;
            }
            catch { Erreurs["steam"] = true; return new List<Offre>(); }
        }

        /// <summary>Fiches Steam par lots de OffresSources.LotSteam.</summary>
        public static async Task<List<Offre>> FichesSteam(IEnumerable<int> appids, int cacheMinutes)
        {
            try
            {
                var r = await Fiches(appids, cacheMinutes).ConfigureAwait(false);
                Erreurs["steam"] = r.Echec;
                return r.Offres;
            }
            catch { Erreurs["steam"] = true; return new List<Offre>(); }
        }

        /// <summary>Jeux gratuits d'Epic et de GamerPower ; une source en panne n'empêche pas l'autre.</summary>
        public static async Task<List<Offre>> Gratuits()
        {
            try
            {
                var epic = Lit(OffresSources.UrlEpic, 60);
                var gp = Lit(OffresSources.UrlGamerPower, 60);
                await Task.WhenAll(epic, gp).ConfigureAwait(false);
                Erreurs["epic"] = epic.Result.Echec;
                Erreurs["gamerpower"] = gp.Result.Echec;
                var tous = new List<Offre>();
                tous.AddRange(OffresSources.GratuitsEpic(epic.Result.Texte, DateTime.UtcNow));
                tous.AddRange(OffresSources.GratuitsGamerPower(gp.Result.Texte));
                // GamerPower n'a pas toujours retiré une offre finie ; un cache peut aussi être ancien.
                return Offres.EnCours(Offres.Dedoublonner(tous), DateTime.UtcNow);
            }
            catch { Erreurs["epic"] = true; Erreurs["gamerpower"] = true; return new List<Offre>(); }
        }

        /// <summary>Une page de promos d'un store autre que Steam ; Fini : plus rien après cette page.</summary>
        public sealed class PageStore { public List<Offre> Offres = new List<Offre>(); public bool Fini, Echec; }

        /// <summary>
        /// Promos de "gog", "nintendo", "itch" ou "cheapshark" (stores idsCheapShark), page 0, 1, 2…
        /// Le texte est cherché par le store (itch.io : filtré ici, son flux n'a qu'une page).
        /// </summary>
        public static async Task<PageStore> PromosStore(string source, string texte, int page, string idsCheapShark)
        {
            var res = new PageStore();
            try
            {
                string url = source == "gog" ? OffresSources.UrlGog(texte, page + 1)
                    : source == "nintendo" ? OffresSources.UrlNintendo(texte, page * OffresSources.ParPageNintendo)
                    : source == "itch" ? (page == 0 ? OffresSources.UrlItch : null)
                    : source == "cheapshark" && !string.IsNullOrEmpty(idsCheapShark) ? OffresSources.UrlCheapShark(idsCheapShark, texte, page)
                    : null;
                if (url == null) { res.Fini = true; return res; }
                var r = await Lit(url, 30).ConfigureAwait(false);
                res.Echec = r.Echec;
                Erreurs[source] = r.Echec;
                if (r.Texte == null) return res;
                List<Offre> l = source == "gog" ? OffresSources.PromosGog(r.Texte)
                    : source == "nintendo" ? OffresSources.PromosNintendo(r.Texte)
                    : source == "itch" ? OffresSources.PromosItch(r.Texte)
                    : OffresSources.PromosCheapShark(r.Texte);
                int brut = l.Count;   // avant le tri des promos finies : une page vidée n'est pas la dernière
                if (source == "nintendo")
                {
                    string urlPrix = OffresSources.UrlPrixNintendo(l);
                    if (urlPrix != null)
                    {
                        var p = await Lit(urlPrix, 30).ConfigureAwait(false);
                        if (p.Texte != null) l = OffresSources.AppliquePrixNintendo(l, p.Texte);
                    }
                }
                if (source == "itch" && !string.IsNullOrWhiteSpace(texte)) l = Offres.Filtrer(l, new Offres.Filtre { Texte = texte });
                res.Offres = Offres.EnCours(l, DateTime.UtcNow);
                res.Fini = source == "itch" || brut == 0;
                return res;
            }
            catch { Erreurs[source ?? ""] = true; res.Echec = true; return res; }
        }

        /// <summary>Titres des sites d'actus et des flux Steam des 10 premiers jeux suivis (Source = nom du jeu).</summary>
        public static async Task<List<Actu>> Actualites(IEnumerable<JeuSuivi> suivis)
        {
            // Copie AVANT le premier await : la liste des suivis appartient au thread d'interface.
            var jeux = suivis == null ? new List<JeuSuivi>() : suivis.Where(j => j != null && j.AppId > 0).Take(10)
                .Select(j => new JeuSuivi { AppId = j.AppId, Nom = string.IsNullOrWhiteSpace(j.Nom) ? "Steam" : j.Nom }).ToList();
            try
            {
                var appels = new List<Tuple<string, Task<Lu>>>();
                foreach (var s in Actus.Sources) appels.Add(Tuple.Create(s.Nom, Lit(s.Url, 15)));
                foreach (var j in jeux) appels.Add(Tuple.Create(j.Nom, Lit(Actus.UrlFluxSteam(j.AppId), 15)));
                await Task.WhenAll(appels.Select(a => a.Item2)).ConfigureAwait(false);
                var sites = new List<List<Actu>>();
                var fluxJeux = new List<List<Actu>>();
                int nbSites = Actus.Sources.Length;
                bool toutEchoue = true;
                for (int i = 0; i < appels.Count; i++)
                {
                    var a = appels[i];
                    if (!a.Item2.Result.Echec) toutEchoue = false;
                    var lus = Actus.LitRss(a.Item2.Result.Texte, a.Item1);
                    if (i >= nbSites) foreach (var x in lus) x.AppId = jeux[i - nbSites].AppId;   // repli d'image : la jaquette du jeu
                    (i < nbSites ? sites : fluxJeux).Add(lus);
                }
                Erreurs["actus"] = toutEchoue;
                // Les flux des jeux suivis ont leur propre quota : les sites, plus récents, ne les évincent pas.
                return Actus.FusionneAvecJeux(sites, fluxJeux, Actus.MaxAffiche, 10);
            }
            catch { Erreurs["actus"] = true; return new List<Actu>(); }
        }

        // ---------------------------------------------------------------- machine

        private sealed class Lu { public string Texte; public bool Echec; }
        private sealed class LotFiches { public List<Offre> Offres = new List<Offre>(); public bool Echec; }

        private static async Task<LotFiches> Fiches(IEnumerable<int> appids, int cacheMinutes)
        {
            var res = new LotFiches();
            var ids = appids == null ? new List<int>() : appids.Where(i => i > 0).Distinct().ToList();
            for (int i = 0; i < ids.Count; i += OffresSources.LotSteam)
            {
                string url = OffresSources.UrlFichesSteam(ids.Skip(i).Take(OffresSources.LotSteam));
                if (url == null) continue;
                var r = await Lit(url, cacheMinutes).ConfigureAwait(false);
                if (r.Echec) res.Echec = true;
                res.Offres.AddRange(OffresSources.FichesSteam(r.Texte));
            }
            return res;
        }

        private static async Task<Lu> Lit(string url, int cacheMinutes)
        {
            var res = new Lu();
            if (!Offres.LienSur(url)) { res.Echec = true; return res; }
            string fichier = null;
            try
            {
                Purge();
                fichier = FichierCache(url);
                if (cacheMinutes > 0 && File.Exists(fichier)
                    && DateTime.UtcNow - File.GetLastWriteTimeUtc(fichier) < TimeSpan.FromMinutes(cacheMinutes))
                {
                    res.Texte = File.ReadAllText(fichier, Encoding.UTF8);
                    return res;
                }
            }
            catch { fichier = null; }

            string texte = await Telecharge(url).ConfigureAwait(false);
            if (texte != null)
            {
                res.Texte = texte;
                if (fichier != null)
                {
                    try { File.WriteAllText(fichier, texte, new UTF8Encoding(false)); } catch { }
                }
                return res;
            }
            res.Echec = true;
            try { if (fichier != null && File.Exists(fichier)) res.Texte = File.ReadAllText(fichier, Encoding.UTF8); } catch { }
            return res;
        }

        /// <summary>Un appel HTTP, espacé d'au moins 1 s du précédent vers le même hôte ; jamais d'exception.</summary>
        private static async Task<string> Telecharge(string url)
        {
            string hote;
            try { hote = new Uri(url).Host.ToLowerInvariant(); } catch { return null; }
            var porte = Portes.GetOrAdd(hote, h => new SemaphoreSlim(1, 1));
            await porte.WaitAsync().ConfigureAwait(false);
            try
            {
                DateTime dernier;
                if (DernierAppel.TryGetValue(hote, out dernier))
                {
                    TimeSpan attente = dernier + Espacement - DateTime.UtcNow;
                    if (attente > TimeSpan.Zero) await Task.Delay(attente).ConfigureAwait(false);
                }
                // Http.Timeout ne couvre pas la lecture du corps (ResponseHeadersRead) : un jeton borne l'appel entier.
                try
                {
                    using (var delai = new CancellationTokenSource(Http.Timeout))
                    using (var rep = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, delai.Token).ConfigureAwait(false))
                    {
                        if (!rep.IsSuccessStatusCode) return null;
                        long? taille = rep.Content.Headers.ContentLength;
                        if (taille.HasValue && taille.Value > MaxOctets) return null;
                        byte[] octets = await LitBorne(rep, delai.Token).ConfigureAwait(false);
                        if (octets == null) return null;
                        string texte = Decode(octets, rep.Content.Headers.ContentType == null ? null : rep.Content.Headers.ContentType.CharSet);
                        // Portail captif (Wi-Fi public) ou page d'erreur en 200 : traité comme un échec, jamais mis en cache.
                        return OffresSources.ReponsePlausible(texte) ? texte : null;
                    }
                }
                catch { return null; }
                finally { DernierAppel[hote] = DateTime.UtcNow; }
            }
            finally { porte.Release(); }
        }

        private static async Task<byte[]> LitBorne(HttpResponseMessage rep, CancellationToken jeton)
        {
            using (var flux = await rep.Content.ReadAsStreamAsync(jeton).ConfigureAwait(false))
            using (var ms = new MemoryStream())
            {
                var tampon = new byte[81920];
                int n;
                while ((n = await flux.ReadAsync(tampon, 0, tampon.Length, jeton).ConfigureAwait(false)) > 0)
                {
                    if (ms.Length + n > MaxOctets) return null;
                    ms.Write(tampon, 0, n);
                }
                return ms.ToArray();
            }
        }

        private static string Decode(byte[] octets, string charset)
        {
            Encoding enc = Encoding.UTF8;
            if (!string.IsNullOrWhiteSpace(charset))
            {
                try { enc = Encoding.GetEncoding(charset.Trim('"', ' ')); } catch { enc = Encoding.UTF8; }
            }
            return enc.GetString(octets).TrimStart('﻿');
        }

        private static string DossierCache()
        {
            string d = Path.Combine(AppPaths.DataDir, "bt-offres-cache");
            Directory.CreateDirectory(d);
            return d;
        }

        private static string FichierCache(string url)
        {
            byte[] h;
            using (var sha = SHA256.Create()) h = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
            var sb = new StringBuilder(64);
            foreach (byte b in h) sb.Append(b.ToString("x2"));
            return Path.Combine(DossierCache(), sb.ToString());
        }

        /// <summary>Au premier appel de la session : fichiers de cache de plus de 7 jours supprimés.</summary>
        private static void Purge()
        {
            if (Interlocked.Exchange(ref _purgeFaite, 1) == 1) return;
            try
            {
                foreach (string f in Directory.GetFiles(DossierCache()))
                {
                    try { if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromDays(7)) File.Delete(f); } catch { }
                }
            }
            catch { }
        }
    }
}
