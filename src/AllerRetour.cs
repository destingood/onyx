using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Test aller-retour : pour chaque réglage, Applique (capture de l'état d'origine), Retablit,
    /// puis relecture de chaque élément capturé et comparaison à l'origine. Applique les réglages
    /// pour de vrai : à ne lancer que sur une machine virtuelle (voir EtatOrigine.EstMachineDeTest).
    /// Non compilé par le banc (écritures registre, services, tâches).
    /// </summary>
    internal static class AllerRetour
    {
        /// <summary>0 si toutes les lignes sont OK, 6 sinon. Rapport dans aller-retour-AAAAMMJJ-HHmmss.txt.</summary>
        public static int Lance(List<Tweak> selection, Action<string, int> log)
        {
            var rapport = new List<string>();
            int anomalies = 0;
            foreach (Tweak t in selection)
            {
                string ligne;
                try
                {
                    ligne = Teste(t);
                }
                catch (Exception ex)
                {
                    ligne = "ÉCHEC : " + ex.Message;
                }
                if (ligne != "OK") anomalies++;
                rapport.Add(t.Id + " : " + ligne);
                log(t.Id + " : " + ligne, ligne == "OK" ? 1 : 3);
            }
            rapport.Add("");
            rapport.Add(selection.Count + " réglage(s), " + anomalies + " anomalie(s).");
            try
            {
                string chemin = AppPaths.File("aller-retour-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.WriteAllText(chemin, string.Join(Environment.NewLine, rapport.ToArray()) + Environment.NewLine, Encoding.UTF8);
                log("Rapport écrit : " + chemin, 0);
            }
            catch (Exception ex)
            {
                log("Rapport non écrit : " + ex.Message, 2);
            }
            return anomalies == 0 ? 0 : 6;
        }

        private static string Teste(Tweak t)
        {
            EtatOrigine.Entree entree;
            try
            {
                EtatOrigineMachine.Applique(t);
                entree = EtatOrigineMachine.EntreeDe(t.Id);
            }
            catch (Exception ex)
            {
                // Apply a pu écrire une partie avant de lever : on rétablit quand même, sans masquer l'échec.
                string retab;
                try
                {
                    EtatOrigineMachine.Retablit(t);
                    retab = "OK";
                }
                catch (Exception ex2)
                {
                    retab = ex2.Message;
                }
                return "ÉCHEC : " + ex.Message + " ; rétablissement : " + retab;
            }
            // Pas d'entrée (déjà en place, ou déjà appliqué) ou entrée vide (Apply n'a rien écrit).
            if (entree == null || (entree.Items.Count == 0 && entree.Complet))
            {
                EtatOrigineMachine.Retablit(t);
                return "NON CAPTURÉ";
            }
            List<EtatOrigine.Item> origine = entree.Items;
            EtatOrigineMachine.Retablit(t);
            if (!entree.Complet)
                return "NON COUVERT : " + string.Join(", ", entree.NonCouvert);

            var actuel = new List<EtatOrigine.Item>();
            foreach (EtatOrigine.Item i in origine) actuel.Add(EtatOrigineMachine.Lit(i));
            List<string> ecarts = EtatOrigine.Ecarts(origine, actuel);
            return ecarts.Count == 0 ? "OK" : "ÉCART " + ecarts.Count + " : " + string.Join(" ; ", ecarts);
        }
    }
}
