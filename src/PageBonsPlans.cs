using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Page « Bons plans » : promos, gratuits, nouveautés, jeux suivis et favoris.</summary>
    internal sealed class PageBonsPlans : FpsPage
    {
        public PageBonsPlans(DashboardForm host) : base(host) { }

        /// <summary>Ouvre l'onglet « Suivis » (appelé quand des alertes n'ont pas encore été vues).</summary>
        public void MontreSuivis() { }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "BONS PLANS", "Promos, jeux gratuits et nouveautés");
        }
    }
}
