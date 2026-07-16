using System;

namespace BTOptimizer
{
    /// <summary>Une optimisation individuelle, cochable dans l'interface.</summary>
    public class Tweak
    {
        public string Id;
        public string Category;
        public string Name;
        public string Desc;
        public bool Recommended;   // coche par le preset "Recommandé"
        public bool Esport;        // coche par le preset "eSport"
        public bool Reboot;        // nécessite un redémarrage
        public string[] BackupKeys = new string[0];
        public Action Apply;
        public Action Revert;
        public Func<bool?> Check;  // true = déjà actif, false = inactif, null = indéterminé
    }

    public static class Cat
    {
        public const string Souris   = "Souris & clavier";
        public const string Alim     = "Alimentation & CPU";
        public const string Gpu      = "GPU & jeux";
        public const string Systeme  = "Système & planificateur";
        public const string Rapidite = "Rapidité & démarrage";
        public const string Services = "Services & arrière-plan";
        public const string Reseau   = "Réseau";

        public static readonly string[] Order = new string[]
        {
            Souris, Alim, Gpu, Systeme, Rapidite, Services, Reseau
        };
    }
}
