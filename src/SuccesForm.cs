using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Succès Steam d'un jeu, lus dans le cache de Steam sur le PC (aucun réseau) : débloqués
    /// d'abord, du plus récent au plus ancien, puis ceux qui restent. Les succès cachés restent
    /// cachés tant qu'ils ne sont pas débloqués, comme dans Steam.
    /// </summary>
    internal sealed class SuccesForm : Form
    {
        private readonly int _appId;
        private readonly Label _titre, _etat;
        private readonly ComboBox _filtre;
        private readonly ListView _liste;
        private readonly Button _fermer;
        private SuccesSteam.Bilan _bilan;
        private volatile bool _ferme;

        public SuccesForm(int appId, string jeu)
        {
            _appId = appId;
            Text = "ONYX — Succès — " + jeu;
            ClientSize = new Size(860, 600);
            MinimumSize = new Size(640, 420);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = FpsUi.BgMain; ForeColor = FpsUi.Ink; Font = FpsUi.Body;
            try { Icon = Logo.MakeIcon(32, FpsUi.Gold); } catch { }

            _titre = new Label { Text = jeu, ForeColor = FpsUi.Ink, Font = FpsUi.H2, AutoEllipsis = true };
            Controls.Add(_titre);

            _filtre = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, FlatStyle = FlatStyle.Flat };
            _filtre.Items.AddRange(new object[] { "Tous", "Débloqués", "À débloquer" });
            _filtre.SelectedIndex = 0;
            _filtre.AccessibleName = "Filtre des succès";
            _filtre.SelectedIndexChanged += (s, e) => Remplit();
            Controls.Add(_filtre);

            _liste = new ListView
            {
                View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, BorderStyle = BorderStyle.FixedSingle, Font = FpsUi.Small
            };
            _liste.AccessibleName = "Succès";
            _liste.Columns.Add("Succès", 220);
            _liste.Columns.Add("Description", 420);
            _liste.Columns.Add("État", 160);
            Controls.Add(_liste);

            _etat = new Label { ForeColor = FpsUi.Dim, Font = FpsUi.Small, AutoEllipsis = true, Text = "Lecture des succès…" };
            Controls.Add(_etat);

            _fermer = OnyxDesign.Button("Fermer");
            _fermer.Click += (s, e) => Close();
            Controls.Add(_fermer);
            CancelButton = _fermer;

            Resize += (s, e) => Place();
            FormClosed += (s, e) => _ferme = true;
            Shown += (s, e) => { ActiveControl = _liste; Charge(); };
            Place();
        }

        private void Place()
        {
            int W = ClientSize.Width, H = ClientSize.Height, m = 20;
            _titre.SetBounds(m, 16, W - 2 * m - 190, 32);
            _filtre.SetBounds(W - m - 170, 20, 170, 28);
            _liste.SetBounds(m, 60, W - 2 * m, H - 60 - 100);
            int reste = Math.Max(200, _liste.ClientSize.Width - 220 - 160 - SystemInformation.VerticalScrollBarWidth - 4);
            if (_liste.Columns.Count == 3) _liste.Columns[1].Width = reste;
            _etat.SetBounds(m, H - 88, W - 2 * m, 36);
            _fermer.SetBounds(W - m - 120, H - 48, 120, 34);
        }

        private void Charge()
        {
            Task.Run(() =>
            {
                string raison; DateTime? releve;
                SuccesSteam.Bilan b = SuccesSteam.Charge(_appId, out raison, out releve);
                try { if (!_ferme) BeginInvoke((Action)(() => Affiche(b, raison, releve))); } catch { }
            });
        }

        private void Affiche(SuccesSteam.Bilan b, string raison, DateTime? releve)
        {
            _bilan = b;
            if (b == null) { _etat.Text = raison ?? "Aucun succès."; _liste.Items.Clear(); return; }
            _titre.Text = (b.Jeu.Length > 0 ? b.Jeu : _titre.Text) + "  —  " + SuccesSteam.Resume(b);
            _etat.Text = "Lu dans le cache de Steam sur ce PC"
                       + (releve.HasValue ? ", relevé du " + releve.Value.ToString("dd/MM/yyyy HH:mm") : " (aucun succès relevé pour ce compte)")
                       + ". Steam le met à jour quand tu joues ou que tu ouvres la page Succès du jeu.";
            Remplit();
        }

        private void Remplit()
        {
            _liste.BeginUpdate();
            _liste.Items.Clear();
            if (_bilan != null)
                foreach (SuccesSteam.Succes s in _bilan.Liste)
                {
                    if (_filtre.SelectedIndex == 1 && !s.Obtenu) continue;
                    if (_filtre.SelectedIndex == 2 && s.Obtenu) continue;
                    var it = new ListViewItem(SuccesSteam.NomAffiche(s));
                    it.SubItems.Add(SuccesSteam.DescriptionAffichee(s));
                    it.SubItems.Add(SuccesSteam.Etat(s));
                    it.ForeColor = s.Obtenu ? FpsUi.Gold : FpsUi.Dim;
                    _liste.Items.Add(it);
                }
            _liste.EndUpdate();
        }

        /// <summary>Pour le test d'interface : nombre de lignes affichées.</summary>
        internal int Lignes { get { return _liste.Items.Count; } }
        internal bool ChargeFini { get { return _bilan != null || !_etat.Text.StartsWith("Lecture", StringComparison.Ordinal); } }
    }
}
