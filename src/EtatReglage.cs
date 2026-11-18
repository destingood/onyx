namespace BTOptimizer
{
    /// <summary>Lecture PURE de l'état de réglages dont la valeur seule ne suffit pas.</summary>
    internal static class EtatReglage
    {
        /// <summary>
        /// Session AutoLogger coupée ? Sans sa clé, Windows ne la crée pas au démarrage : le
        /// réglage est acquis. Sans valeur « Start », Windows la démarre (défaut).
        /// </summary>
        public static bool AutologgerCoupe(bool cleExiste, object start)
        {
            if (!cleExiste) return true;
            try { return start != null && System.Convert.ToInt32(start) == 0; } catch { return false; }
        }
    }
}
