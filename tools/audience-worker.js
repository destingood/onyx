// ---------------------------------------------------------------------------
//  Point de collecte d'audience ONYX — Cloudflare Worker
//
//  À QUOI ÇA SERT : compter combien de machines distinctes lancent ONYX par
//  jour et par mois, et savoir quelles versions sont installées.
//
//  DÉPLOIEMENT (gratuit, ~5 minutes) :
//    1. dash.cloudflare.com → Workers & Pages → Create → Worker
//    2. Coller ce fichier, Deploy
//    3. Settings → Bindings → KV Namespace : nom « STATS », créer l'espace
//    4. Copier l'URL du Worker (https://xxx.workers.dev) dans le fichier
//       bt-audience-url.txt à côté d'ONYX, ou le livrer avec l'installeur.
//
//  CONSULTER LES CHIFFRES : ouvrir https://xxx.workers.dev/stats?cle=CHANGE-MOI
//
//  CE QUI EST STOCKÉ : un compteur par jour et un identifiant pseudonyme par
//  jour, avec expiration automatique à 400 jours. Aucune adresse IP n'est
//  enregistrée — elle sert seulement à limiter les abus, et n'est pas écrite.
// ---------------------------------------------------------------------------

const CLE_LECTURE = "CHANGE-MOI";   // <-- change ça avant de déployer
const RETENTION_S = 400 * 24 * 3600;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === "GET" && url.pathname === "/stats") {
      if (url.searchParams.get("cle") !== CLE_LECTURE)
        return new Response("non", { status: 403 });
      return Response.json(await resume(env));
    }

    if (request.method !== "POST") return new Response("ok");

    let corps;
    try { corps = await request.json(); } catch { return new Response("ok"); }

    // On ne fait confiance à rien de ce qui arrive : on re-valide et on tronque.
    const id = propre(corps.id, 32);
    const version = propre(corps.version, 12);
    const windows = propre(corps.windows, 8);
    if (!id) return new Response("ok");

    const jour = new Date().toISOString().slice(0, 10);   // AAAA-MM-JJ

    // Un identifiant ne compte qu'une fois par jour, même s'il insiste.
    const vu = `vu:${jour}:${id}`;
    if (await env.STATS.get(vu)) return new Response("ok");
    await env.STATS.put(vu, "1", { expirationTtl: RETENTION_S });

    await incr(env, `jour:${jour}`);
    await incr(env, `mois:${jour.slice(0, 7)}:${id}`, RETENTION_S);   // pour le compte mensuel unique
    if (version) await incr(env, `ver:${jour}:${version}`);
    if (windows) await incr(env, `win:${jour}:${windows}`);

    return new Response("ok");
  }
};

function propre(v, max) {
  if (typeof v !== "string") return "";
  return v.replace(/[^A-Za-z0-9._-]/g, "").slice(0, max);
}

async function incr(env, cle, ttl) {
  const n = parseInt(await env.STATS.get(cle) || "0", 10) + 1;
  await env.STATS.put(cle, String(n), ttl ? { expirationTtl: ttl } : undefined);
  return n;
}

async function resume(env) {
  const jours = {}, versions = {};
  let curseur, liste;
  do {
    liste = await env.STATS.list({ prefix: "jour:", cursor: curseur });
    for (const k of liste.keys) jours[k.name.slice(5)] = parseInt(await env.STATS.get(k.name) || "0", 10);
    curseur = liste.cursor;
  } while (!liste.list_complete);

  curseur = undefined;
  do {
    liste = await env.STATS.list({ prefix: "ver:", cursor: curseur });
    for (const k of liste.keys) {
      const v = k.name.split(":")[2];
      versions[v] = (versions[v] || 0) + parseInt(await env.STATS.get(k.name) || "0", 10);
    }
    curseur = liste.cursor;
  } while (!liste.list_complete);

  const dates = Object.keys(jours).sort();
  const mois = new Set();
  curseur = undefined;
  do {
    liste = await env.STATS.list({ prefix: "mois:", cursor: curseur });
    for (const k of liste.keys) mois.add(k.name.slice(5));   // mois:AAAA-MM:id
    curseur = liste.cursor;
  } while (!liste.list_complete);

  const moisCourant = new Date().toISOString().slice(0, 7);
  let uniquesMois = 0;
  for (const m of mois) if (m.startsWith(moisCourant)) uniquesMois++;

  return {
    machines_aujourdhui: jours[new Date().toISOString().slice(0, 10)] || 0,
    machines_ce_mois: uniquesMois,
    par_jour: Object.fromEntries(dates.slice(-30).map(d => [d, jours[d]])),
    par_version: versions
  };
}
