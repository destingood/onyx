using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="SuccesSteam"/> — succès Steam lus dans le cache local : bit à 1 = débloqué,
    /// date reprise d'AchievementTimes, succès cachés gardés secrets, fichiers abîmés refusés.
    /// </summary>
    internal static class SuccesSteamTests
    {
        // Petit écrivain KeyValues binaire, pour fabriquer des fichiers d'essai.
        private sealed class Kv
        {
            private readonly MemoryStream _m = new MemoryStream();
            private void Nom(byte t, string n) { _m.WriteByte(t); byte[] b = Encoding.UTF8.GetBytes(n); _m.Write(b, 0, b.Length); _m.WriteByte(0); }
            public Kv Ouvre(string n) { Nom(0, n); return this; }
            public Kv Ferme() { _m.WriteByte(8); return this; }
            public Kv Txt(string n, string v) { Nom(1, n); byte[] b = Encoding.UTF8.GetBytes(v); _m.Write(b, 0, b.Length); _m.WriteByte(0); return this; }
            public Kv Int(string n, int v) { Nom(2, n); _m.Write(BitConverter.GetBytes(v), 0, 4); return this; }
            public byte[] Octets() { return _m.ToArray(); }
        }

        private static Kv Bit(Kv k, string num, string fr, string en, string desc, bool cache)
        {
            k.Ouvre(num).Txt("name", "API_" + num).Ouvre("display").Ouvre("name");
            if (fr != null) k.Txt("french", fr);
            k.Txt("english", en).Txt("token", "TOK").Ferme().Ouvre("desc").Txt("english", desc).Ferme();
            if (cache) k.Int("hidden", 1);
            return k.Ferme().Ferme();
        }

        private static byte[] Schema()
        {
            var k = new Kv().Ouvre("1091500").Txt("gamename", "Jeu d'essai").Ouvre("stats");
            k.Ouvre("1").Txt("type", "ACHIEVEMENTS").Ouvre("bits");
            Bit(k, "0", "Le Fou", "The Fool", "Devenir mercenaire.", false);
            Bit(k, "1", null, "The Lovers", "Secret.", true);
            Bit(k, "2", "Le Monde", "The World", "Finir.", false);
            k.Ferme().Ferme();
            k.Ouvre("5").Txt("type", "INT").Txt("name", "kills").Ferme();          // simple statistique : ignorée
            k.Ouvre("2").Int("type", 4).Ouvre("bits");
            Bit(k, "3", "Bit trois", "Three", "", true);
            k.Ferme().Ferme();
            return k.Ferme().Ferme().Ferme().Octets();
        }

        private static byte[] Joueur()
        {
            var k = new Kv().Ouvre("cache").Int("crc", 1).Int("PendingChanges", 0);
            k.Ouvre("1").Int("data", 0x5).Ouvre("AchievementTimes").Int("0", 1700000000).Int("2", 1750000000).Ferme().Ferme();
            k.Ouvre("2").Int("data", 0x8).Ferme();
            return k.Ferme().Ferme().Octets();
        }

        public static void Tout()
        {
            Banc.Titre("Succès Steam");

            SuccesSteam.Bilan b = SuccesSteam.Lit(Schema(), Joueur());
            Banc.Verifie("schéma lu : 4 succès, 3 débloqués, statistique simple ignorée", true, b != null && b.Total == 4 && b.Obtenus == 3);
            Banc.Egal("nom du jeu", "Jeu d'essai", b.Jeu);
            Banc.Egal("ordre : débloqués du plus récent au plus ancien, puis le reste",
                "Le Monde|Le Fou|Bit trois|Succès caché",
                string.Join("|", b.Liste.ConvertAll(SuccesSteam.NomAffiche)));
            Banc.Verifie("français, sinon anglais", true, b.Liste.Exists(x => x.Nom == "The Lovers"));
            SuccesSteam.Succes cache = b.Liste.Find(x => x.Nom == "The Lovers");
            Banc.Verifie("succès caché non débloqué : nom et description masqués", true,
                SuccesSteam.NomAffiche(cache) == "Succès caché" && SuccesSteam.DescriptionAffichee(cache) != "Secret.");
            SuccesSteam.Succes trois = b.Liste.Find(x => x.Nom == "Bit trois");
            Banc.Verifie("succès caché débloqué : visible ; type numérique 4 reconnu ; sans date", true,
                SuccesSteam.NomAffiche(trois) == "Bit trois" && trois.Obtenu && SuccesSteam.Etat(trois) == "Débloqué");
            Banc.Verifie("date reprise d'AchievementTimes", true, b.Liste[0].DateUnix == 1750000000 && SuccesSteam.Etat(b.Liste[0]).StartsWith("Débloqué le "));
            Banc.Egal("résumé", "3 / 4 (75 %)", SuccesSteam.Resume(b));

            SuccesSteam.Bilan rien = SuccesSteam.Lit(Schema(), null);
            Banc.Verifie("aucun relevé du joueur : tout reste à débloquer", true, rien != null && rien.Obtenus == 0 && rien.Total == 4 && SuccesSteam.Etat(rien.Liste[0]) == "À débloquer");

            // Bilan de jeu : succès datés, par période.
            List<SuccesSteam.Deblocage> d = SuccesSteam.Deblocages(b, "Jeu A");
            Banc.Verifie("déblocages : seuls les succès datés comptent", true, d.Count == 2 && d.TrueForAll(x => x.Jeu == "Jeu A"));
            DateTime t1 = DateTimeOffset.FromUnixTimeSeconds(1750000000).LocalDateTime;
            d.Add(new SuccesSteam.Deblocage { Jeu = "Jeu B", Nom = "Autre", DateUnix = 1750000000 - 3 * 86400 });
            string l7 = SuccesSteam.LigneBilan(d, t1.AddDays(1), 7);
            Banc.Egal("ligne sur 7 jours : 2 succès sur 2 jeux, le plus récent nommé",
                "Succès Steam débloqués : 2 sur 2 jeux (dernier : « Le Monde », Jeu A, le " + t1.ToString("dd'/'MM") + ").", l7);
            Banc.Verifie("depuis le début : les 3", true, SuccesSteam.LigneBilan(d, t1.AddDays(1), 0).StartsWith("Succès Steam débloqués : 3 sur 2 jeux"));
            Banc.Verifie("un seul jeu : pas de « sur 1 jeux »", true, SuccesSteam.LigneBilan(d, t1.AddDays(1), 1) == "Succès Steam débloqués : 1 (dernier : « Le Monde », Jeu A, le " + t1.ToString("dd'/'MM") + ").");
            Banc.Verifie("rien dans la période, liste vide ou pas encore lue : pas de ligne", true,
                SuccesSteam.LigneBilan(d, t1.AddDays(30), 7) == null && SuccesSteam.LigneBilan(new List<SuccesSteam.Deblocage>(), t1, 0) == null && SuccesSteam.LigneBilan(null, t1, 0) == null);
            Banc.Verifie("succès daté après « maintenant » : ignoré", true, SuccesSteam.LigneBilan(d, t1.AddDays(-1), 0).StartsWith("Succès Steam débloqués : 2 sur 2 jeux (dernier : « Autre »"));

            byte[] s = Schema();
            byte[] tronque = new byte[s.Length - 5];
            Array.Copy(s, tronque, tronque.Length);
            Banc.Verifie("fichier tronqué : refusé", true, SuccesSteam.Lit(tronque, Joueur()) == null);
            Banc.Verifie("type inconnu : refusé", true, SuccesSteam.LitKv(new byte[] { 9, 65, 0, 8 }) == null);
            Banc.Verifie("vide ou absent : rien", true, SuccesSteam.Lit(new byte[0], null) == null && SuccesSteam.Lit(null, null) == null);
            byte[] sansSucces = new Kv().Ouvre("1").Ouvre("stats").Ouvre("5").Txt("type", "INT").Ferme().Ferme().Ferme().Ferme().Octets();
            Banc.Verifie("jeu sans succès : rien", true, SuccesSteam.Lit(sansSucces, null) == null);
            byte[] joueurAbime = new byte[] { 0, 99, 0 };
            Banc.Verifie("relevé du joueur abîmé : liste quand même, rien de débloqué", true, SuccesSteam.Lit(Schema(), joueurAbime).Obtenus == 0);

            // Le vrai cache Steam de ce PC, s'il existe : lu seulement.
            string dossier = @"C:\Program Files (x86)\Steam\appcache\stats";
            if (Directory.Exists(dossier))
            {
                int lus = 0, refus = 0, incoherents = 0;
                foreach (string f in Directory.GetFiles(dossier, "UserGameStatsSchema_*.bin"))
                {
                    string app = Path.GetFileNameWithoutExtension(f).Substring("UserGameStatsSchema_".Length);
                    string[] j = Directory.GetFiles(dossier, "UserGameStats_*_" + app + ".bin");
                    byte[] o = File.ReadAllBytes(f);
                    if (SuccesSteam.LitKv(o) == null) { refus++; continue; }
                    SuccesSteam.Bilan v = SuccesSteam.Lit(o, j.Length > 0 ? File.ReadAllBytes(j[0]) : null);
                    if (v == null) continue;
                    lus++;
                    if (v.Obtenus > v.Total || v.Liste.Exists(x => x.Nom.Length == 0)) incoherents++;
                }
                Banc.Verifie("vrai cache Steam : tous les schémas lus (" + lus + " jeux avec succès)", true, refus == 0 && incoherents == 0);
            }
        }
    }
}
