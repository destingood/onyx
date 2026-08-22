using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="InventaireServices"/>.
    ///
    /// L'essentiel de ce banc porte sur ce que le module REFUSE de faire. Un module qui arrête des
    /// services se juge d'abord là-dessus : arrêter l'anticheat d'un jeu compétitif, couper le
    /// service de manette ou désactiver un service tiers qu'on n'a pas identifié coûte plus cher
    /// que tout ce qu'un tel outil peut rapporter.
    ///
    /// Le geste réel passe par <see cref="InventaireServices.IActions"/>, injecté. C'est ce qui
    /// permet de prouver ici qu'un anticheat n'est jamais touché — sans en arrêter un seul.
    /// </summary>
    internal static class InventaireServicesTests
    {
        /// <summary>Un faux exécutant : il n'agit pas, il note. Suffit à vérifier QUI aurait été
        /// touché, ce qui est exactement la question.</summary>
        private sealed class FauxActions : InventaireServices.IActions
        {
            public readonly List<string> Arretes = new List<string>();
            public readonly List<string> Configures = new List<string>();
            public readonly List<string> Demarres = new List<string>();
            public readonly HashSet<string> Interdits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public void Configure(string service, string type, bool arrete, bool demarre)
            {
                Configures.Add(service + "=" + type);
            }
            public void Arrete(string service) { Arretes.Add(service); }
            public void Demarre(string service) { Demarres.Add(service); }
            public bool Interdit(string service) { return Interdits.Contains(service); }
        }

        private static InventaireServices.Service Svc(string nom, string chemin, bool enCours, int start)
        {
            var s = new InventaireServices.Service
            {
                Nom = nom, Libelle = nom, Chemin = chemin, EnCours = enCours, Start = start
            };
            InventaireServices.Classe(s);
            return s;
        }

        private const string SousWindows = @"C:\Windows\System32\svchost.exe -k netsvcs";
        private const string SousTiers = @"""C:\Program Files\Bidule\bidule.exe"" --service";

        public static void Tout()
        {
            Anticheats();
            Vitaux();
            SondesConnues();
            Tiers();
            Classement();
            RienAGagner();
            Tri();
            JournalAllerRetour();
            GesteProtege();
            Restauration();
        }

        // ------------------------------------------------------------------ règle 1

        private static void Anticheats()
        {
            Banc.Titre("Anticheats : jamais touchés");

            Banc.Verifie("vgc (Vanguard) reconnu par son nom", true,
                InventaireServices.EstAnticheat("vgc", "", ""));
            Banc.Verifie("BattlEye reconnu par son libellé", true,
                InventaireServices.EstAnticheat("BEService", "BattlEye Service", ""));

            // Le cas qui compte : un anticheat qu'aucune table ne connaît. Il doit être protégé
            // par son CHEMIN, sinon la première nouveauté du marché passe entre les mailles.
            Banc.Verifie("un anticheat inconnu est reconnu par son chemin", true,
                InventaireServices.EstAnticheat("SvcX7", "Service X7",
                    @"C:\Program Files\Jeu\EasyAntiCheat\eac.exe"));

            Banc.Verifie("un service ordinaire n'est pas pris pour un anticheat", false,
                InventaireServices.EstAnticheat("DiagTrack", "Expériences des utilisateurs connectés", SousWindows));

            var s = Svc("vgc", @"C:\Program Files\Riot Vanguard\vgc.exe", true, 2);
            Banc.Verifie("un anticheat est classé VITAL", true,
                s.Verdict == InventaireServices.Verdict.Vital);
            Banc.Verifie("un anticheat n'est jamais proposé", false, InventaireServices.Proposable(s));
        }

        private static void Vitaux()
        {
            Banc.Titre("Ce que Windows et le jeu réclament");

            foreach (string n in new[] { "RpcSs", "Audiosrv", "Dnscache", "XboxGipSvc", "hidserv", "bthserv", "WinDefend" })
                Banc.Verifie(n + " est intouchable", true, InventaireServices.EstVital(n, "", SousWindows));

            // La manette filaire Xbox et le Bluetooth sont dans toutes les listes de « services à
            // couper » ; on y perd sa manette, pas de la latence.
            var manette = Svc("XboxGipSvc", SousWindows, true, 2);
            Banc.Verifie("le service de manette n'est pas proposable", false,
                InventaireServices.Proposable(manette));

            Banc.Verifie("un service sans nom est traité comme vital (on ne touche pas à l'inconnu)",
                true, InventaireServices.EstVital("", "", ""));

            Banc.Verifie("la télémétrie, elle, n'est pas vitale", false,
                InventaireServices.EstVital("DiagTrack", "", SousWindows));

            // La plate-forme de Defender vit sous ProgramData, donc HORS de system32 : elle
            // arrivait en « tiers inconnu » sur la machine de référence. Et un antivirus tiers
            // n'est dans aucune table — se tromper de ce côté-là désarme la machine.
            Banc.Verifie("Defender est protégé même hors de system32", true,
                InventaireServices.EstVital("WdNisSvc", "Antimalware Network Inspection",
                    @"C:\ProgramData\Microsoft\Windows Defender\Platform\4.18\NisSrv.exe"));
            Banc.Verifie("un antivirus tiers inconnu est protégé par son éditeur", true,
                InventaireServices.EstSecurite("SvcQ", "Bidule Antivirus Service", ""));
            Banc.Verifie("un service ordinaire n'est pas pris pour un antivirus", false,
                InventaireServices.EstSecurite("WSearch", "Windows Search", SousWindows));
        }

        /// <summary>Le raccord avec le Mode Jeu : ce qu'il suspend déjà ne doit pas être déclaré
        /// « inconnu » ici. Deux verdicts contraires sur le même service, c'est l'un des deux qui
        /// ment.</summary>
        private static void SondesConnues()
        {
            Banc.Titre("Ce que le Mode Jeu suspend déjà");

            var avant = Svc("SondeBidule", SousTiers, true, 2);
            Banc.Verifie("sans raccord, une sonde tierce reste inconnue", true,
                avant.Verdict == InventaireServices.Verdict.Inconnu);

            InventaireServices.SuspendableAussi(new[] { "SondeBidule" });

            var apres = Svc("SondeBidule", SousTiers, true, 2);
            Banc.Verifie("une fois annoncée, elle devient suspendable", true,
                apres.Verdict == InventaireServices.Verdict.Suspendable);
            Banc.Verifie("et elle est enfin proposable", true, InventaireServices.Proposable(apres));

            // Le raccord ne doit PAS pouvoir contourner la protection.
            InventaireServices.SuspendableAussi(new[] { "Audiosrv" });
            Banc.Verifie("annoncer un service vital ne le rend pas arrêtable", true,
                Svc("Audiosrv", SousWindows, true, 2).Verdict == InventaireServices.Verdict.Vital);
        }

        // ------------------------------------------------------------------ tiers

        private static void Tiers()
        {
            Banc.Titre("Reconnaître ce qui ne vient pas de Windows");

            Banc.Verifie("system32 n'est pas un tiers", false, InventaireServices.EstTiers(SousWindows));
            Banc.Verifie("SysWOW64 non plus", false,
                InventaireServices.EstTiers(@"C:\Windows\SysWOW64\svchost.exe"));
            Banc.Verifie("\\SystemRoot non plus", false,
                InventaireServices.EstTiers(@"\SystemRoot\System32\drivers\truc.sys"));
            Banc.Verifie("Program Files est un tiers", true, InventaireServices.EstTiers(SousTiers));
            Banc.Verifie("un chemin vide n'est pas déclaré tiers", false, InventaireServices.EstTiers(""));

            Banc.Egal("le binaire est extrait d'une ligne de commande entre guillemets",
                @"C:\Program Files\Bidule\bidule.exe", InventaireServices.BinaireSeul(SousTiers));
            Banc.Egal("les arguments sont écartés",
                @"C:\Windows\System32\svchost.exe", InventaireServices.BinaireSeul(SousWindows));
            Banc.Egal("une ligne vide ne fait pas d'exception", "", InventaireServices.BinaireSeul(null));
        }

        private static void Classement()
        {
            Banc.Titre("Classement");

            Banc.Verifie("la télémétrie est inutile sur un PC de jeu",
                true, Svc("DiagTrack", SousWindows, true, 2).Verdict == InventaireServices.Verdict.Inutile);
            Banc.Verifie("l'indexation est suspendable, pas inutile",
                true, Svc("WSearch", SousWindows, true, 2).Verdict == InventaireServices.Verdict.Suspendable);

            // Deux services que les listes populaires font couper, et qu'ONYX garde : l'un fait
            // démarrer les vieux jeux, l'autre alimente sa propre analyse de plantage.
            Banc.Verifie("l'assistant de compatibilité est gardé",
                true, Svc("PcaSvc", SousWindows, true, 3).Verdict == InventaireServices.Verdict.Utile);
            Banc.Verifie("les rapports d'erreur sont gardés (ONYX les lit)",
                true, Svc("WerSvc", SousWindows, false, 3).Verdict == InventaireServices.Verdict.Utile);

            Banc.Verifie("une suite constructeur est suspendable", true,
                Svc("CorsairService", @"C:\Program Files\Corsair\iCUE\service.exe", true, 2).Verdict
                    == InventaireServices.Verdict.Suspendable);
            Banc.Verifie("un service de mise à jour est suspendable", true,
                Svc("BiduleUpdateService", @"C:\Program Files\Bidule\update.exe", true, 2).Verdict
                    == InventaireServices.Verdict.Suspendable);

            // Le cœur de la prudence : un tiers qu'on n'identifie pas reste INCONNU. Le classer
            // « inutile » reviendrait à décider à la place de qui l'a installé.
            var inconnu = Svc("ZorgSvc", SousTiers, true, 2);
            Banc.Verifie("un tiers non identifié reste inconnu",
                true, inconnu.Verdict == InventaireServices.Verdict.Inconnu);
            Banc.Verifie("un tiers inconnu n'est pas proposé", false, InventaireServices.Proposable(inconnu));

            Banc.Verifie("un service Windows non catalogué est laissé tranquille", true,
                Svc("ServiceMaisonDeWindows", SousWindows, true, 2).Verdict == InventaireServices.Verdict.Utile);
        }

        // ------------------------------------------------------------------ règle 2

        private static void RienAGagner()
        {
            Banc.Titre("Ne rien proposer qui ne rapporte rien");

            var arrete = Svc("WSearch", SousWindows, false, 3);
            Banc.Verifie("un service à l'arrêt n'est pas proposé", false, InventaireServices.Proposable(arrete));
            Banc.Verifie("et le texte le dit au lieu de promettre un gain", true,
                InventaireServices.Gain(arrete).IndexOf("ne libérerait donc rien", StringComparison.Ordinal) > 0);

            var deja = Svc("DiagTrack", SousWindows, false, 4);
            Banc.Egal("un service déjà désactivé annonce zéro gain", "déjà désactivé : rien à gagner",
                InventaireServices.Gain(deja));

            var actif = Svc("WSearch", SousWindows, true, 2);
            actif.RamMo = 42; actif.Cpu = 3.5; actif.Partage = 1;
            Banc.Verifie("un service actif est proposé", true, InventaireServices.Proposable(actif));
            Banc.Egal("son coût est chiffré", "42 Mo, 3.5 % du processeur",
                InventaireServices.Gain(actif));

            // Un svchost partagé ne se découpe pas : le dire vaut mieux que d'afficher quatre fois
            // la même mémoire comme si elle était quatre fois libérable.
            var partage = Svc("WSearch", SousWindows, true, 2);
            partage.RamMo = 30; partage.Partage = 3;
            Banc.Verifie("le partage de processus est annoncé", true,
                InventaireServices.Gain(partage).IndexOf("estimé", StringComparison.Ordinal) > 0);
        }

        private static void Tri()
        {
            Banc.Titre("Ordre d'affichage");

            var inutile = Svc("DiagTrack", SousWindows, true, 2);
            var suspend = Svc("WSearch", SousWindows, true, 2);
            var vital = Svc("RpcSs", SousWindows, true, 2);

            Banc.Verifie("l'inutile passe avant le suspendable", true,
                InventaireServices.Compare(inutile, suspend) < 0);
            Banc.Verifie("le vital finit en bas", true,
                InventaireServices.Compare(suspend, vital) < 0);

            // Relevé sur une capture de l'écran : le haut du tableau était occupé par huit services
            // « inutiles ici » DÉJÀ désactivés, dont la colonne coût répétait « rien à gagner ».
            // Le classement disait donc de regarder d'abord ce qui ne rapporte rien.
            var inutileEteint = Svc("DiagTrack", SousWindows, false, 4);
            var suspendActif = Svc("WSearch", SousWindows, true, 2);
            Banc.Verifie("un inutile déjà désactivé passe SOUS un suspendable qui tourne", true,
                InventaireServices.Compare(inutileEteint, suspendActif) > 0);

            var gros = Svc("DiagTrack", SousWindows, true, 2); gros.RamMo = 200;
            var petit = Svc("dmwappushservice", SousWindows, true, 2); petit.RamMo = 5;
            Banc.Verifie("à verdict égal, le plus gourmand est en haut", true,
                InventaireServices.Compare(gros, petit) < 0);

            Banc.Verifie("comparer à un absent ne lève pas d'exception", true,
                InventaireServices.Compare(gros, null) == 0);
        }

        // ------------------------------------------------------------------ marche arrière

        private static void JournalAllerRetour()
        {
            Banc.Titre("Journal : de quoi revenir en arrière");

            var t = new InventaireServices.Trace
            {
                Nom = "DiagTrack", StartAvant = 2, TournaitAvant = true, Action = "desactive",
                Quand = new DateTime(2026, 8, 22, 14, 5, 9)
            };
            InventaireServices.Trace relu = InventaireServices.Relit(InventaireServices.Ligne(t));
            Banc.Egal("le nom survit à l'aller-retour", "DiagTrack", relu.Nom);
            Banc.Verifie("le type de démarrage d'avant aussi", true, relu.StartAvant == 2);
            Banc.Verifie("le fait qu'il tournait aussi", true, relu.TournaitAvant);

            // Un journal tronqué doit rendre les lignes qui restent, pas empêcher toute
            // restauration : une ligne abîmée est ignorée, pas fatale.
            Banc.Verifie("une ligne abîmée est ignorée", true, InventaireServices.Relit("n'importe quoi") == null);
            Banc.Verifie("une ligne vide aussi", true, InventaireServices.Relit("") == null);

            Banc.Egal("2 se réécrit en automatique", "auto", InventaireServices.TypeDemarrage(2));
            Banc.Egal("4 se réécrit en désactivé", "disabled", InventaireServices.TypeDemarrage(4));
            Banc.Egal("tout le reste retombe sur manuel", "demand", InventaireServices.TypeDemarrage(3));
        }

        /// <summary>Le test le plus important du fichier : ce qui aurait été touché, et ce qui ne
        /// l'a pas été. Sur un faux exécutant, donc sans arrêter quoi que ce soit.</summary>
        private static void GesteProtege()
        {
            Banc.Titre("Appliquer : ce qui est protégé ne bouge pas");

            string journal = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "onyx-banc-services-" + Guid.NewGuid().ToString("N") + ".txt");
            InventaireServices.CheminJournal = journal;
            try
            {
                var actions = new FauxActions();
                actions.Interdits.Add("SensorService");   // ce que ServiceGuard interdit en vrai

                var plan = new List<InventaireServices.Service>
                {
                    Svc("DiagTrack", SousWindows, true, 2),
                    Svc("vgc", @"C:\Program Files\Riot Vanguard\vgc.exe", true, 2),
                    Svc("Audiosrv", SousWindows, true, 2),
                    Svc("SensorService", SousWindows, true, 3)
                };

                int n = InventaireServices.Applique(plan, InventaireServices.Geste.Suspendre, actions, null);

                Banc.Verifie("un seul service sur quatre a été touché", true, n == 1);
                Banc.Verifie("c'est bien la télémétrie", true, actions.Arretes.Contains("DiagTrack"));
                Banc.Verifie("l'anticheat n'a PAS été arrêté", false, actions.Arretes.Contains("vgc"));
                Banc.Verifie("le service audio non plus", false, actions.Arretes.Contains("Audiosrv"));
                Banc.Verifie("le service interdit par le garde-fou non plus", false,
                    actions.Arretes.Contains("SensorService"));

                // « Suspendre » ne doit RIEN reconfigurer : c'est toute la différence avec
                // « désactiver », et c'est ce qui rend le geste sans dette.
                Banc.Verifie("suspendre ne reconfigure aucun démarrage", true, actions.Configures.Count == 0);
                Banc.Verifie("le journal a retenu le geste", true, InventaireServices.Journal().Count == 1);
            }
            finally
            {
                try { if (System.IO.File.Exists(journal)) System.IO.File.Delete(journal); } catch { }
                InventaireServices.CheminJournal = null;
            }
        }

        private static void Restauration()
        {
            Banc.Titre("Restaurer : l'état exact d'avant");

            string journal = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "onyx-banc-services-" + Guid.NewGuid().ToString("N") + ".txt");
            InventaireServices.CheminJournal = journal;
            try
            {
                var actions = new FauxActions();

                // Deux services désactivés : l'un tournait (à relancer), l'autre pas.
                var plan = new List<InventaireServices.Service>
                {
                    Svc("DiagTrack", SousWindows, true, 2),
                    Svc("Fax", SousWindows, false, 3)
                };
                // Fax est à l'arrêt : Proposable dit non, mais si l'utilisateur le coche quand même
                // le geste doit rester correct — et surtout réversible.
                InventaireServices.Applique(plan, InventaireServices.Geste.Desactiver, actions, null);
                Banc.Verifie("désactiver reconfigure les deux", true, actions.Configures.Count == 2);

                var retour = new FauxActions();
                int n = InventaireServices.Restaure(retour, null);

                Banc.Verifie("les deux sont restaurés", true, n == 2);
                Banc.Verifie("la télémétrie retrouve son démarrage automatique", true,
                    retour.Configures.Contains("DiagTrack=auto"));
                Banc.Verifie("le fax retrouve son démarrage manuel", true,
                    retour.Configures.Contains("Fax=demand"));
                Banc.Verifie("seul celui qui tournait est relancé", true, retour.Demarres.Count == 1);
                Banc.Verifie("et c'est le bon", true, retour.Demarres.Contains("DiagTrack"));

                // Le journal est vidé : restaurer deux fois ne doit pas rejouer des gestes qui
                // n'ont plus lieu d'être.
                Banc.Verifie("le journal est vidé après restauration", true, InventaireServices.Journal().Count == 0);
            }
            finally
            {
                try { if (System.IO.File.Exists(journal)) System.IO.File.Delete(journal); } catch { }
                InventaireServices.CheminJournal = null;
            }
        }
    }
}
