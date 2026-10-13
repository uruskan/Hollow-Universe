# Hollow Universe — Lore

Status: draft 1, 13 October 2026. Owner: Yusuf. The setting, kept as simple as Rust's. Read it before you make a
model, texture, sound, name, item description or monument, so that everything belongs to the same world.
[GAME_DESIGN.md](GAME_DESIGN.md) says what the game is; this says only where it happens.

Every rule is marked **Decided** (the owner said so), **Proposal** (waiting for a yes or no) or **Open**.

---

## 1. The whole story (Decided)

Once, a great human civilisation spread across the galaxy. It built towns, mines, power grids, ships and stations
on thousands of worlds. Then it vanished, and nobody knows why.

What it left behind are the monuments. Players arrive among them with nothing, and nobody knows who they are or where
they came from.

That is all of it. There is no more history, no factions and no story to tell. The game is called Hollow because
the galaxy is: full of what somebody built, with nobody left in it.

---

## 2. Rules

| Topic | Rule | Status |
|---|---|---|
| The old civilisation | Human. It spread across the galaxy and then vanished. The docs call it "the old civilisation"; the game never names it, and it has no government, leaders, dates or wars to tell. | Decided (no name: Proposal) |
| Why it vanished | Nobody knows, and the game never answers. | Decided |
| How long ago | Long enough that nobody remembers: everything is rusted, overgrown, half-buried or fallen. No number is ever given. | Proposal |
| Is anywhere still alive? | No. No city, government or populated region survives anywhere, Sol included. Earth is a ruin like every other world. | Decided (Earth: Proposal) |
| The players | Origins unknown, as in Rust. Nothing explains why they arrive, why they come back at a sleeping bag or bed, or how they know how to build. Never explain it. | Decided |
| Factions | None. No factions, no history, no campaign. Enemies at monuments are kinds of enemy (§5), not sides with a story. | Decided |
| Aliens | None known. Nothing in the game is not human: no alien ruins, artefacts, ships or intelligent creatures. A world's animals are animals. | Decided |
| Magic | None: no magic, psychic powers, ancient energies or the like. The game is as down to earth as Rust. | Decided |
| The rumour | One rumour, almost never heard (§3). | Decided |

---

## 3. The rumour (Decided; its wording Open)

Very rarely there is a rumour that humanity was wiped out by machines made in its own image. The game almost never
mentions it: a line scratched into a wall, a torn note at a rare site. It is never a cutscene, a quest, a boss or an
explanation.

To keep it a rumour:

- Nothing ever confirms or denies it.
- The whole game holds only a handful of traces, each one deniable.
- **No humanoid robots anywhere** (Proposal). The old civilisation's machines are turrets, drones, crawlers and
  gunships (§5), never walking metal men; one walking machine in a ruin would turn the rumour into the story.
- **Its words are Open.** The owner calls them "men of iron", but that is Warhammer 40,000's name for exactly this
  idea, and other franchises' names are never used (GAME_DESIGN.md §10). The rumour needs words of our own.

---

## 4. Technology

Rust's technology, a step further: people fly between stars. Every asset belongs to one of two layers.

| Layer | What | Look | Status |
|---|---|---|---|
| **Old** | What the old civilisation built: monuments, wrecks, its grid, its machines | Engineered and industrial: concrete, steel frames, panels, cables, stencilled codes. Well made once, now decayed. Believable near-future, never glossy. | Proposal |
| **Now** | What players and Reclaimers make | Scrap: welded sheet, bolts, salvaged panels, mismatched paint, tape and wire. It works, and it looks hand-made. | Decided |

**The Tin Finch is the measure** (Decided): a real spaceship, built as if in a scrapyard today. Better gear is better
built from better salvage and old parts, still by hand; it never turns sleek or clean.

- **It looks like it works.** Hinges, bolts, cables, vents, exhausts, fuel caps. Nothing glows without a lamp to
  glow from. (Proposal)
- **No sci-fi glamour.** No energy blades, force-field walls, floating cities or glowing crystals. Weapons are
  firearms, explosives and melee, as in Rust. (Proposal)
- **Screens and lights are hardware:** lamps, LEDs, old monitors. The placement ghost and other holograms of the
  interface belong to the interface, not to the world. (Proposal)
- **The best technology is found, not invented.** The best parts and blueprints come from the ruins (GAME_DESIGN.md
  pillar 4, MONUMENTS.md §7). (Proposal)

---

## 5. Who you meet

| Who | What | Status |
|---|---|---|
| **Other players** | The real danger. | Decided |
| **Reclaimers** | Armed people who live off the ruins, as Rust has bandits and scientists. Where they come from is as unknown as the players. Not a faction: no leaders, clans or history. The Reclaimer Camp is simply where some of them trade. | Proposal (the name) |
| **Wardens** | The old civilisation's security machines, still guarding what they were told to guard: turrets, drones, tracked crawlers, gunships. Never humanoid (§3). | Proposal (the name) |
| **Animals** | Each world's wildlife. Nothing intelligent. | Decided |

---

## 6. How ruins look

A ruin shows what it was and how it fell (abandoned, burnt, buried, flooded, overgrown, picked clean), never who
fought whom. Each one belongs to a visual family. The data keys are internal and keep the first draft's names for now
(§7).

| Family | Data key | Look | Where |
|---|---|---|---|
| Old civil | `seedward-civil` | Off-white and pale grey plating, safety orange and deep teal bands, rounded corners, stencilled codes, big louvres | Grids, relays, water, power, terraforming, labs |
| Old industrial | `seedward-industrial` | Board-formed concrete, painted steel frames, corrugated cladding, yellow rails, hazard stripes | Mines, smelters, yards, ports |
| Old military | `bulwark` | Olive and grey, armoured concrete, gun ports, revetments, sandbags | Garrisons, redoubts, launch complexes, warships |
| Old colony | — | Prefab hab cans, domes and greenhouses, solar fields, wind turbines, hand-painted signs | Homesteads, colony towns |
| Scrap | `reclaimer` | Scrap walls, tarps, welded sheet, graffiti, lights strung on cables | Reclaimer camps, and their work at every ruin |
| Ships | — | Panelled hull plating, ribs and decks exposed where it broke, burnt patches | Wrecks of every size |

---

## 7. Names and words

- In the game: "old", "ruins", "salvage", the names of places and things. Never a name for the old civilisation,
  its government or whatever ended it.
- Our own names only, never another game's or franchise's (GAME_DESIGN.md §10). Generated names use our syllable
  tables.
- **Retired names.** The first monuments draft (13 October 2026) invented a history: the Seedward Authority, its
  seeder fleets, the Quiet, the Bulwark, Reclaimer clans, players as a "second wave". None of it is the lore. Those
  names still appear in internal data keys and asset descriptions under `art/monuments` and `game/data/monuments`;
  they are never shown to players, and they get renamed when that work is next touched.

---

## 8. Open questions

1. The rumour's own words (§3).
2. Does the old civilisation stay without a name, even in item text? (Proposal: yes.)
3. The Anomaly (MONUMENTS.md §5.6, #59): without aliens, it has to be the old civilisation's own work (a failed
   experiment, say), or it goes.
