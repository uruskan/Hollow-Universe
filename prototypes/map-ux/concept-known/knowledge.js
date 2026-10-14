/* Hollow Universe map, concept Known: what you know. MOCK.
 *
 * Nothing in this file exists in the game yet. It stands in for what a player would know after a few weeks of play,
 * built by fixed rules from the real star data, with no randomness: every load gives the same result.
 *   - Your bases: the home base on Gliese 54.1 (from ../data/mock.js) and three outposts 19 to 23 ly away.
 *   - Visited systems: the stops of eight past trips, laid out by the real route planner (data.js).
 *   - Three drone surveys (spheres of 20 to 26 ly, 45 to 70 ly from home) and what they found.
 *   - Seen: the bright named stars you can see from home. You know their name and colour, nothing else.
 * Everything else is unknown. While the map runs, drones add surveys (addSurvey) and trips add visits (visit).
 *
 * Levels: 0 unknown, 1 seen, 2 scanned by a drone, 3 visited, 4 your base.
 * Finds follow docs/LORE.md: remains of the vanished civilisation (ruins, wrecks), resources, other players' bases and
 * fuel stars. There are no stations, factions, aliens or raiders anywhere.
 *
 * Interface (window.HOLLOW_KNOWLEDGE)
 *   level[i], groupOf[i] (index into groups or -1), since[i] (performance.now() when it became known; 0 = before),
 *   knownList (the indices of every known star), version (bumps on every change)
 *   groups: [{ id, kind: 'home' | 'survey' | 'route', name, members, centre, radius, finds, drone, when, summary() }]
 *   finds: [{ id, kind: 'monument' | 'wreck' | 'resource' | 'player' | 'fuel', sys, body, title, sub, group }]
 *   bases: [{ sys, name, body, role: 'home' | 'outpost' }], drones: [...], reports: [...]
 *   findsAt(i), baseAt(i), playerFound(i), groupById(id), whenOf(i), addSurvey(opts), visit(i, now), countWithin(p, r)
 */
