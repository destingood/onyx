using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// LATENCE AUDIO — ce que Windows intercale entre l'application et la carte son.
    ///
    /// Deux réglages coûtent réellement des millisecondes : les « améliorations audio » (chaque
    /// effet — égalisation, suppression de bruit, son spatial — est un calcul de plus avant la
    /// sortie) et le refus du MODE EXCLUSIF (l'application ne peut alors pas parler directement au
    /// matériel, tout repasse par le mélangeur système).
    ///
    /// ON NE FAIT QUE LIRE. Ces valeurs vivent sous MMDevices\...\Properties, protégé par ACL, et
    /// le format par défaut y est un blob binaire qu'une écriture malformée transforme en
    /// périphérique muet. AudioTools a déjà tranché : « les réglages d'améliorations se font dans le
    /// panneau Windows natif ». On garde cette ligne — on DÉTECTE, et on ouvre le bon panneau.
    ///
    /// Règle de prudence : une valeur ABSENTE ne prouve rien (Windows applique alors son défaut,
    /// qui dépend du pilote). On ne signale QUE ce qui est explicitement mauvais.
    /// </summary>
    internal static class AudioLatency
    {
        private const string RenderBase = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";

        // Clés de propriété Windows (PKEY), au format « {fmtid},pid ».
        private const string PkeyName = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";  // nom lisible
        private const string PkeyDisableSysFx = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";  // 1 = améliorations coupées
        private const string PkeyExclusiveAllow = "{b3f8fa53-0004-438e-9003-51a46e139bfc},3";  // 1 = mode exclusif autorisé
        private const string PkeyExclusivePrio = "{b3f8fa53-0004-438e-9003-51a46e139bfc},4";  // 1 = priorité à l'exclusif

        /// <summary>État à trois valeurs : on ne confond jamais « mauvais » et « inconnu ».</summary>
        public enum Etat { Inconnu, Bon, Mauvais }

        public sealed class Sortie
        {
            public string Nom;
            public Etat Ameliorations;   // Bon = coupées
            public Etat Exclusif;        // Bon = autorisé
        }

        /// <summary>
        /// Interprétation PURE d'une valeur de registre (testable sans matériel).
        /// <paramref name="bonQuand"/> est la valeur qui correspond à l'état souhaité.
        /// Une valeur absente ou illisible reste INCONNUE — jamais un constat.
        /// </summary>
        public static Etat Lire(object valeur, int bonQuand)
        {
            if (valeur == null) return Etat.Inconnu;
            try
            {
                int v = Convert.ToInt32(valeur);
                return v == bonQuand ? Etat.Bon : Etat.Mauvais;
            }
            catch { return Etat.Inconnu; }
        }

        /// <summary>Périphériques de sortie ACTIFS et leur état. Liste vide si rien n'est lisible.</summary>
        public static List<Sortie> Sorties()
        {
            var list = new List<Sortie>();
            try
            {
                using (RegistryKey racine = Registry.LocalMachine.OpenSubKey(RenderBase, false))
                {
                    if (racine == null) return list;
                    foreach (string id in racine.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey dev = racine.OpenSubKey(id, false))
                            {
                                if (dev == null) continue;
                                object st = dev.GetValue("DeviceState");
                                if (st == null || Convert.ToInt32(st) != 1) continue;   // 1 = actif
                                using (RegistryKey props = dev.OpenSubKey("Properties", false))
                                {
                                    if (props == null) continue;
                                    string nom = Convert.ToString(props.GetValue(PkeyName)) ?? "";
                                    if (nom.Length == 0) nom = id;
                                    list.Add(new Sortie
                                    {
                                        Nom = nom,
                                        Ameliorations = Lire(props.GetValue(PkeyDisableSysFx), 1),
                                        Exclusif = Lire(props.GetValue(PkeyExclusiveAllow), 1)
                                    });
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>Sorties dont le mode exclusif est EXPLICITEMENT refusé.</summary>
        public static List<Sortie> ExclusifRefuse(List<Sortie> sorties)
        {
            var r = new List<Sortie>();
            if (sorties != null)
                foreach (var s in sorties) if (s != null && s.Exclusif == Etat.Mauvais) r.Add(s);
            return r;
        }

        /// <summary>Sorties dont les améliorations sont EXPLICITEMENT actives.</summary>
        public static List<Sortie> AmeliorationsActives(List<Sortie> sorties)
        {
            var r = new List<Sortie>();
            if (sorties != null)
                foreach (var s in sorties) if (s != null && s.Ameliorations == Etat.Mauvais) r.Add(s);
            return r;
        }

        /// <summary>Noms, pour l'affichage (au plus <paramref name="max"/>).</summary>
        public static string Noms(List<Sortie> sorties, int max)
        {
            if (sorties == null || sorties.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var s in sorties)
            {
                if (n >= max) { sb.Append(" …"); break; }
                if (n++ > 0) sb.Append(", ");
                sb.Append('«').Append(' ').Append(s.Nom).Append(' ').Append('»');
            }
            return sb.ToString();
        }
    }
}
