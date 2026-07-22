using System;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Marshaling UI sûr depuis un thread de fond. Beaucoup de panneaux font
    /// <c>Task.Run(() =&gt; { ...; BeginInvoke(() =&gt; MajUI()); })</c> : si l'utilisateur
    /// FERME la fenêtre pendant que le fond tourne encore, le callback marshalé
    /// s'exécute plus tard sur des contrôles déjà <c>Dispose()</c> → ObjectDisposedException
    /// sur le thread UI. <see cref="Post"/> garde AVANT le post (handle vivant ?) ET
    /// À L'INTÉRIEUR du callback (toujours vivant au moment où il s'exécute ?), puis avale
    /// la course résiduelle si le handle est détruit entre les deux.
    /// </summary>
    internal static class UiSafe
    {
        /// <summary>Exécute <paramref name="action"/> sur le thread UI de <paramref name="c"/>
        /// seulement si le contrôle est encore vivant. Ne lève jamais.</summary>
        public static void Post(Control c, Action action)
        {
            if (c == null || action == null) return;
            try
            {
                if (c.IsDisposed || c.Disposing || !c.IsHandleCreated) return;
                c.BeginInvoke((Action)delegate ()
                {
                    if (c.IsDisposed || c.Disposing) return;   // fermé entre le post et l'exécution
                    action();
                });
            }
            catch { }   // handle détruit pendant le post : la fenêtre part, rien à mettre à jour
        }
    }
}
