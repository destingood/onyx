using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Page « Actus » : titres des sites d'actualité et des jeux suivis.</summary>
    internal sealed class PageActus : FpsPage
    {
        public PageActus(DashboardForm host) : base(host) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "ACTUS", "Les titres des sites de jeu vidéo");
        }
    }
}
