// Writes ../territory.js: the MOCK territory layer for the map prototypes (none of it exists in the game).
// Deterministic: no randomness, only the real star data and fixed rules, so every run gives the same file.
//   node prototypes/map-ux/concept-ab/tools/make-territory.js
'use strict';
const fs = require('fs'), path = require('path');
global.window = {};
const dataDir = path.join(__dirname, '..', '..', 'data');
for (const f of ['local.js', 'mock.js']) eval(fs.readFileSync(path.join(dataDir, f), 'utf8'));
const L = window.HOLLOW_LOCAL, M = window.HOLLOW_MOCK, S = L.systems, N = S.name.length;

const P = i => [S.x[i], S.y[i], S.z[i]];
const d3 = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
const dXZ = (a, b) => Math.hypot(a[0] - b[0], a[2] - b[2]);
const inDanger = i => M.danger.some(z => d3(P(i), z.centre) <= z.radiusLy);
const stationSys = new Set(M.stations.map(s => s.sys)), playerSys = new Set(M.players.map(p => p.sys));
const quality = i => S.sum[i][3] * 2 + (S.scoop[i] ? 3 : 0) + (S.cat[i] ? 2 : 0) + (S.sum[i][6] ? 3 : 0);
const byQuality = (a, b) => quality(b) - quality(a) || a - b;
const home = M.home.sys, H = P(home);
const owner = new Map(); // sys -> faction id
const factions = [];

// 1. Your space: the home base plus four outposts 7-25 ly away, each on a landable world, spread out.
const youSys = [{ sys: home, role: 'home', name: M.home.name, radiusLy: 7 }];
{
  const c = [];
  for (let i = 0; i < N; i++) {
    const d = d3(P(i), H);
    if (d >= 7 && d <= 25 && S.sum[i][3] > 0 && !inDanger(i) && !stationSys.has(i) && !playerSys.has(i)) c.push(i);
  }
  c.sort((a, b) => byQuality(a, b) || d3(P(a), H) - d3(P(b), H));
  const names = ['Ember', 'Lark', 'Cinder', 'Wick'];
  for (const i of c) {
    if (youSys.length > names.length) break;
    if (youSys.every(o => d3(P(i), P(o.sys)) >= 9 && dXZ(P(i), P(o.sys)) >= 5)) youSys.push({ sys: i, role: 'outpost', name: 'Outpost ' + names[youSys.length - 1], radiusLy: 5.5 });
  }
  youSys.forEach(o => owner.set(o.sys, 'you'));
  factions.push({ id: 'you', name: 'Your space', relation: 'self', color: '#5fe3c0', capital: home, systems: youSys });
}

// Directions on the plane from home: toward the outpost farthest out (hostile neighbour), its opposite, and a side.
const far = youSys.slice(1).sort((a, b) => dXZ(P(b.sys), H) - dXZ(P(a.sys), H))[0];
let dir = [P(far.sys)[0] - H[0], P(far.sys)[2] - H[2]]; const dl = Math.hypot(dir[0], dir[1]); dir = [dir[0] / dl, dir[1] / dl];
const rot = (v, a) => [v[0] * Math.cos(a) - v[1] * Math.sin(a), v[0] * Math.sin(a) + v[1] * Math.cos(a)];

function cluster(id, name, relation, color, toward, distLy, count, keepOff) {
  const T = [H[0] + toward[0] * distLy, H[1], H[2] + toward[1] * distLy];
  const clear = i => !owner.has(i) && !inDanger(i) && !playerSys.has(i) && keepOff.every(s => dXZ(P(i), P(s)) >= 11);
  let cap = -1;
  for (let r = 8; cap < 0 && r < 40; r += 4) {
    const c = []; for (let i = 0; i < N; i++) if (d3(P(i), T) <= r && S.cat[i] && clear(i)) c.push(i);
    c.sort(byQuality); cap = c.length ? c[0] : -1;
  }
  const sys = [{ sys: cap, role: 'capital', radiusLy: 6.5 }];
  owner.set(cap, id);
  const c = []; for (let i = 0; i < N; i++) if (i !== cap && d3(P(i), P(cap)) <= 15 && clear(i)) c.push(i);
  c.sort((a, b) => byQuality(a, b) || d3(P(a), P(cap)) - d3(P(b), P(cap)));
  for (const i of c) {
    if (sys.length >= count) break;
    if (sys.every(o => d3(P(i), P(o.sys)) >= 4.2)) { sys.push({ sys: i, role: 'member', radiusLy: 4.2 }); owner.set(i, id); }
  }
  factions.push({ id, name, relation, color, capital: cap, systems: sys });
  return sys.map(o => o.sys);
}
const yours = youSys.map(o => o.sys);
// The hostile neighbour sits just beyond your farthest outpost, so the two footprints meet along a contested border.
const karth = cluster('karth', 'Karth Hegemony', 'hostile', '#ff7a5c', dir, dXZ(P(far.sys), H) + 21, 16, yours.filter(s => s !== far.sys));
const tessaly = cluster('tessaly', 'Tessaly Accord', 'neutral', '#a7b2ff', rot(dir, Math.PI), 44, 13, [...yours, ...karth]);
const orrin = cluster('orrin', 'Orrin Free Holds', 'friendly', '#e6c46e', rot(dir, Math.PI / 2), 56, 10, [...yours, ...karth, ...tessaly]);

