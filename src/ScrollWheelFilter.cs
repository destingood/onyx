using System;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Fait défiler un conteneur scrollable dès que le curseur est au-dessus, même s'il n'a pas le
    /// focus (sous Windows la molette va au contrôle focalisé, pas à celui sous le curseur). À
    /// enregistrer via Application.AddMessageFilter et à retirer au Dispose de la page.
    /// </summary>
    internal sealed class ScrollWheelFilter : IMessageFilter
    {
        private const int WM_MOUSEWHEEL = 0x020A;
        private readonly ScrollableControl _target;
        public ScrollWheelFilter(ScrollableControl target) { _target = target; }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL || _target == null) return false;
            try
            {
                if (!_target.IsHandleCreated || !_target.Visible || !_target.VerticalScroll.Visible) return false;
                Point p = _target.PointToClient(Control.MousePosition);
                if (!_target.ClientRectangle.Contains(p)) return false;
                int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
                var ap = _target.AutoScrollPosition;
                _target.AutoScrollPosition = new Point(-ap.X, -ap.Y - delta);
                return true;   // consommé
            }
            catch { return false; }
        }
    }
}
