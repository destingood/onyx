using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Retirer les applis Windows « bloatware » — liste CURATÉE et sûre, chacune cochable,
    /// rien coché par défaut. Retire l'application préinstallée pour libérer de l'espace et
    /// alléger le menu Démarrer. IRRÉVERSIBLE sans réinstallation manuelle (via le Store) :
    /// un avertissement le rappelle avant toute suppression. Ne touche JAMAIS aux composants
    /// critiques (Store, Calculatrice, Photos, sécurité, Explorateur).
    /// </summary>
    internal class BloatRemoveForm : Form
    {
        // (libellé affiché, motif de nom de paquet Appx, "certains l'utilisent" ?)
        private static readonly Tuple<string, string, bool>[] Catalog =
        {
            Tuple.Create("Xbox (overlay, barre de jeu, appli Xbox)", "Microsoft.Xbox", false),
            Tuple.Create("Xbox Game Bar",                            "Microsoft.XboxGamingOverlay", false),
            Tuple.Create("Solitaire Collection",                     "Microsoft.MicrosoftSolitaireCollection", false),
            Tuple.Create("Cortana",                                  "Microsoft.549981C3F5F10", false),
            Tuple.Create("Microsoft Teams (grand public)",           "MicrosoftTeams", false),
            Tuple.Create("Actualités (Bing News)",                   "Microsoft.BingNews", false),
            Tuple.Create("Météo (Bing Weather)",                     "Microsoft.BingWeather", false),
            Tuple.Create("Clipchamp (montage vidéo)",                "Clipchamp.Clipchamp", false),
            Tuple.Create("Obtenir de l'aide",                        "Microsoft.GetHelp", false),
            Tuple.Create("Hub de commentaires (Feedback)",           "Microsoft.WindowsFeedbackHub", false),
            Tuple.Create("Cartes",                                   "Microsoft.WindowsMaps", false),
            Tuple.Create("Conseils (Tips)",                          "Microsoft.Getstarted", false),
            Tuple.Create("Groove Musique",                           "Microsoft.ZuneMusic", true),
            Tuple.Create("Films et TV",                              "Microsoft.ZuneVideo", true),
            Tuple.Create("Enregistreur vocal",                       "Microsoft.WindowsSoundRecorder", true),
            Tuple.Create("Pense-bêtes (Sticky Notes)",               "Microsoft.MicrosoftStickyNotes", true),
            Tuple.Create("To Do",                                    "Microsoft.Todos", true),
            Tuple.Create("Mobile connecté (Phone Link)",             "Microsoft.YourPhone", true),
            Tuple.Create("Courrier et Calendrier",                   "microsoft.windowscommunicationsapps", true),
            Tuple.Create("Bureautique (Get Office)",                 "Microsoft.MicrosoftOfficeHub", false),
        };

        private readonly Action<string, int> _log;
        private CheckedListBox _list;
        private Label _status;
        private Button _btnRemove, _btnRestore, _btnClose;
        private bool _busy;

        public BloatRemoveForm(Action<string, int> log)
        {
            _log = log;
            BuildUi();
            Theme.Apply(this);
        }

        private void BuildUi()
        {
            Text = "Fluide — Retirer les applis Windows";
            ClientSize = new Size(560, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            var intro = new Label();
            intro.SetBounds(16, 12, 528, 56);
            intro.Text = "Coche les applications préinstallées que tu veux RETIRER (rien n'est coché par défaut). "
                       + "⚠️ Irréversible : la réinstallation ne se fait qu'à la main via le Microsoft Store. "
                       + "Les composants critiques (Store, Calculatrice, Photos, sécurité) ne sont jamais proposés. "
                       + "« (parfois utile) » = à réfléchir avant de retirer.";
            intro.ForeColor = Theme.InkDimColor;

            _list = new CheckedListBox();
            _list.SetBounds(16, 74, 528, 380);
            _list.CheckOnClick = true;
            _list.IntegralHeight = false;
            foreach (var a in Catalog)
                _list.Items.Add(a.Item1 + (a.Item3 ? "   (parfois utile)" : ""), false);

            _status = new Label();
            _status.SetBounds(16, 462, 528, 40);
            _status.ForeColor = Theme.InkDimColor;
            _status.Text = "Astuce : garde Photos, Calculatrice, Store — ils ne sont pas dans la liste exprès.";

            _btnRemove = MakeBtn("🗑 Retirer la sélection", 16, 510, 220, 38, true);
            _btnRemove.Click += OnRemove;
            _btnRestore = MakeBtn("↺ Réinstaller les applis par défaut", 246, 510, 210, 38, false);
            _btnRestore.Click += OnRestore;
            _btnClose = MakeBtn("Fermer", 466, 510, 78, 38, false);
            _btnClose.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { intro, _list, _status, _btnRemove, _btnRestore, _btnClose });
        }

        private void OnRemove(object sender, EventArgs e)
        {
            if (_busy) return;
            var sel = new List<Tuple<string, string, bool>>();
            for (int i = 0; i < Catalog.Length; i++)
                if (_list.GetItemChecked(i)) sel.Add(Catalog[i]);
            if (sel.Count == 0) { SetStatus("Rien de coché.", 0); return; }

            if (MessageBox.Show(this,
                    "Retirer " + sel.Count + " application(s) de Windows ?\r\n\r\n"
                    + "⚠️ IRRÉVERSIBLE : pour les récupérer, il faudra les réinstaller une par une "
                    + "depuis le Microsoft Store. Continuer ?",
                    "Retirer des applis Windows", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            _busy = true; SetBusy(true);
            SetStatus("Suppression de " + sel.Count + " appli(s)…", 0);
            Task.Run(() =>
            {
                int ok = 0;
                foreach (var a in sel)
                {
                    try { if (Sys.RemoveAppxByName(a.Item2, RelayLog)) ok++; }
                    catch { }
                }
                int done = ok, total = sel.Count;
                try { BeginInvoke((Action)(() =>
                {
                    _busy = false; SetBusy(false);
                    for (int i = Catalog.Length - 1; i >= 0; i--) _list.SetItemChecked(i, false);
                    SetStatus("Terminé : " + done + "/" + total + " appli(s) retirée(s). Certaines exigent une déconnexion pour disparaître partout.", done > 0 ? 1 : 2);
                })); }
                catch { _busy = false; }
            });
        }

        private void OnRestore(object sender, EventArgs e)
        {
            if (_busy) return;
            if (MessageBox.Show(this,
                    "Tenter de réinstaller les applis Windows par défaut ?\r\n\r\n"
                    + "Best-effort : réenregistre les paquets encore présents et ouvre le Microsoft Store "
                    + "pour récupérer le reste. Certaines applis retirées devront être réinstallées à la main.",
                    "Réinstaller les applis", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            _busy = true; SetBusy(true);
            SetStatus("Réenregistrement des applis Windows…", 0);
            Task.Run(() =>
            {
                try { Sys.ReprovisionDefaultApps(RelayLog); } catch { }
                try { BeginInvoke((Action)(() =>
                {
                    _busy = false; SetBusy(false);
                    SetStatus("Réenregistrement lancé. Ouvre le Microsoft Store → Bibliothèque pour le reste.", 1);
                })); }
                catch { _busy = false; }
            });
        }

        private void SetBusy(bool b)
        {
            Cursor = b ? Cursors.WaitCursor : Cursors.Default;
            _list.Enabled = !b; _btnRemove.Enabled = !b; _btnRestore.Enabled = !b;
        }

        private void SetStatus(string t, int level)
        {
            _status.Text = t;
            _status.ForeColor = level == 1 ? Theme.OkColor : level >= 2 ? Color.FromArgb(200, 120, 0) : Theme.InkDimColor;
        }

        private void RelayLog(string m, int l) { if (_log != null) try { _log(m, l); } catch { } }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy) { e.Cancel = true; SetStatus("Patiente : opération en cours…", 2); return; }
            base.OnFormClosing(e);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button { Text = text };
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            b.BackColor = primary ? Color.FromArgb(0, 150, 90) : Color.White;
            b.ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52);
            b.UseVisualStyleBackColor = false;
            return b;
        }
    }
}
