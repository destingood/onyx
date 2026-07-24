using System;
using System.Collections.Generic;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Détermine la CAUSE EXACTE d'un crash, pour n'importe quelle app ou jeu, à partir du
    /// module fautif et du code d'exception que Windows enregistre (Application Error 1000).
    /// Base de connaissance locale : module (la DLL qui a lâché) + code → cause probable + remède
    /// gratuit. 100 % local. L'IA/le web peuvent enrichir un module inconnu (voir ChatActions).
    /// </summary>
    internal static class CrashAnalyzer
    {
        /// <summary>Résultat lisible d'une analyse : cause, remède, et éventuel correctif câblable.</summary>
        public sealed class Diag
        {
            public string Cause;       // « Pilote graphique NVIDIA » …
            public string Remedy;      // remède gratuit, concret
            public string Fix;         // clé de correctif : "libs" | "gpu" | "system" | "ram" | "game" | "anticheat" | null
            public bool Known;         // true si le module a été reconnu (sinon → enrichir via IA/web)
        }

        // Sens des codes d'exception les plus fréquents.
        public static string CodeMeaning(string code)
        {
            switch ((code ?? "").ToLowerInvariant().TrimStart('0', 'x'))
            {
                case "c0000005": return "violation d'accès mémoire (le programme a touché une zone interdite)";
                case "c0000409": return "dépassement de tampon (protection anti-corruption déclenchée)";
                case "c0000374": return "corruption du tas mémoire (heap)";
                case "c00000fd": return "débordement de pile (stack overflow)";
                case "e0434352": return "exception .NET non gérée";
                case "e06d7363": return "exception C++ non gérée";
                case "c000041d": return "exception non gérée pendant un rappel système";
                case "c0000006": return "erreur de pagination (in-page, souvent disque/lecture)";
                case "c0000017": return "mémoire insuffisante";
                case "80000003": return "point d'arrêt (souvent lié à un overlay/anti-triche)";
                default: return "";
            }
        }

        // Cause + remède à partir du MODULE fautif (et du code). L'ordre va du plus spécifique au général.
        public static Diag FromModule(string exe, string module, string code)
        {
            string m = (module ?? "").ToLowerInvariant();
            string e = (exe ?? "").ToLowerInvariant();
            string c = (code ?? "").ToLowerInvariant();

            // --- Pilote graphique (toutes marques) : cause n°1 des « dispositif de rendu perdu » ---
            if (Has(m, "nvlddmkm", "nvwgf2um", "nvoglv", "nvd3dum", "nvcuda", "nvapi"))
                return new Diag { Cause = "le pilote graphique NVIDIA (" + module + ")", Fix = "gpu", Known = true,
                    Remedy = "réinstalle le pilote PROPREMENT avec DDU (gratuit, 1 clic depuis Bibliothèques), puis pilote NVIDIA à jour. Surveille aussi la température (un GPU trop chaud plante pareil)." };
            if (Has(m, "amdvlk", "atidxx", "amdxc", "aticfx", "amdkmdag", "atiumd", "amdihk"))
                return new Diag { Cause = "le pilote graphique AMD (" + module + ")", Fix = "gpu", Known = true,
                    Remedy = "réinstalle le pilote PROPREMENT avec DDU (gratuit), puis Adrenalin à jour. Vérifie la température du GPU." };
            if (Has(m, "igd", "igdumd", "ig9icd", "igdgmm", "igdml", "igxelpicd"))
                return new Diag { Cause = "le pilote graphique Intel (" + module + ")", Fix = "gpu", Known = true,
                    Remedy = "mets à jour le pilote Intel Arc/Graphics (gratuit), ou réinstalle-le proprement (DDU)." };

            // --- DirectX / rendu ---
            if (Has(m, "d3d12", "d3d11", "d3d10", "d3d9", "dxgi", "d3dx", "dxcore"))
                return new Diag { Cause = "la couche graphique DirectX (" + module + ")", Fix = "libs", Known = true,
                    Remedy = "installe le runtime DirectX (gratuit, pré-coché dans Bibliothèques) et mets à jour ton pilote GPU." };

            // --- Runtimes Visual C++ / .NET (composants manquants ou corrompus) ---
            if (Has(m, "msvcp", "msvcr", "vcruntime", "vccorlib", "mfc", "concrt"))
                return new Diag { Cause = "un runtime Visual C++ manquant ou corrompu (" + module + ")", Fix = "libs", Known = true,
                    Remedy = "réinstalle les Visual C++ Redistributable (gratuit, 1 clic dans Bibliothèques) — cause classique d'un jeu qui plante au lancement ou en cours." };
            if (Has(m, "clr", "mscoree", "coreclr", "clrjit", "system.") || c == "e0434352")
                return new Diag { Cause = "le framework .NET (" + (module ?? ".NET") + ", exception .NET)", Fix = "libs", Known = true,
                    Remedy = "installe/répare le .NET Desktop Runtime (gratuit, dans Bibliothèques). Si c'est une appli précise, réinstalle-la." };

            // --- Anti-triche ---
            if (Has(m, "easyanticheat", "eac", "beservice", "battleye", "vgc", "vgk", "vanguard"))
                return new Diag { Cause = "l'anti-triche du jeu (" + module + ")", Fix = "anticheat", Known = true,
                    Remedy = "répare/réinstalle l'anti-triche depuis le dossier du jeu (Steam : Propriétés → Fichiers locaux, ou le lanceur), et redémarre. Assure-toi qu'aucun autre anti-triche ne tourne en même temps." };

            // --- Audio (souvent des plantages en jeu liés au périphérique) ---
            if (Has(m, "audioeng", "audiodg", "audioses", "wdmaud"))
                return new Diag { Cause = "le moteur audio de Windows (" + module + ")", Fix = "system", Known = true,
                    Remedy = "mets à jour le pilote de ta carte son/casque, et relance le service audio (dis « plus de son »)." };

            // --- Corruption mémoire / système générique (ntdll, kernel) ---
            if (Has(m, "ntdll", "kernelbase", "kernel32", "wow64", "win32u"))
            {
                bool acc = c == "c0000005" || c == "c0000374" || c == "c00000fd";
                return new Diag { Cause = "un plantage bas niveau via " + module + (acc ? " (accès mémoire invalide)" : ""), Fix = acc ? "ram" : "system", Known = true,
                    Remedy = acc
                        ? "souvent une RAM instable (profil XMP/EXPO trop agressif → teste avec MemTest86, gratuit), un overclock, ou un overlay tiers. Répare aussi les fichiers Windows (DISM + SFC, gratuit)."
                        : "répare l'intégrité de Windows (DISM + SFC, gratuit) et mets à jour l'appli concernée." };
            }

            // --- Le module fautif EST l'application elle-même → bug interne du programme/jeu ---
            if (m.Length > 0 && (m == e || (m.EndsWith(".exe") && e.Contains(m.Replace(".exe", "")))))
                return new Diag { Cause = "le programme lui-même (" + exe + ", bug interne)", Fix = "game", Known = true,
                    Remedy = "mets le jeu/l'appli à jour et VÉRIFIE l'intégrité de ses fichiers (Steam : clic droit → Propriétés → Fichiers locaux → Vérifier). Sur un jeu moddé, désactive les mods." };

            // --- Composants embarqués fréquents ---
            if (Has(m, "python")) return new Diag { Cause = "un composant Python de l'appli (" + module + ")", Fix = "game", Known = true,
                Remedy = "mets l'application à jour ou réinstalle-la : un de ses composants (Python) plante. Vérifie l'intégrité de ses fichiers." };
            if (Has(m, "unityplayer")) return new Diag { Cause = "le moteur Unity du jeu (" + module + ")", Fix = "game", Known = true,
                Remedy = "mets le jeu à jour et vérifie ses fichiers ; mets aussi le pilote GPU à jour." };
            if (Has(m, "ue4", "ue5", "unreal")) return new Diag { Cause = "le moteur Unreal du jeu (" + module + ")", Fix = "game", Known = true,
                Remedy = "vérifie l'intégrité des fichiers du jeu et mets le pilote GPU à jour ; réinstalle les Visual C++ (gratuit)." };

            // Inconnu : on le dit honnêtement (ChatActions peut enrichir via IA/web).
            return new Diag { Cause = module != null && module.Length > 0 ? "le module « " + module + " »" : "un module non identifié", Fix = null, Known = false,
                Remedy = "je ne reconnais pas ce module de tête. Répare Windows (DISM + SFC), mets à jour le pilote GPU et l'appli. Je peux aussi chercher ce module sur le web si tu veux (« active internet »)." };
        }

        private static bool Has(string s, params string[] ks) { foreach (var k in ks) if (s.Contains(k)) return true; return false; }
    }
}
