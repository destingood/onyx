using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// LA LIAISON D'UNE CLÉ À SON PC — décidée ici, appliquée dans <see cref="License"/>.
    ///
    /// Séparé pour une raison précise : la classe License lit des fichiers et le registre dès
    /// qu'on la touche. Une politique de licence doit pouvoir se rejouer au banc d'essai sans
    /// écrire une ligne sur la machine de qui la vérifie.
    /// </summary>
    internal static class LicenceLiaison
    {
        // ==================================================================
        //  Ce qu'on retient localement : le jeton, ET à quel PC il s'est lié
        // ==================================================================

        /// <summary>Le jeton et l'histoire de sa liaison. Écrit en clair, exprès : ce n'est pas un
        /// secret, c'est une mémoire.</summary>
        public sealed class Enregistrement
        {
            public string Token = "";
            /// <summary>PC auquel la clé est liée aujourd'hui.</summary>
            public string Machine = "";
            /// <summary>Première activation, tous PC confondus.</summary>
            public DateTime Depuis = DateTime.MinValue;
            /// <summary>Tous les PC vus, dans l'ordre. C'est la seule trace qu'un vendeur puisse
            /// lire au renouvellement — voir <see cref="Decide"/>.</summary>
            public System.Collections.Generic.List<string> Machines = new System.Collections.Generic.List<string>();
        }

        /// <summary>PUR : mise en forme. Une ligne par information, le jeton d'abord — le fichier
        /// reste lisible et l'ancien format (jeton seul) en est un cas particulier.</summary>
        public static string Rend(Enregistrement e)
        {
            if (e == null) return "";
            var sb = new StringBuilder();
            sb.Append(e.Token).Append("\r\n");
            if (!string.IsNullOrEmpty(e.Machine)) sb.Append("machine=").Append(e.Machine).Append("\r\n");
            if (e.Depuis != DateTime.MinValue)
                sb.Append("depuis=").Append(e.Depuis.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\r\n");
            if (e.Machines.Count > 0)
                sb.Append("vues=").Append(string.Join(";", e.Machines.ToArray())).Append("\r\n");
            return sb.ToString();
        }

        /// <summary>
        /// PUR : relecture. Tolère l'ANCIEN format — un fichier qui ne contient que le jeton — parce
        /// que c'est ce que contiennent toutes les installations existantes. Rend null si rien
        /// d'exploitable.
        /// </summary>
        public static Enregistrement Lit(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) return null;
            var e = new Enregistrement();
            foreach (string brut in texte.Replace("\r\n", "\n").Split('\n'))
            {
                string l = brut.Trim();
                if (l.Length == 0) continue;
                if (l.StartsWith("machine=", StringComparison.OrdinalIgnoreCase)) e.Machine = l.Substring(8).Trim();
                else if (l.StartsWith("depuis=", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime d;
                    if (DateTime.TryParseExact(l.Substring(7).Trim(), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) e.Depuis = d;
                }
                else if (l.StartsWith("vues=", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string m in l.Substring(5).Split(';'))
                        if (m.Trim().Length > 0 && !e.Machines.Contains(m.Trim())) e.Machines.Add(m.Trim());
                }
                else if (e.Token.Length == 0) e.Token = l;
            }
            return e.Token.Length == 0 ? null : e;
        }

        /// <summary>Ce que la liaison automatique a décidé au dernier chargement.</summary>
        public enum Liaison
        {
            /// <summary>Jamais liée : cette activation-ci fixe le PC.</summary>
            Premiere,
            /// <summary>Toujours le même PC. Rien à faire.</summary>
            Meme,
            /// <summary>Autre PC : la clé se relie ici. Voir <see cref="Decide"/>.</summary>
            Rebind
        }

        /// <summary>
        /// DÉCISION PURE de liaison — et le point le plus important de tout ce fichier.
        ///
        /// HORS LIGNE, ON NE PEUT PAS DISTINGUER « le client a réinstallé Windows » de « le client a
        /// donné sa clé à un ami ». Les deux produisent exactement le même signal : la même clé, un
        /// identifiant de machine différent. L'identifiant vient du MachineGuid, que Windows
        /// régénère à chaque réinstallation — donc le cas légitime EST le cas suspect.
        ///
        /// Il faut choisir laquelle des deux erreurs on accepte de commettre. Refuser, c'est
        /// bloquer un client qui a payé, le jour où il a déjà passé la soirée à réinstaller son PC.
        /// Accepter, c'est laisser passer un partage qu'aucun contrôle hors ligne n'aurait de toute
        /// façon empêché — il suffit de ne jamais lancer l'app sur le premier PC.
        ///
        /// On accepte donc, et on ÉCRIT L'HISTOIRE. Le vendeur qui voit une clé passée sur douze
        /// machines sait quoi faire au renouvellement ; le client qui a réinstallé, lui, ne
        /// s'aperçoit de rien. Prétendre verrouiller sans serveur serait un théâtre — celui-ci a
        /// déjà coûté deux clés à ce produit, verrouillées en dur sur des PC qui changeront.
        /// </summary>
        public static Liaison Decide(Enregistrement e, string machine)
        {
            if (e == null || string.IsNullOrEmpty(machine)) return Liaison.Premiere;
            if (string.IsNullOrEmpty(e.Machine)) return Liaison.Premiere;
            return string.Equals(e.Machine, machine, StringComparison.OrdinalIgnoreCase)
                ? Liaison.Meme : Liaison.Rebind;
        }

        /// <summary>PUR : applique la décision. Rend l'enregistrement à écrire.</summary>
        public static Enregistrement Relie(Enregistrement e, string machine, DateTime maintenant)
        {
            if (e == null) return null;
            if (string.IsNullOrEmpty(machine)) return e;
            if (e.Depuis == DateTime.MinValue) e.Depuis = maintenant.Date;
            e.Machine = machine;
            if (!e.Machines.Contains(machine)) e.Machines.Add(machine);
            return e;
        }
    }
}