(function () {
  'use strict';
  const L = window.HOLLOW_LOCAL, M = window.HOLLOW_MOCK, HD = window.HD;
  if (!L || !M || !HD) throw new Error('knowledge.js needs ../data/local.js, ../data/mock.js and data.js first');
  const S = L.systems, N = S.name.length, X = S.x, Y = S.y, Z = S.z, SUM = S.sum, MAG = S.mag, CLS = S.cls;
  const LV = { UNKNOWN: 0, SEEN: 1, SCANNED: 2, VISITED: 3, BASE: 4 };
  const level = new Uint8Array(N), groupOf = new Int16Array(N).fill(-1), since = new Float64Array(N);
  const whenOf = new Array(N).fill('');
  const byName = n => S.name.indexOf(n);
  const dist = (i, p) => Math.hypot(X[i] - p[0], Y[i] - p[1], Z[i] - p[2]);
  const posOf = i => [X[i], Y[i], Z[i]];
  // FNV-1a over a string: the only source of variety (deterministic)
  const hash = s => {
    let h = 2166136261; for (let k = 0; k < s.length; k++) { h ^= s.charCodeAt(k); h = Math.imul(h, 16777619); }
    h ^= h >>> 16; h = Math.imul(h, 0x85ebca6b); h ^= h >>> 13; h = Math.imul(h, 0xc2b2ae35); h ^= h >>> 16;   // finaliser
    return h >>> 0;
  };
  const h01 = (i, salt) => (hash(S.addr[i] + ':' + salt) % 100000) / 100000;
  const isProper = i => S.cat[i] === 1 && !/\d/.test(S.name[i]);
  const shortName = i => { const n = S.name[i]; return n.startsWith('Nolcas Peris ') ? n.slice(13) : n; };
  const bodiesOf = i => L.bodies[String(i)] || null;
  const raise = (i, lv, date) => { if (lv > level[i]) { level[i] = lv; if (date) whenOf[i] = date; } };

  const K = {
    meta: {
      mock: true,
      what: 'MOCK knowledge for the map prototype: your bases, past trips, drone surveys and what they found, and the bright stars seen from home. Built by fixed rules from the real star data; none of it exists in the game.',
    },
    LV, level, groupOf, since, groups: [], finds: [], bases: [], drones: [], reports: [], version: 1,
    get knownList() { if (knownVer !== K.version) rebuildKnown(); return knownCache; },
  };
  let knownCache = [], knownVer = -1;
  function rebuildKnown() { knownCache = []; for (let i = 0; i < N; i++) if (level[i]) knownCache.push(i); knownVer = K.version; }

  // ---------- your bases ----------
  const landableBody = i => { const b = bodiesOf(i); if (!b) return null; const k = b.findIndex(q => q[1] === 1 && q[9]); return k >= 0 ? b[k][0] : null; };
  const HOME = M.home.sys;
  K.bases.push({ sys: HOME, name: 'Home base', body: M.home.body, role: 'home' });
  [['Achird', 'Outpost Ember'], ['Gliese 783 A', 'Outpost Cinder'], ['Tabit', 'Outpost Lark']].forEach(([star, name]) => {
    const i = byName(star); if (i >= 0) K.bases.push({ sys: i, name, body: landableBody(i), role: 'outpost' });
  });
  for (const b of K.bases) raise(b.sys, LV.BASE, b.role === 'home' ? 'since 14 Sep' : 'built ' + ['', '22 Sep', '25 Sep', '28 Sep'][K.bases.indexOf(b)]);

  // ---------- seen from home: bright named stars (apparent magnitude under 2.5 from the home system) ----------
  for (let i = 0; i < N; i++) {
    if (!isProper(i)) continue;
    const d = Math.max(0.1, dist(i, posOf(HOME)));
    if (MAG[i] + 5 * Math.log10(d / 32.6) < 2.5) raise(i, LV.SEEN, 'seen from home');
  }

  // ---------- past trips: the planner's route for each; every stop was visited ----------
  const TRIPS = [
    { from: 'Gliese 54.1', to: 'Sol', when: '16 Sep' },
    { from: 'Sol', to: 'Alpha Centauri', when: '17 Sep' },
    { from: 'Gliese 54.1', to: 'Achird', when: '21 Sep' },
    { from: 'Gliese 54.1', to: 'Gliese 783 A', when: '24 Sep' },
    { from: 'Gliese 54.1', to: 'Tabit', when: '27 Sep' },
    { from: 'Gliese 54.1', to: 'Vega', when: '30 Sep' },
    { from: 'Gliese 54.1', to: 'Capella', when: '3 Oct' },
  ];
  const tripStops = [];
  function walkTrip(a, b, when) {
    const r = HD.planRoute(a, b, { fuel: HD.FUEL_MAX });
    const stops = r && r.ok ? r.stops.map(s => s.sys) : [a, b];
    stops.forEach(s => raise(s, LV.VISITED, 'visited ' + when));
    return stops;
  }
  for (const t of TRIPS) tripStops.push({ to: byName(t.to), when: t.when, stops: walkTrip(byName(t.from), byName(t.to), t.when) });

  // ---------- finds: properties of a system, found when a drone scans it ----------
  const RUINS = [
    ['Old mine complex', 'old industrial'], ['Collapsed relay array', 'old civil'], ['Flooded smelter', 'old industrial'],
    ['Colony town ruins', 'old colony'], ['Buried garrison', 'old military'], ['Overgrown power plant', 'old civil'],
    ['Terraforming plant ruins', 'old civil'], ['Launch complex ruins', 'old military'], ['Abandoned ore port', 'old industrial'],
    ['Homestead ruins', 'old colony'],
  ];
  const WRECKS = ['Wreck of a bulk hauler', 'Wreck of an ore carrier', 'Broken-backed freighter', 'Gunship wreck'];
  const RARE = 1 << HD.RES.indexOf('rare metals');
  const pickBody = (i, salt, test) => {
    const b = bodiesOf(i);
    if (b) { const ks = []; b.forEach((q, k) => { if (q[1] !== 0 && q[1] !== 3 && test(q, k)) ks.push(k); }); if (ks.length) return b[ks[hash(S.addr[i] + salt) % ks.length]][0]; }
    const n = Math.max(1, SUM[i][1]); return S.name[i] + ' ' + (1 + hash(S.addr[i] + salt) % n);
  };
  const playerAt = new Map(); for (const p of M.players) playerAt.set(p.sys, p);
  function findsIn(members, g) {
    const out = [];
    for (const i of members) {
      const p = playerAt.get(i);
      if (p) out.push({ kind: 'player', sys: i, body: p.body, title: p.name, sub: p.relation[0].toUpperCase() + p.relation.slice(1) + ' player · on ' + p.bodyType.toLowerCase() + ' ' + p.body.replace(S.name[i] + ' ', ''), relation: p.relation, player: p });
      if (SUM[i][3] > 0 && h01(i, 'ruin') < 0.010) {
        const [title, family] = RUINS[hash(S.addr[i] + 'ruin-kind') % RUINS.length];
        const body = pickBody(i, 'ruin-body', q => q[9]);
        out.push({ kind: 'monument', sys: i, body, title, sub: 'Ruins · ' + family + ' · on ' + body.replace(S.name[i] + ' ', 'planet '), family });
      }
      if (h01(i, 'wreck') < 0.003) out.push({ kind: 'wreck', sys: i, body: null, title: WRECKS[hash(S.addr[i] + 'wreck-kind') % WRECKS.length], sub: 'Wreck · old ship · in orbit of ' + shortName(i) });
      if ((HD.resMask[i] & RARE) && h01(i, 'rich') < 0.015) {
        const body = pickBody(i, 'rich-body', (q, k) => q[9] && ((HD.resByBody.get(i + ':' + k) || RARE) & RARE));
        out.push({ kind: 'resource', sys: i, body, title: 'Rich rare-metal deposit', sub: 'Resource · rare metals · on ' + body.replace(S.name[i] + ' ', 'planet ') });
      }
    }
    // one fuel star per survey: the brightest scoopable A, F or G star, if it is bright enough to matter
    let best = -1;
    for (const i of members) if (HD.canRefuel[i] && /^[AFG]/.test(CLS[i]) && MAG[i] < 4.2 && (best < 0 || MAG[i] < MAG[best])) best = i;
    if (best >= 0) out.push({ kind: 'fuel', sys: best, body: null, title: 'Fuel star', sub: 'Fuel · ' + CLS[best] + ' · bright and quick to scoop' });
    const order = { player: 0, monument: 1, wreck: 2, resource: 3, fuel: 4 };
    out.sort((a, b) => order[a.kind] - order[b.kind]);
    out.forEach(f => { f.group = g; f.id = K.finds.length; K.finds.push(f); });
    return out;
  }

  // ---------- groups (the roots of the map's tiles) ----------
  function makeGroup(o) {
    const g = Object.assign({ finds: [], drone: null, when: '' }, o);
    g.index = K.groups.length;
    g.summary = () => {
      const n = g.members.length, f = g.finds.filter(q => !q.hidden).length;
      return n + (n === 1 ? ' system' : ' systems') + (f ? ' · ' + f + (f === 1 ? ' find' : ' finds') : '');
    };
    K.groups.push(g);
    return g;
  }
  const centreOf = ms => { const c = [0, 0, 0]; for (const i of ms) { c[0] += X[i]; c[1] += Y[i]; c[2] += Z[i]; } return c.map(v => v / Math.max(1, ms.length)); };
  const radiusOf = (ms, c) => { let r = 0; for (const i of ms) r = Math.max(r, dist(i, c)); return r; };

  // home: your bases and every system you visited within 26 ly of home
  const homeMembers = [];
  for (let i = 0; i < N; i++) if (level[i] >= LV.VISITED && dist(i, posOf(HOME)) <= 26) homeMembers.push(i);
  const homeC = posOf(HOME);
  const home = makeGroup({ id: 'home', kind: 'home', name: 'Home', members: homeMembers, centre: homeC, radius: radiusOf(homeMembers, homeC), when: 'since 14 Sep' });
  for (const i of homeMembers) groupOf[i] = home.index;

  // surveys: every system inside the sphere becomes known (scanned), and its finds are reported
  function surveyName(members, centre) {
    let best = -1;
    for (const i of members) if (isProper(i) && (best < 0 || MAG[i] < MAG[best])) best = i;
    if (best < 0) for (const i of members) if (S.cat[i] && (best < 0 || dist(i, centre) < dist(best, centre))) best = i;
    if (best < 0) { let bd = Infinity; for (const i of members) { const d = dist(i, centre); if (d < bd) { bd = d; best = i; } } }
    return best >= 0 ? shortName(best) + ' survey' : 'Survey';
  }
  function survey({ centre, radius, drone, when, now = 0, id }) {
    const members = [], wave = [];
    for (let i = 0; i < N; i++) {
      const d = dist(i, centre); if (d > radius) continue;
      if (groupOf[i] >= 0 && K.groups[groupOf[i]].kind !== 'route') continue;   // already part of home or another survey
      members.push(i); wave.push(d / radius);
    }
    let name = surveyName(members, centre);
    if (K.groups.some(g => g.name === name)) name += ' ' + (K.groups.filter(g => g.kind === 'survey').length + 1);
    const g = makeGroup({ id: id || 'sv' + K.groups.length, kind: 'survey', name, members, centre: centre.slice(), radius, drone, when });
    members.forEach((i, k) => {
      const wasKnown = level[i] > 0;
      raise(i, LV.SCANNED, 'scanned by ' + drone + ' · ' + when);
      groupOf[i] = g.index;
      if (now && !wasKnown) since[i] = now + wave[k] * 1300;          // the reveal runs outward from the centre
      else if (now) since[i] = Math.max(since[i], now + wave[k] * 1300 - 900);
    });
    g.finds = findsIn(members, g);
    K.version++;
    return g;
  }
  survey({ id: 'sv-ash', centre: [40, 36, 16], radius: 20, drone: 'Tern', when: '2 Oct' });
  survey({ id: 'sv-quill', centre: [-4, 55, -60], radius: 22, drone: 'Moth', when: '5 Oct' });
  survey({ id: 'sv-far', centre: [-52, 4, -30], radius: 26, drone: 'Tern', when: '11 Oct' });

  // the expedition that followed the 5 Oct report: home to the first ruin it found
  const south = K.groups.find(g => g.id === 'sv-ash');
  const ruin = south.finds.find(f => f.kind === 'monument' || f.kind === 'wreck') || south.finds[0];
  if (ruin) tripStops.push({ to: ruin.sys, when: '8 Oct', stops: walkTrip(HOME, ruin.sys, '8 Oct') });
  // trips: the stops that no other group holds become a route group of their own
  for (const t of tripStops) {
    const rest = t.stops.filter(i => groupOf[i] < 0 && level[i] >= LV.VISITED);
    if (rest.length < 2) continue;
    const c = centreOf(rest);
    const g = makeGroup({ id: 'trip-' + t.to, kind: 'route', name: 'Trip to ' + shortName(t.to), members: rest, centre: c, radius: radiusOf(rest, c), when: t.when });
    for (const i of rest) groupOf[i] = g.index;
  }

  // ---------- drones (mock state; app.js animates them) ----------
  K.drones = [
    { id: 'moth', name: 'Moth', state: 'surveying', at: [52, 4, -38], target: [52, 4, -38], radius: 20, progress: 0.64, etaMin: 72 },
    { id: 'tern', name: 'Tern', state: 'returning', from: [-52, 4, -30], at: null, target: posOf(HOME), progress: 0.55, etaMin: 41, carrying: K.groups.find(g => g.id === 'sv-far').finds.length },
    { id: 'gnat', name: 'Gnat', state: 'idle', at: posOf(HOME), target: null, progress: 0, etaMin: 0 },
  ];
  K.reports = K.groups.filter(g => g.kind === 'survey').map(g => ({ drone: g.drone, group: g, when: g.when })).reverse();

  // ---------- lookups and live changes ----------
  const findsBySys = new Map();
  function indexFinds() { findsBySys.clear(); for (const f of K.finds) { if (!findsBySys.has(f.sys)) findsBySys.set(f.sys, []); findsBySys.get(f.sys).push(f); } }
  indexFinds();
  const baseBySys = new Map(K.bases.map(b => [b.sys, b]));
  K.findsAt = i => findsBySys.get(i) || [];
  K.baseAt = i => baseBySys.get(i) || null;
  K.playerFound = i => K.findsAt(i).some(f => f.kind === 'player');
  K.groupById = id => K.groups.find(g => g.id === id) || null;
  K.whenOf = i => whenOf[i];
  K.countWithin = (p, r) => { let n = 0; for (let i = 0; i < N; i++) if (dist(i, p) <= r) n++; return n; };
  K.addSurvey = opts => { const g = survey(Object.assign({ when: '13 Oct' }, opts)); indexFinds(); K.reports.unshift({ drone: opts.drone, group: g, when: 'just now', fresh: true }); return g; };
  K.visit = (i, now) => {
    if (level[i] >= LV.VISITED) return false;
    if (!level[i] && now) since[i] = now;
    raise(i, LV.VISITED, 'visited 13 Oct');
    K.version++;
    return true;
  };
  K.HOME = HOME;
  window.HOLLOW_KNOWLEDGE = K;
})();
