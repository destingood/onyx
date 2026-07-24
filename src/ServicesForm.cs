using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Gestionnaire de services curé : liste de services couramment désactivés, avec description et recommandation.</summary>
    internal class ServicesForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private bool _loading;

        private class SvcDef
        {
            public string Name;         // nom du service
            public string Display;      // libellé lisible
            public string Effect;       // ce que ça change
            public bool RecoDisable;    // recommandé de désactiver
            public string RevertType;   // type de démarrage par défaut
            public SvcDef(string n, string d, string e, bool r, string rev) { Name = n; Display = d; Effect = e; RecoDisable = r; RevertType = rev; }
        }

        private static readonly SvcDef[] Catalog = new[]
        {
            new SvcDef("DiagTrack", "Télémétrie (DiagTrack)", "Collecte et envoi de diagnostics.", true, "delayed-auto"),
            new SvcDef("dmwappushservice", "Télémétrie WAP Push", "Routage de messages de télémétrie.", true, "demand"),
            new SvcDef("SysMain", "SysMain / Superfetch", "Préchargement disque. À garder sur disque mécanique.", false, "auto"),
            new SvcDef("WSearch", "Windows Search", "Indexation. Désactiver ralentit la recherche de fichiers.", false, "delayed-auto"),
            new SvcDef("Spooler", "Spouleur d'impression", "Nécessaire uniquement si tu imprimes.", false, "auto"),
            new SvcDef("Fax", "Fax", "Service de fax.", true, "demand"),
            new SvcDef("RemoteRegistry", "Registre à distance", "Modification du registre par le réseau (surface d'attaque).", true, "demand"),
            new SvcDef("MapsBroker", "Cartes hors ligne", "Téléchargement de cartes en arrière-plan.", true, "delayed-auto"),
            new SvcDef("lfsvc", "Géolocalisation", "Service de localisation.", false, "demand"),
            new SvcDef("WerSvc", "Rapport d'erreurs Windows", "Collecte/envoi après plantage.", true, "demand"),
            new SvcDef("RetailDemo", "Mode démo magasin", "Inutile hors borne de démonstration.", true, "demand"),
            new SvcDef("WMPNetworkSvc", "Partage média (WMP)", "Partage réseau de Windows Media Player.", true, "demand"),
            new SvcDef("XblAuthManager", "Xbox Live (auth)", "À garder si tu utilises le Game Pass / le Store.", false, "demand"),
            new SvcDef("XblGameSave", "Xbox Live (sauvegardes)", "Sauvegardes cloud Xbox.", false, "demand"),
            new SvcDef("XboxNetApiSvc", "Xbox Live (réseau)", "Réseau Xbox Live.", false, "demand"),
            new SvcDef("DPS", "Service de stratégie de diagnostic", "Diagnostics système (à garder en général).", false, "auto"),
        };

        public ServicesForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Reload();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "ONYX — Services Windows";
            ClientSize = new Size(720, 460);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(600, 380);
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Services Windows", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true,
                GridLines = true, HideSelection = false, Font = new Font("Segoe UI", 9f)
            };
            _list.Columns.Add("Service", 200);
            _list.Columns.Add("État", 90);
            _list.Columns.Add("Effet si désactivé", 300);
            _list.Columns.Add("Conseil", 90);
            _list.ItemChecked += OnItemChecked;
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 6) };
            host.Controls.Add(_list);
            Controls.Add(host);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(12, 7, 12, 7) };
            bottom.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(110, 115, 125),
                Text = "Coché = activé. Décoche pour désactiver (réversible : recoche). Vert = recommandé de désactiver."
            });
            var close = new Button { Text = "Fermer", Width = 100, Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, BackColor = Color.White };
            close.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            close.Click += (s, e) => Close();
            bottom.Controls.Add(close);
            Controls.Add(bottom);
            Controls.SetChildIndex(host, 0);
        }

        private void Reload()
        {
            _loading = true;
            _list.Items.Clear();
            foreach (SvcDef d in Catalog)
            {
                int start = Sys.GetServiceStart(d.Name);
                if (start < 0) continue; // service absent sur cette machine
                bool disabled = (start == 4);
                var it = new ListViewItem(d.Display);
                it.SubItems.Add(disabled ? "désactivé" : (start == 2 ? "auto" : "manuel"));
                it.SubItems.Add(d.Effect);
                it.SubItems.Add(d.RecoDisable ? "désactiver" : "au choix");
                it.Checked = !disabled;
                it.Tag = d;
                if (d.RecoDisable) it.SubItems[3].ForeColor = Color.FromArgb(0, 130, 70);
                it.UseItemStyleForSubItems = false;
                _list.Items.Add(it);
            }
            _loading = false;
        }

        private void OnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_loading) return;
            var d = e.Item.Tag as SvcDef;
            if (d == null) return;
            try
            {
                if (e.Item.Checked)
                {
                    Sys.ConfigureService(d.Name, d.RevertType, false, false);
                    e.Item.SubItems[1].Text = "manuel/auto";
                    if (_log != null) _log("Service " + d.Name + " réactivé (" + d.RevertType + ").", 0);
                }
                else
                {
                    Sys.ConfigureService(d.Name, "disabled", true, false);
                    e.Item.SubItems[1].Text = "désactivé";
                    if (_log != null) _log("Service " + d.Name + " désactivé.", 1);
                }
            }
            catch (Exception ex)
            {
                if (_log != null) _log("Service " + d.Name + " : " + ex.Message, 3);
            }
        }
    }
}
