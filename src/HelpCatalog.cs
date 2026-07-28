using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Catalogue partagé « symptôme → outil » : la logique du Doc. Utilisé à la fois par
    /// HelpNavForm (fenêtre « J'ai un problème… ») et par la page Consultation du shell, pour
    /// éviter toute duplication. Chaque entrée ouvre directement le panneau qui traite le souci.
    /// </summary>
    internal static class HelpCatalog
    {
        public class Entry
        {
            public string Group;    // catégorie
            public string Symptom;  // ce que ressent l'utilisateur
            public string Tool;     // nom de l'outil qui traite
            public Func<Form> Open;  // fabrique la fenêtre à ouvrir
            public Entry(string g, string s, string t, Func<Form> o) { Group = g; Symptom = s; Tool = t; Open = o; }
        }

        public static List<Entry> Entries(Action<string, int> log)
        {
            var L = log;
            return new List<Entry>
            {
                new Entry("Crashs & instabilité", "Bilan complet : je ne sais pas par où commencer", "Santé de mon PC", () => new HealthForm(L)),
                new Entry("Crashs & instabilité", "Mes jeux crashent ou se ferment tout seuls", "Stabilité (14 j)", () => new StabilityForm(L)),
                new Entry("Crashs & instabilité", "« Dispositif de rendu perdu » / freeze", "Boutiques / crashs", () => new ShopFixForm(L)),
                new Entry("Crashs & instabilité", "Le PC ou le GPU chauffe / bride (throttling)", "Températures & throttling", () => new ThermalForm(L)),
                new Entry("Crashs & instabilité", "Un ancien « optimiseur » a peut-être cassé des réglages", "Réglages néfastes", () => new CheckupForm(L)),

                new Entry("Performance & fluidité", "Ça rame / ça saccade en jeu", "Qui ralentit mon PC", () => new BloatForm(L)),
                new Entry("Performance & fluidité", "Mes FPS sont bas", "FPS en direct", () => new FpsMonForm(L)),
                new Entry("Performance & fluidité", "Je veux mesurer la puissance de mon PC", "Benchmark rapide", () => new BenchForm(L)),
                new Entry("Performance & fluidité", "Chargements longs / jeux sur le bon disque ?", "Jeux & disques", () => new DiskForm(L)),
                new Entry("Performance & fluidité", "Input lag / réactivité de la souris", "Latence en direct", () => new LiveMonForm(L)),

                new Entry("Réseau & ping", "Ça lag en ligne (ping, gigue, décrochages)", "Qualité réseau", () => new NetworkForm(L)),
                new Entry("Réseau & ping", "Voir OÙ le lag apparaît sur le trajet", "Trajet réseau", () => new NetRouteForm(L)),
                new Entry("Réseau & ping", "Téléchargements lents / Steam charge à l'infini", "Réglages TCP/IP", () => new NetTuneForm(L)),
                new Entry("Réseau & ping", "Réduire la latence de ma carte réseau", "Carte réseau", () => new NetAdapterForm(L)),
                new Entry("Réseau & ping", "Changer / tester mon DNS", "DNS rapide", () => new DnsForm(L)),
                new Entry("Réseau & ping", "Je suis en 4G/5G (box mobile) et ça lag / ça décroche", "Connexion 4G/5G", () => new MobileNetForm(L)),

                new Entry("Jeux & lancement", "Un jeu refuse de démarrer (dll manquante)", "Bibliothèques de jeu", () => new LibsForm(L)),
                new Entry("Jeux & lancement", "Boutique en jeu vide / qui tourne à l'infini", "Boutiques / crashs", () => new ShopFixForm(L)),
                new Entry("Jeux & lancement", "Booster mon jeu principal (priorité CPU)", "Priorité par jeu", () => new GameProfileForm(L)),
                new Entry("Jeux & lancement", "Moins de saccades (analyse antivirus des jeux)", "Exclusions antivirus", () => new DefenderForm(L)),

                new Entry("Écran & périphériques", "Mon écran semble bloqué à 60 Hz", "Réglages d'écran", () => new DisplayForm(L)),
                new Entry("Écran & périphériques", "Ma souris est-elle vraiment à 1000 Hz ?", "Fréquence de la souris", () => new MouseForm(L)),

                new Entry("Entretien & sécurité", "PC lent à démarrer (trop de programmes au boot)", "Programmes au démarrage", () => new StartupForm(L)),
                new Entry("Entretien & sécurité", "Libérer de l'espace disque", "Nettoyage disque", () => new CleanupForm(L)),
                new Entry("Entretien & sécurité", "Faire une sauvegarde avant de bidouiller", "Points de restauration", () => new RestoreForm(L)),
                new Entry("Entretien & sécurité", "Windows refuse de lancer une application (« stratégie de contrôle d'application »)", "Smart App Control", () => new SmartAppControlForm(L)),
            };
        }
    }
}
