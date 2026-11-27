using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// JEUX DU COMPTE STEAM — branchement : loginusers.vdf (compte le plus récent), puis
    /// userdata\&lt;compte&gt;\config\localconfig.vdf, puis les fiches publiques du store par lots de 50
    /// (réponse en cache 7 jours). Rien de personnel ne sort : seuls des numéros de jeux partent au store.
    /// </summary>
    internal static partial class JeuxCompteSteam
    {
        public const int MaxJeux = 1000;

        /// <summary>Jeux non installés du compte, prêts pour la bibliothèque. Bloquant : hors du fil d'interface. Vide si Steam est absent ou hors ligne.</summary>
        public static List<GameScan.GameInfo> Charge(ICollection<int> installes)
        {
            var res = new List<GameScan.GameInfo>();
            string steam = SteamGames.SteamPath();
            if (steam == null) return res;
            string login = Path.Combine(steam, "config", "loginusers.vdf");
            if (!File.Exists(login)) return res;
            string compte = Compte(ListeSouhaits.SteamId64(File.ReadAllText(login, Encoding.UTF8)));
            if (compte == null) return res;
            string lc = Path.Combine(steam, "userdata", compte, "config", "localconfig.vdf");
            if (!File.Exists(lc)) return res;
            List<Possede> absents = NonInstalles(Lire(File.ReadAllText(lc, Encoding.UTF8)), installes);
            if (absents.Count > MaxJeux) absents.RemoveRange(MaxJeux, absents.Count - MaxJeux);

            var noms = new Dictionary<int, string>();
            for (int i = 0; i < absents.Count; i += OffresSources.LotSteam)
            {
                var ids = new List<int>();
                for (int k = i; k < absents.Count && k < i + OffresSources.LotSteam; k++) ids.Add(absents[k].AppId);
                try
                {
                    string json = OffresReseau.Texte(OffresSources.UrlFichesSteam(ids), 7 * 24 * 60).GetAwaiter().GetResult();
                    foreach (KeyValuePair<int, string> kv in Jeux(json)) noms[kv.Key] = kv.Value;
                }
                catch { }   // lot raté (hors ligne) : ces jeux n'apparaissent pas cette fois
            }
            foreach (Possede p in absents)
            {
                string nom;
                if (!noms.TryGetValue(p.AppId, out nom)) continue;
                res.Add(new GameScan.GameInfo
                {
                    Name = nom, SteamId = p.AppId, Store = "STEAM", SurCompte = true, MinutesSteam = p.Minutes,
                    Dirs = new string[0], Keywords = new string[0],
                });
            }
            return res;
        }
    }
}
