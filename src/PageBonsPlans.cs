using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Page « Bons plans » : promos et nouveautés Steam, jeux gratuits (Epic, GamerPower), jeux
    /// suivis et favoris. Rien n'est chargé avant la première ouverture ; chaque chargement porte
    /// un numéro de génération et une réponse arrivée après un changement de filtre est ignorée.
    /// </summary>
    internal sealed class PageBonsPlans : FpsPage
    {
        private const int OngletPromos = 0, OngletGratuits = 1, OngletNouveautes = 2, OngletSuivis = 3, OngletFavoris = 4;
        private const int ParPage = 24, LotsMax = 3, PasSteam = 50;
        private static readonly string[] NomsOnglets = { "Promos", "Gratuits", "Nouveautés", "Suivis", "Favoris" };
        private static readonly int[] Plafonds = { -1, 0, 100, 500, 1000, 2000 };
        private static readonly KeyValuePair<string, string>[] Tris =
        {
            new KeyValuePair<string, string>("Remise", "remise"),
            new KeyValuePair<string, string>("Prix", "prix"),
            new KeyValuePair<string, string>("Récents", "recent"),
            new KeyValuePair<string, string>("Nom", "nom"),
        };
        private const string TexteMax = "50 jeux suivis au maximum : retire-en un d'abord.";
        private const string TexteErreur = "Source indisponible pour l'instant.";

        private readonly TextBox _search;
        private readonly Button[] _onglets = new Button[NomsOnglets.Length];
        private readonly Button[] _pilules = new Button[Plafonds.Length];
        private readonly Label _lPrix, _lCat, _lPlat, _lTri, _etat, _numPage, _info;
        private readonly ComboBox _cat, _plat, _tri;
        private readonly Button _verifier, _installes, _reessayer, _prec, _suiv;
        private readonly BufferedFlow _flow;
        private readonly LinkLabel _pied;
        private readonly Timer _attente;
        private ScrollWheelFilter _wheel;

        private int _onglet, _plafond = -1, _generation, _pageIdx, _debutSteam;
        private bool _charge, _occupe, _steamFini, _majCombos;
        private List<Offre> _resultats = new List<Offre>();
        private List<Offre> _gratuitsBruts;
        private DateTime _gratuitsLe;   // UTC ; au-delà d'une heure, la liste est rechargée

        public PageBonsPlans(DashboardForm host) : base(host)
        {
            _search = new TextBox();
            try { _search.PlaceholderText = "Rechercher un jeu…"; } catch { }
            _search.BackColor = FpsUi.Card; _search.ForeColor = FpsUi.Ink;
            _search.BorderStyle = BorderStyle.FixedSingle; _search.Font = FpsUi.Small;
            _search.TextChanged += (s, e) => { _attente.Stop(); _attente.Start(); };
            Controls.Add(_search);

            _attente = new Timer { Interval = 400 };   // 400 ms sans frappe avant l'appel
            _attente.Tick += (s, e) => { _attente.Stop(); Recharge(); };

            for (int i = 0; i < _onglets.Length; i++)
            {
                int k = i;
                _onglets[i] = FpsUi.GhostButton(NomsOnglets[i]);
                _onglets[i].Height = 30;
                _onglets[i].Click += (s, e) => ChoisitOnglet(k);
                Controls.Add(_onglets[i]);
            }

            _lPrix = Etiquette("Prix :");
            for (int i = 0; i < _pilules.Length; i++)
            {
                int k = i;
                _pilules[i] = FpsUi.GhostButton(Offres.LibellePlafond(Plafonds[i]));
                _pilules[i].Height = 28;
                _pilules[i].Width = TextRenderer.MeasureText(_pilules[i].Text, FpsUi.Small).Width + 20;
                _pilules[i].Click += (s, e) => ChoisitPlafond(Plafonds[k]);
                Controls.Add(_pilules[i]);
            }

            _lCat = Etiquette("Catégorie");
            _cat = Liste(150, new[] { "Toutes" }.Concat(Offres.Categories.Select(c => c.Key)));
            _lPlat = Etiquette("Plateforme");
            _plat = Liste(130, new[] { "Toutes" }.Concat(Offres.Plateformes));
            _lTri = Etiquette("Tri");
            _tri = Liste(110, Tris.Select(t => t.Key));

            _verifier = FpsUi.GhostButton("Vérifier maintenant");
            _verifier.Height = 28; _verifier.Width = 150;
            _verifier.Click += (s, e) => Verifie();
            Controls.Add(_verifier);
            _installes = FpsUi.GhostButton("Suivre mes jeux installés");
            _installes.Height = 28; _installes.Width = 190;
            _installes.Click += (s, e) => SuitInstalles();
            Controls.Add(_installes);

            _flow = new BufferedFlow();
            _flow.AutoScroll = true;
            _flow.BackColor = Color.Transparent;
            _flow.Padding = new Padding(0, 4, 0, 12);
            Controls.Add(_flow);
            _wheel = new ScrollWheelFilter(_flow);
            try { Application.AddMessageFilter(_wheel); } catch { }

            _etat = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = FpsUi.Body, ForeColor = FpsUi.Dim, BackColor = FpsUi.BgMain, Visible = false };
            Controls.Add(_etat);
            _reessayer = FpsUi.GoldButton("Réessayer");
            _reessayer.Size = new Size(130, 34); _reessayer.Visible = false;
            _reessayer.Click += (s, e) => { _gratuitsBruts = null; Recharge(); };
            Controls.Add(_reessayer);
            _etat.BringToFront(); _reessayer.BringToFront();

            _prec = FpsUi.GhostButton("‹ Précédent");
            _prec.Size = new Size(110, 28);
            _prec.Click += (s, e) => { if (_pageIdx > 0) { _pageIdx--; Affiche(false); } };
            Controls.Add(_prec);
            _numPage = Etiquette("Page 1");
            _numPage.AutoSize = false; _numPage.TextAlign = ContentAlignment.MiddleCenter; _numPage.Size = new Size(110, 28);
            _suiv = FpsUi.GhostButton("Suivant ›");
            _suiv.Size = new Size(110, 28);
            _suiv.Click += (s, e) => Suivant();
            Controls.Add(_suiv);
            _info = Etiquette("");
            _info.AutoSize = false; _info.AutoEllipsis = true; _info.TextAlign = ContentAlignment.MiddleLeft;

            _pied = new LinkLabel
            {
                Text = "Jeux gratuits : GamerPower.com · Prix : Steam, Epic Games Store",
                Font = FpsUi.Tiny, ForeColor = FpsUi.Dim2, BackColor = FpsUi.BgMain,
                LinkColor = FpsUi.GoldDim, ActiveLinkColor = FpsUi.Gold, VisitedLinkColor = FpsUi.GoldDim,
                LinkBehavior = LinkBehavior.HoverUnderline, AutoSize = false,
            };
            Lien(_pied, "GamerPower.com", "https://www.gamerpower.com");
            Lien(_pied, "Steam", "https://store.steampowered.com");
            Lien(_pied, "Epic Games Store", "https://store.epicgames.com/fr/");
            _pied.LinkClicked += (s, e) => Ouvre(e.Link.LinkData as string);
            Controls.Add(_pied);

            _majCombos = true;
            _cat.SelectedIndex = 0; _plat.SelectedIndex = 0; _tri.SelectedIndex = 0;
            _majCombos = false;
            _cat.SelectedIndexChanged += (s, e) => { if (!_majCombos) Recharge(); };
            _plat.SelectedIndexChanged += (s, e) => { if (!_majCombos) Recharge(); };
            _tri.SelectedIndexChanged += (s, e) => { if (!_majCombos) Recharge(); };

            MajOnglets();
            Resize += (s, e) => DoLayout();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_wheel != null) { try { Application.RemoveMessageFilter(_wheel); } catch { } _wheel = null; }
                try { _attente.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        public override void OnShown()
        {
            DoLayout();
            if (SuiviVeille.AlertesNonVues > 0) { _charge = true; MontreSuivis(); return; }
            MajOnglets();
            if (_charge) return;
            _charge = true;
            Recharge();
        }

        /// <summary>Affiche le sous-onglet « Suivis » (alertes pas encore vues).</summary>
        public void MontreSuivis()
        {
            _charge = true;
            ChoisitOnglet(OngletSuivis);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "BONS PLANS", "Promos, jeux gratuits et nouveautés — tu peux suivre un jeu pour être prévenu de sa promo");
        }

        // ------------------------------------------------------------------ disposition

        private Label Etiquette(string t)
        {
            var l = new Label { Text = t, AutoSize = true, Font = FpsUi.Small, ForeColor = FpsUi.Dim, BackColor = Color.Transparent };
            Controls.Add(l);
            return l;
        }

        private ComboBox Liste(int largeur, IEnumerable<string> elements)
        {
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, Font = FpsUi.Small, Width = largeur,
            };
            foreach (string e in elements) c.Items.Add(e);
            Controls.Add(c);
            return c;
        }

        private static void Lien(LinkLabel l, string mot, string url)
        {
            int i = l.Text.IndexOf(mot, StringComparison.Ordinal);
            if (i >= 0) l.Links.Add(i, mot.Length, url);
        }

        private void DoLayout()
        {
            int w = ClientSize.Width, h = ClientSize.Height, x0 = 34, droite = Math.Max(x0 + 300, w - 34);
            _search.SetBounds(Math.Max(x0 + 260, droite - 280), 24, Math.Min(280, droite - x0 - 260), 26);

            int x = x0;
            foreach (Button b in _onglets)
            {
                b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + 28;
                b.Location = new Point(x, 90);
                x += b.Width + 6;
            }

            // Rangée des filtres : passe à la ligne quand la fenêtre est étroite.
            var rangee = new List<Control> { _lPrix };
            rangee.AddRange(_pilules);
            rangee.AddRange(new Control[] { _lCat, _cat, _lPlat, _plat, _lTri, _tri, _verifier, _installes });
            int y = 130;
            x = x0;
            foreach (Control c in rangee)
            {
                if (!c.Visible) continue;
                if (c is Label && c.Width == 0) continue;
                bool etiquette = c is Label;
                if (x + c.Width > droite && x > x0) { x = x0; y += 34; }
                c.Location = new Point(x + (etiquette && x > x0 ? 8 : 0), y + (28 - c.Height) / 2);
                x = c.Right + (etiquette ? 6 : 6);
            }
            int haut = y + 40;

            _pied.SetBounds(x0, h - 26, droite - x0, 18);
            int py = h - 26 - 36;
            int cx = x0 + (droite - x0) / 2;
            _numPage.Location = new Point(cx - _numPage.Width / 2, py);
            _prec.Location = new Point(_numPage.Left - _prec.Width - 8, py);
            _suiv.Location = new Point(_numPage.Right + 8, py);
            _info.SetBounds(x0, py, Math.Max(60, _prec.Left - x0 - 12), 28);

            _flow.SetBounds(x0, haut, Math.Max(200, w - x0 - 16), Math.Max(80, py - 8 - haut));
            _etat.SetBounds(_flow.Left, _flow.Top + 30, Math.Max(200, droite - x0), 48);
            _reessayer.Location = new Point(_flow.Left + (_etat.Width - _reessayer.Width) / 2, _etat.Bottom + 6);
            Invalidate();
        }

        private static void Style(Button b, bool actif)
        {
            b.ForeColor = actif ? FpsUi.Gold : FpsUi.Ink;
            b.BackColor = actif ? FpsUi.BlendGold(FpsUi.Card, 0.16f) : Color.FromArgb(31, 27, 22);
            b.FlatAppearance.BorderColor = FpsUi.Gold;
            b.FlatAppearance.BorderSize = actif ? 1 : 0;
        }

        private void MajOnglets()
        {
            int nv = SuiviVeille.AlertesNonVues;
            for (int i = 0; i < _onglets.Length; i++)
            {
                _onglets[i].Text = i == OngletSuivis && nv > 0 ? "Suivis •" + nv.ToString(CultureInfo.InvariantCulture) : NomsOnglets[i];
                Style(_onglets[i], i == _onglet);
            }
            bool prixActif = _onglet != OngletGratuits && _onglet != OngletFavoris;
            for (int i = 0; i < _pilules.Length; i++)
            {
                _pilules[i].Enabled = prixActif;
                Style(_pilules[i], prixActif && Plafonds[i] == _plafond);
            }
            _cat.Enabled = prixActif;
            bool plateforme = _onglet == OngletGratuits;   // une seule plateforme ailleurs (Steam) ; Favoris : texte seul
            _lPlat.Visible = _plat.Visible = plateforme;
            bool tri = _onglet == OngletPromos || _onglet == OngletNouveautes;
            _lTri.Visible = _tri.Visible = tri;
            _verifier.Visible = _installes.Visible = _onglet == OngletSuivis;
            DoLayout();
        }

        // ------------------------------------------------------------------ actions

        private void ChoisitOnglet(int i)
        {
            _onglet = i;
            if (i == OngletSuivis) SuiviVeille.MarqueVues();
            _majCombos = true;
            _tri.SelectedIndex = i == OngletNouveautes ? 2 : 0;   // « recent » pour les nouveautés, « remise » sinon
            _majCombos = false;
            MajOnglets();
            Recharge();
        }

        private void ChoisitPlafond(int cts)
        {
            // Steam n'a pas de filtre « gratuit en promo » : « Gratuit » mène aux jeux gratuits.
            if (cts == 0 && _onglet == OngletPromos) { ChoisitOnglet(OngletGratuits); return; }
            _plafond = cts;
            MajOnglets();
            Recharge();
        }

        private string Texte() { return (_search.Text ?? "").Trim(); }

        private Offres.Filtre FiltreCourant()
        {
            int c = _cat.SelectedIndex, p = _plat.SelectedIndex;
            return new Offres.Filtre
            {
                Texte = Texte(),
                PlafondCts = _plafond,
                Tag = c > 0 && c <= Offres.Categories.Length ? Offres.Categories[c - 1].Value : 0,
                Plateforme = p > 0 && p <= Offres.Plateformes.Length ? Offres.Plateformes[p - 1] : null,
            };
        }

        private string TriCourant()
        {
            int i = _tri.SelectedIndex;
            return i >= 0 && i < Tris.Length ? Tris[i].Value : "remise";
        }

        private bool OngletSteam()
        {
            return _onglet == OngletPromos || _onglet == OngletNouveautes || (_onglet == OngletSuivis && Texte().Length > 0);
        }

        private void Recharge()
        {
            _attente.Stop();
            _generation++;
            _occupe = false;
            _pageIdx = 0; _debutSteam = 0; _steamFini = false;
            _resultats = new List<Offre>();
            _info.Text = "";
            switch (_onglet)
            {
                case OngletGratuits: ChargeGratuits(_generation); break;
                case OngletFavoris: _resultats = ListeFavoris(); Affiche(false); break;
                case OngletSuivis:
                    if (Texte().Length > 0) ChargeSteam(_generation, false);
                    else ChargeSuivis(_generation);
                    break;
                default: ChargeSteam(_generation, false); break;
            }
        }

        // Exécute sur le thread d'interface, sans effet si la page est détruite ou si un autre
        // chargement a été lancé entre-temps (gen < 0 : toujours appliqué).
        private void Retour(int gen, Action a)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)(() =>
                {
                    if (IsDisposed || (gen >= 0 && gen != _generation)) return;
                    try { a(); } catch { }
                }));
            }
            catch { }
        }

        private void EtatChargement()
        {
            VideCartes();
            _etat.Text = "Chargement…"; _etat.Visible = true; _reessayer.Visible = false;
            _occupe = true;
            MajPagination();
        }

        private void ChargeSteam(int gen, bool suite)
        {
            Offres.Filtre f = FiltreCourant();
            bool promos = _onglet == OngletPromos, nouveautes = _onglet == OngletNouveautes;
            string tri = promos || nouveautes ? TriCourant() : null;   // Suivis : ordre de pertinence de Steam
            int debut = _debutSteam, deja = _resultats.Count;
            int besoin = (suite ? _pageIdx + 2 : 1) * ParPage;
            if (suite) { _occupe = true; _numPage.Text = "Chargement…"; MajPagination(); }
            else EtatChargement();

            Task.Run(async () =>
            {
                var nouveaux = new List<Offre>();
                bool fini = false;
                int d = debut;
                try
                {
                    for (int lot = 0; lot < LotsMax; lot++)
                    {
                        List<Offre> brut = await OffresReseau.RechercheSteam(f, promos, nouveautes, d).ConfigureAwait(false);
                        d += PasSteam;
                        if (brut.Count == 0) { fini = true; break; }
                        // Filtre local : le plafond seulement (texte et catégorie sont appliqués par Steam).
                        foreach (Offre o in brut) if (Offres.SousPlafond(o, f.PlafondCts)) nouveaux.Add(o);
                        if (deja + nouveaux.Count >= besoin) break;
                    }
                }
                catch { fini = true; }
                bool erreur = OffresReseau.DerniereErreur("steam");
                Retour(gen, () =>
                {
                    _occupe = false;
                    _debutSteam = d; _steamFini = fini;
                    var vus = new HashSet<string>(_resultats.Select(o => o.Cle));
                    List<Offre> ajout = nouveaux.Where(o => vus.Add(o.Cle)).ToList();
                    // Chaque lot est trié à part et ajouté après les pages déjà vues : elles ne bougent pas.
                    if (tri != null) ajout = Offres.Trier(ajout, tri);
                    _resultats.AddRange(ajout);
                    if (suite && _resultats.Count > (_pageIdx + 1) * ParPage) _pageIdx++;
                    Affiche(erreur && _resultats.Count == 0);
                });
            });
        }

        private void ChargeGratuits(int gen)
        {
            if (_gratuitsBruts != null && DateTime.UtcNow - _gratuitsLe < TimeSpan.FromMinutes(60)) { AfficheGratuits(false); return; }
            EtatChargement();
            Task.Run(async () =>
            {
                List<Offre> l;
                try { l = await OffresReseau.Gratuits().ConfigureAwait(false); } catch { l = new List<Offre>(); }
                bool erreur = OffresReseau.DerniereErreur("epic") && OffresReseau.DerniereErreur("gamerpower");
                Retour(gen, () =>
                {
                    _occupe = false;
                    _gratuitsBruts = erreur && l.Count == 0 ? null : l;
                    _gratuitsLe = DateTime.UtcNow;
                    AfficheGratuits(erreur);
                });
            });
        }

        private void AfficheGratuits(bool erreur)
        {
            Offres.Filtre f = FiltreCourant();
            f.PlafondCts = -1; f.Tag = 0;   // plafonds et catégorie désactivés ici
            _resultats = Offres.Filtrer(_gratuitsBruts ?? new List<Offre>(), f)
                .OrderBy(o => o.Fin.HasValue ? 0 : 1).ThenBy(o => o.Fin ?? DateTime.MaxValue).ToList();
            Affiche(erreur && _resultats.Count == 0);
        }

        private void ChargeSuivis(int gen)
        {
            var suivis = Magasin.Suivis.Where(j => j != null && j.AppId > 0)
                .Select(j => new JeuSuivi { AppId = j.AppId, Nom = j.Nom }).ToList();
            if (suivis.Count == 0) { Affiche(false); return; }
            List<Offre> fiches = SuiviVeille.DernieresFiches;
            if (suivis.All(j => fiches.Exists(o => o.SteamAppId == j.AppId))) { ConstruitSuivis(suivis, fiches, false); return; }
            EtatChargement();
            Task.Run(async () =>
            {
                List<Offre> l;
                try { l = await OffresReseau.FichesSteam(suivis.Select(j => j.AppId), 30).ConfigureAwait(false); } catch { l = new List<Offre>(); }
                bool erreur = OffresReseau.DerniereErreur("steam");
                Retour(gen, () => { _occupe = false; ConstruitSuivis(suivis, l, erreur); });
            });
        }

        private void ConstruitSuivis(List<JeuSuivi> suivis, List<Offre> fiches, bool erreur)
        {
            var liste = new List<Offre>();
            foreach (JeuSuivi j in suivis)
            {
                Offre f = fiches.Find(o => o != null && o.SteamAppId == j.AppId);
                liste.Add(f ?? new Offre
                {
                    Source = "steam", Id = j.AppId.ToString(CultureInfo.InvariantCulture), Nom = j.Nom,
                    SteamAppId = j.AppId, Plateformes = new[] { "Steam" },
                    Url = "https://store.steampowered.com/app/" + j.AppId.ToString(CultureInfo.InvariantCulture) + "/",
                });
            }
            _resultats = liste.OrderBy(o => o.Remise > 0 ? 0 : 1).ToList();   // promos en tête, ordre du suivi sinon
            if (erreur) _info.Text = "Prix : " + TexteErreur;
            Affiche(false);
        }

        private List<Offre> ListeFavoris()
        {
            var liste = new List<Offre>();
            foreach (Favori f in Magasin.FavorisJeux)
            {
                if (f == null || string.IsNullOrEmpty(f.Cle)) continue;
                int i = f.Cle.IndexOf(':');
                string src = i > 0 ? f.Cle.Substring(0, i) : (f.Source ?? "");
                string id = i > 0 ? f.Cle.Substring(i + 1) : f.Cle;
                int appId = 0;
                if (src != "steam" || !int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out appId)) appId = 0;
                liste.Add(new Offre
                {
                    Source = src, Id = id, Nom = f.Titre, Url = f.Url, SteamAppId = appId,
                    Plateformes = src == "steam" ? new[] { "Steam" } : src == "epic" ? new[] { "Epic Games" } : new string[0],
                });
            }
            liste.Reverse();   // le plus récent en tête
            return Offres.Filtrer(liste, new Offres.Filtre { Texte = Texte() });
        }

        private void Suivant()
        {
            if ((_pageIdx + 1) * ParPage < _resultats.Count) { _pageIdx++; Affiche(false); return; }
            if (OngletSteam() && !_steamFini && !_occupe) ChargeSteam(_generation, true);
        }

        private async void Verifie()
        {
            _verifier.Enabled = false;
            _info.Text = "Vérification…";
            try { await SuiviVeille.Controle(); } catch { }
            if (IsDisposed) return;
            _verifier.Enabled = true;
            _info.Text = OffresReseau.DerniereErreur("steam") ? TexteErreur : "Vérifié.";
            MajOnglets();
            if (_onglet == OngletSuivis && Texte().Length == 0) { string t = _info.Text; Recharge(); _info.Text = t; }
        }

        private void SuitInstalles()
        {
            _installes.Enabled = false;
            _info.Text = "Recherche des jeux installés…";
            Task.Run(() =>
            {
                List<GameLibrary.InstalledGame> jeux;
                try { jeux = GameLibrary.ScanAll() ?? new List<GameLibrary.InstalledGame>(); } catch { jeux = new List<GameLibrary.InstalledGame>(); }
                Retour(-1, () =>
                {
                    int n = 0; bool plein = false;
                    foreach (GameLibrary.InstalledGame g in jeux)
                    {
                        if (g == null || g.SteamAppId <= 0 || string.IsNullOrWhiteSpace(g.Name)) continue;
                        if (Magasin.Suivis.Exists(j => j.AppId == g.SteamAppId)) continue;
                        if (Magasin.Suivis.Count >= Suivi.Max) { plein = true; break; }
                        var o = new Offre { Source = "steam", Id = g.SteamAppId.ToString(CultureInfo.InvariantCulture), Nom = g.Name, SteamAppId = g.SteamAppId };
                        if (Suivi.Ajoute(Magasin.Suivis, o)) n++;
                    }
                    if (n > 0) Magasin.SauveSuivis();
                    _installes.Enabled = true;
                    string t = plein ? TexteMax
                        : n == 0 ? "Aucun nouveau jeu Steam installé à suivre."
                        : n.ToString(CultureInfo.InvariantCulture) + (n > 1 ? " jeux ajoutés aux suivis." : " jeu ajouté aux suivis.");
                    if (_onglet == OngletSuivis && Texte().Length == 0) Recharge();
                    _info.Text = t;
                });
            });
        }

        internal bool EstFavori(Offre o) { return o != null && Favoris.Contient(Magasin.FavorisJeux, o.Cle); }
        internal bool EstSuivi(Offre o) { return o != null && o.SteamAppId > 0 && Magasin.Suivis.Exists(j => j.AppId == o.SteamAppId); }

        internal void BasculeFavori(Offre o)
        {
            if (o == null) return;
            Favoris.Bascule(Magasin.FavorisJeux, Favoris.DepuisOffre(o, DateTime.Now));
            Magasin.SauveFavorisJeux();
            if (_onglet == OngletFavoris) Recharge();
        }

        internal void BasculeSuivi(Offre o)
        {
            if (o == null || o.SteamAppId <= 0) return;
            if (EstSuivi(o)) Suivi.Retire(Magasin.Suivis, o.SteamAppId);
            else if (!Suivi.Ajoute(Magasin.Suivis, o))
            {
                if (Magasin.Suivis.Count >= Suivi.Max) _info.Text = TexteMax;
                return;
            }
            Magasin.SauveSuivis();
            if (_onglet == OngletSuivis && Texte().Length == 0) Recharge();
        }

        /// <summary>Ouvre un lien dans le navigateur, seulement s'il est absolu en http ou https.</summary>
        internal static void Ouvre(string url)
        {
            if (!Offres.LienSur(url)) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.Trim()) { UseShellExecute = true }); } catch { }
        }

        // ------------------------------------------------------------------ rendu

        private void VideCartes()
        {
            _flow.SuspendLayout();
            foreach (Control c in _flow.Controls.Cast<Control>().ToList()) c.Dispose();
            _flow.Controls.Clear();
            _flow.ResumeLayout();
        }

        private void Affiche(bool erreur)
        {
            _flow.SuspendLayout();
            foreach (Control c in _flow.Controls.Cast<Control>().ToList()) c.Dispose();
            _flow.Controls.Clear();
            List<Offre> page = _resultats.Skip(_pageIdx * ParPage).Take(ParPage).ToList();
            if (page.Count == 0)
            {
                bool aide = _onglet == OngletSuivis && Texte().Length == 0 && !erreur;
                _etat.Text = erreur ? TexteErreur
                    : aide ? "Cherche un jeu puis clique sur la cloche : tu seras prévenu dès qu'il passe en promo."
                    : "Aucun jeu ne correspond à ces filtres.";
                _etat.Visible = true;
                _reessayer.Visible = erreur;
            }
            else
            {
                _etat.Visible = false; _reessayer.Visible = false;
                foreach (Offre o in page) _flow.Controls.Add(new Carte(this, o));   // vignettes : cette page seulement
            }
            _flow.ResumeLayout();
            try { _flow.AutoScrollPosition = Point.Empty; } catch { }
            MajPagination();
        }

        private void MajPagination()
        {
            bool plusLocal = (_pageIdx + 1) * ParPage < _resultats.Count;
            _prec.Enabled = !_occupe && _pageIdx > 0;
            _suiv.Enabled = !_occupe && (plusLocal || (OngletSteam() && !_steamFini && _resultats.Count > 0));
            if (!_occupe) _numPage.Text = "Page " + (_pageIdx + 1).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Une carte de jeu : jaquette, nom, prix, plateforme et fin d'offre, ☆ et 🔔.</summary>
        private sealed class Carte : Control
        {
            private const int L = 300, H = 128;
            private static readonly Font Barre = new Font(FpsUi.Small, FontStyle.Strikeout);
            private static readonly Font Emoji = new Font("Segoe UI Emoji", 11f);
            private readonly PageBonsPlans _page;
            private readonly Offre _o;
            private bool _survol;

            private static readonly Rectangle REtoile = new Rectangle(L - 62, H - 34, 26, 26);
            private static readonly Rectangle RCloche = new Rectangle(L - 34, H - 34, 26, 26);

            public Carte(PageBonsPlans page, Offre o)
            {
                _page = page; _o = o;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Size = new Size(L, H);
                Margin = new Padding(0, 0, 12, 12);
                BackColor = FpsUi.BgMain;
                Cursor = Cursors.Hand;
                MouseEnter += (s, e) => { _survol = true; Invalidate(); };
                MouseLeave += (s, e) => { _survol = false; Invalidate(); };
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                if (e.Button != MouseButtons.Left) return;
                if (REtoile.Contains(e.Location)) { _page.BasculeFavori(_o); if (!IsDisposed) Invalidate(); return; }
                if (_o.SteamAppId > 0 && RCloche.Contains(e.Location)) { _page.BasculeSuivi(_o); if (!IsDisposed) Invalidate(); return; }
                Ouvre(_o.Url);
            }

            private void Pret()
            {
                try { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(() => { if (!IsDisposed) Invalidate(); })); } catch { }
            }

            private string Plateforme()
            {
                if (_o.Plateformes != null && _o.Plateformes.Length > 0) return string.Join(", ", _o.Plateformes.Take(2));
                if (_o.Source == "steam") return "Steam";
                if (_o.Source == "epic") return "Epic Games";
                return "GamerPower";
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                FpsUi.PaintCard(g, new Rectangle(0, 0, L - 1, H - 1), _survol ? FpsUi.CardHi : FpsUi.Card, FpsUi.Border, 10f);

                // Jaquette Steam (portrait) ; hors Steam, un fond avec le nom de la plateforme.
                var ri = new Rectangle(8, 8, 76, H - 16);
                Image img = null;
                if (_o.SteamAppId > 0) { try { img = GameArt.Get(_o.SteamAppId, Pret); } catch { } }
                if (img != null) { try { g.DrawImage(img, ri); } catch { } }
                else
                {
                    using (var p = FpsUi.Round(ri, 6f))
                    using (var br = new SolidBrush(FpsUi.Accent(24))) g.FillPath(br, p);
                    TextRenderer.DrawText(g, Plateforme(), FpsUi.Tiny, ri, FpsUi.Dim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                }

                int tx = 96, tw = L - tx - 10;
                TextRenderer.DrawText(g, _o.Nom ?? "", FpsUi.H3, new Rectangle(tx, 8, tw, 38), FpsUi.Ink,
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                int x = tx;
                if (_o.Remise > 0)
                {
                    string r = "−" + _o.Remise.ToString(CultureInfo.InvariantCulture) + " %";
                    int rw = TextRenderer.MeasureText(r, FpsUi.Small).Width + 8;
                    var rr = new Rectangle(x, 52, rw, 20);
                    using (var p = FpsUi.Round(rr, 5f))
                    using (var br = new SolidBrush(FpsUi.Ok)) g.FillPath(br, p);
                    TextRenderer.DrawText(g, r, FpsUi.Small, rr, FpsUi.BgMain,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    x = rr.Right + 8;
                }
                if (_o.PrixCts >= 0)
                {
                    string prix = Offres.Prix(_o.PrixCts);
                    TextRenderer.DrawText(g, prix, FpsUi.H2, new Point(x, 50), FpsUi.Gold, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                    x += TextRenderer.MeasureText(prix, FpsUi.H2, Size.Empty, TextFormatFlags.NoPadding).Width + 8;
                    if (_o.Remise > 0 && _o.PrixOrigineCts > 0)
                        TextRenderer.DrawText(g, Offres.Prix(_o.PrixOrigineCts), Barre, new Point(x, 55), FpsUi.Dim2, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                }

                var details = new List<string> { Plateforme() };
                if (_o.Fin.HasValue) details.Add("jusqu'au " + _o.Fin.Value.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture));
                else if (_o.Sortie.HasValue) details.Add("sortie le " + _o.Sortie.Value.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                TextRenderer.DrawText(g, string.Join(" · ", details), FpsUi.Tiny, new Rectangle(tx, 82, L - tx - 70, 36), FpsUi.Dim,
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                bool fav = _page.EstFavori(_o);
                TextRenderer.DrawText(g, fav ? "★" : "☆", FpsUi.H2, REtoile, fav ? FpsUi.Gold : FpsUi.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                if (_o.SteamAppId > 0)
                {
                    bool suivi = _page.EstSuivi(_o);
                    TextRenderer.DrawText(g, "🔔", Emoji, RCloche, suivi ? FpsUi.Gold : FpsUi.Dim2,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);                }
            }
        }
    }
}