// 2. Contested border: unclaimed systems between your space and the Karth Hegemony, claimed by both.
const contested = [];
{
  const c = [];
  for (let i = 0; i < N; i++) {
    if (owner.has(i) || inDanger(i)) continue;
    const dy = Math.min(...yours.map(s => dXZ(P(i), P(s)))), dk = Math.min(...karth.map(s => dXZ(P(i), P(s))));
    if (dy <= 12 && dk <= 10 && Math.abs(P(i)[1] - H[1]) < 40) c.push([i, dy + dk]);
  }
  c.sort((a, b) => a[1] - b[1] || a[0] - b[0]);
  for (const [i] of c) {
    if (contested.length >= 3) break;
    if (contested.every(o => d3(P(i), P(o.sys)) >= 4)) { contested.push({ sys: i, claimants: ['you', 'karth'], radiusLy: 4.5 }); owner.set(i, 'contested'); }
  }
}

// 3. Frontier: unclaimed systems within one jump (12.5 ly) of your space.
const frontier = [];
for (let i = 0; i < N; i++) if (!owner.has(i) && yours.some(s => d3(P(i), P(s)) <= M.ship.jumpRangeLy)) frontier.push(i);

const decorate = o => Object.assign({ sys: o.sys, system: S.name[o.sys], addr: S.addr[o.sys] }, o);
const out = {
  meta: {
    mock: true,
    what: 'MOCK territory for the map prototypes: your space (home base and outposts), three NPC factions, a contested border and the unclaimed frontier. None of it exists in the game or its generator.',
    generatedBy: 'concept-ab/tools/make-territory.js (deterministic: fixed rules over the real star data, no randomness)',
    localCount: N,
    sys: "'sys' fields are indices into HOLLOW_LOCAL.systems (same export); 'addr' and 'system' repeat the address and name.",
    radiusLy: 'Each controlled system claims a disc of this radius on the galactic plane; a faction\'s region is the smooth union of its discs.',
  },
  factions: factions.map(f => ({ id: f.id, name: f.name, relation: f.relation, color: f.color, capital: f.capital, systems: f.systems.map(decorate) })),
  contested: contested.map(decorate),
  frontier: { what: 'Unclaimed systems within one jump (12.5 ly) of your space', systems: frontier },
};
const json = JSON.stringify(out).replace(/[\u007f-￿]/g, c => '\\u' + c.charCodeAt(0).toString(16).padStart(4, '0'));
const file = '// Hollow Universe: MOCK territory layer for the map prototypes (none of this exists in the game yet).\n' +
  '// Generated by concept-ab/tools/make-territory.js from the real star data; deterministic. Do not edit; regenerate.\n' +
  'window.HOLLOW_TERRITORY = ' + json + ';\n';
fs.writeFileSync(path.join(__dirname, '..', 'territory.js'), file);
for (const f of out.factions) console.log(f.id.padEnd(8), f.name.padEnd(18), f.relation.padEnd(9), f.systems.length, 'systems', f.systems.slice(0, 5).map(s => s.system + ' (' + d3(P(s.sys), H).toFixed(1) + ' ly)').join(', '));
console.log('contested', out.contested.map(c => c.system + ' (' + d3(P(c.sys), H).toFixed(1) + ' ly)').join(', '));
console.log('frontier', frontier.length, 'systems');
