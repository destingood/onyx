using System;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Effet d'ouverture de fenêtre (fondu d'opacité pur), bâti sur le moteur Anim.
    /// Volontairement minimal et sûr : PAS de déplacement, PAS d'interception de fermeture,
    /// PAS de cross-fade de page (ces effets, trop fragiles sur du WinForms peint à la main —
    /// capture PrintWindow noire, clignotement — ont été retirés). Inerte si Anim.On == false.
    /// </summary>
    internal static class AnimFx
    {
        /// <summary>À appeler AVANT ShowDialog : chrome carbone + apparition en fondu.</summary>
        public static void HookDialog(Form f)
        {
            if (f == null) return;
            Dwm.Darken(f);   // barre de titre carbone sur TOUS les dialogues, pas seulement le shell
            if (Anim.On) { try { f.Opacity = 0.0; } catch { } }   // évite un éclair pleine opacité avant le fondu
            f.Shown += delegate { FadeIn(f, 150); };
        }

        private static void FadeIn(Form f, int ms)
        {
            if (f == null) return;
            if (!Anim.On) { try { f.Opacity = 1.0; } catch { } return; }
            try { f.Opacity = 0.0; } catch { }
            Anim.Tween(ms, Ease.OutCubic,
                delegate (float p) { try { f.Opacity = p; } catch { } },
                delegate { try { f.Opacity = 1.0; } catch { } });
        }
    }
}
