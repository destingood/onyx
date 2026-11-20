using System;

namespace BTOptimizer
{
    /// <summary>Ce que coûte un réglage à l'utilisateur, hors performance.</summary>
    public enum Risque
    {
        Aucun,                 // rien à perdre : annulable à l'identique, aucun effet de bord
        Securite,              // baisse une protection (Spectre, VBS, mises à jour retardées, LLMNR…)
        Fonction,              // coupe une fonction que l'utilisateur peut remarquer
        AnnulationNonGarantie  // « Rétablir » ne rend pas l'état d'origine à coup sûr (écriture non journalisée)
    }

    /// <summary>Ce qu'on sait du gain d'un réglage. Nommé NiveauPreuve : « Preuve » est déjà la classe de l'optimisation prouvée.</summary>
    public enum NiveauPreuve
    {
        NonMesure,   // gain non démontré
        Mesure,      // gain mesuré (source citée dans docs/)
        DependDuPc,  // l'effet dépend de la machine : sans risque, mais à mesurer chez soi
        SansObjet    // pas un réglage de performance (confidentialité, confort de saisie)
    }

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
        public Risque Risque = Risque.Aucun;
        public NiveauPreuve Preuve = NiveauPreuve.NonMesure;
        /// <summary>Préréglage « Sûr » : annulable à l'identique, sans effet de bord, gain mesuré ou sans enjeu de performance.</summary>
        public bool Sur { get { return Risque == Risque.Aucun && Preuve != NiveauPreuve.NonMesure; } }
    }

    public static class Cat
    {
        public const string Souris   = "🖱 Souris & clavier";
        public const string Alim     = "⚡ Alimentation & CPU";
        public const string Gpu      = "🎮 GPU & jeux";
        public const string Audio    = "🔊 Son & audio";
        public const string Systeme  = "🧠 Système & planificateur";
        public const string Rapidite = "🚀 Rapidité & démarrage";
        public const string Services = "🧹 Services & arrière-plan";
        public const string Privacy  = "🕵 Confidentialité";
        public const string Reseau   = "🌐 Réseau";
        public const string Stockage = "💽 Stockage";

        public static readonly string[] Order = new string[]
        {
            Souris, Alim, Gpu, Audio, Systeme, Rapidite, Services, Privacy, Reseau, Stockage
        };
    }
}
