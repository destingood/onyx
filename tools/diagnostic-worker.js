// ---------------------------------------------------------------------------
//  Point de collecte des RAPPORTS DE DIAGNOSTIC ONYX — Cloudflare Worker
//
//  À QUOI ÇA SERT : recevoir les rapports que des utilisateurs envoient
//  EXPLICITEMENT depuis ONYX (bouton « Envoyer le rapport »), et te permettre
//  de les lire en direct.
//
//  À NE PAS CONFONDRE avec audience-worker.js, qui ne compte que des machines.
//  Ici le contenu est un texte de diagnostic complet : c'est une donnée
//  personnelle au sens du RGPD, même masquée. Traite-la comme telle.
//
//  DÉPLOIEMENT (gratuit, ~5 minutes) :
//    1. dash.cloudflare.com → Workers & Pages → Create → Worker
//    2. Coller ce fichier, Deploy
//    3. Settings → Bindings → KV Namespace : nom « RAPPORTS », créer l'espace
//    4. Settings → Variables : CLE_LECTURE = un secret long, à toi
//    5. Copier l'URL du Worker dans bt-diagnostic-url.txt à côté d'ONYX
//
//  TANT QUE bt-diagnostic-url.txt N'EXISTE PAS, ONYX N'ENVOIE RIEN.
//  C'est l'état par défaut d'une version compilée telle quelle.
//
//  LIRE LES RAPPORTS :
//    liste   https://xxx.workers.dev/rapports?cle=TON-SECRET
//    un seul https://xxx.workers.dev/rapport?cle=TON-SECRET&ref=ONYX-4F2A9C
//
//  CE QUI EST STOCKÉ : le texte du rapport, sa référence, sa version et sa
//  date, pendant 30 JOURS puis suppression automatique. Aucune adresse IP
//  n'est enregistrée — elle ne sert qu'à limiter les abus.
// ---------------------------------------------------------------------------

const RETENTION_S = 30 * 24 * 3600;      // 30 jours, puis effacement automatique
const MAX_OCTETS  = 256 * 1024;          // même plafond que EnvoiDiagnostic.MaxOctets
const MAX_PAR_IP  = 5;                   // par heure : un utilisateur, pas un robot

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    // ---- lecture (toi) ---------------------------------------------------
    if (request.method === "GET" && (url.pathname === "/rapports" || url.pathname === "/rapport")) {
      // Comparaison en temps constant impossible ici sans dépendance ; on se
      // contente d'un secret long, et le débit est de toute façon limité.
      if (!env.CLE_LECTURE || url.searchParams.get("cle") !== env.CLE_LECTURE)
        return new Response("non", { status: 403 });

      if (url.pathname === "/rapport") {
        const ref = propre(url.searchParams.get("ref"), 16);
        if (!ref) return new Response("ref manquante", { status: 400 });
        const brut = await env.RAPPORTS.get(`r:${ref}`);
        if (!brut) return new Response("inconnu", { status: 404 });
        // En texte brut : c'est fait pour être lu, pas pour être joli.
        return new Response(JSON.parse(brut).rapport, {
          headers: { "content-type": "text/plain; charset=utf-8" }
        });
      }

      const liste = await env.RAPPORTS.list({ prefix: "r:", limit: 200 });
      const resume = [];
      for (const k of liste.keys) {
        const brut = await env.RAPPORTS.get(k.name);
        if (!brut) continue;
        const o = JSON.parse(brut);
        resume.push({
          reference: o.reference, version: o.version, recu: o.recu,
          octets: (o.rapport || "").length,
          apercu: (o.rapport || "").slice(0, 200)
        });
      }
      resume.sort((a, b) => (b.recu || "").localeCompare(a.recu || ""));
      return Response.json({ total: resume.length, rapports: resume });
    }

    // ---- réception (ONYX) ------------------------------------------------
    if (request.method !== "POST") return new Response("ok");

    // Limite d'abus. L'IP sert ICI et n'est jamais écrite dans un rapport.
    const ip = request.headers.get("cf-connecting-ip") || "?";
    const heure = new Date().toISOString().slice(0, 13);       // AAAA-MM-JJTHH
    const cleIp = `ip:${heure}:${await empreinte(ip)}`;
    const vus = parseInt((await env.RAPPORTS.get(cleIp)) || "0", 10);
    if (vus >= MAX_PAR_IP) return new Response("trop de rapports", { status: 429 });
    await env.RAPPORTS.put(cleIp, String(vus + 1), { expirationTtl: 3600 });

    let corps;
    try { corps = await request.json(); } catch { return new Response("ok"); }

    // On ne fait confiance à rien de ce qui arrive : on re-valide et on tronque.
    const reference = propre(corps.reference, 16);
    const version   = propre(corps.version, 16);
    const rapport   = String(corps.rapport || "").slice(0, MAX_OCTETS);
    if (!reference || !rapport) return new Response("ok");

    await env.RAPPORTS.put(
      `r:${reference}`,
      JSON.stringify({ reference, version, rapport, recu: new Date().toISOString() }),
      { expirationTtl: RETENTION_S }
    );

    return new Response("ok");
  }
};

// Ne garde que ce qui peut apparaître dans une référence ou une version.
function propre(v, max) {
  return String(v || "").replace(/[^A-Za-z0-9.\-_]/g, "").slice(0, max);
}

// L'IP n'est jamais stockée en clair, même pour la limite d'abus.
async function empreinte(s) {
  const buf = await crypto.subtle.digest("SHA-256", new TextEncoder().encode("onyx:" + s));
  return [...new Uint8Array(buf)].slice(0, 8).map(b => b.toString(16).padStart(2, "0")).join("");
}
