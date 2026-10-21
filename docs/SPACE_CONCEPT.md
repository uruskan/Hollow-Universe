# Space concept: how the galaxy fits the game

**Historical source-project concept.** AB Test B follows [DESIGN.md](DESIGN.md), including its travel revision of
18 October 2026 and the current star-count/planet-size limits. Charged jumps, supercruise and fuel scooping are the
new travel direction. The older autopilot, drones and scale statements below are not newly approved by that choice.
See TRAVEL_IMPLEMENTATION.md for the current integration proposal and DEVELOPMENT.md for actual implementation status.

Status: **direction agreed by the owner on 13 October 2026** (the four ideas in §2). The details are proposals. Nothing in
[GAME_DESIGN.md](GAME_DESIGN.md) changes until the owner approves the table in §4.

**Scale, 14 October 2026:** the owner replaced the 1:1 galaxy with about 200 to 300 systems at real landmarks, all on one
plane, and made worlds small (an Earth-like world about 2.4 km in radius, about four Rust maps). The four ideas still hold;
where this document speaks of billions of stars, read a few hundred. Proposal: [SCALE.md](SCALE.md).

---

## 1. The problem

Rust works because its players are packed into one small map, where they keep running into each other. Elite works
through scale and solitude, but most of its play time is spent in empty space, jumping from star to star. Hollow will
often have few players online, sometimes one. **It has to be fun alone, and better with others.**

---

## 2. The four ideas (agreed)

| # | Idea | What it means |
|---|---|---|
| 1 | **Your home system is your map** | A star system has dozens of landable bodies, each a world at least as big as a Rust map (Sol alone has 9 planets and 21 moons). Day to day you live, gather, build and fight there, plus a few outposts wherever something was worth it. That's the feeling of "having space": your bases in some spots. |
| 2 | **The galaxy is your reach** | You go far on purpose (rare resources, monuments, derelicts, other players' bases), never as a chore. The trip is planned and the built-in autopilot flies it. How distance relates to rarity is not decided (§5, question 4). |
| 3 | **Drones explore for you** | You send drone ships across the galaxy while you play at home. They scan regions and report what they find: resources, monuments, activity, other players' bases. Those reports are what pull you out on an expedition. Elite's "what's in the next system" pull, without the hours of jumping. |
| 4 | **The map shows what you know** | Unknown space is a quiet haze. Your home, outposts, routes and everything your drones have scanned light up, and the stars you know get shapes (the map-UX lead in `prototypes/map-ux/LEADS.md` on branch `map-ux`). The map stays readable with billions of stars because it fills in as you learn the galaxy. |

---

## 3. Not in (owner, 13 October 2026)

- **AI raiders** or other NPC attacks on bases.
- **Territory as a mechanic**: no claiming of systems, no sovereignty, no control of space. "Your space" is only the
  feeling of having bases in some places. (Claims on the ground, GAME_DESIGN §5, are a separate thing and stay.)

---

## 4. What would change in GAME_DESIGN.md (needs the owner's approval)

| Topic | Today | Proposed |
|---|---|---|
| Rarity (§4, Decided) | Rare resources and monuments depend on star and planet type and are spread across the whole galaxy, not concentrated. | **No change for now** (owner, 13 October 2026). Distance and rarity are an open question (§5, question 4). |
| Home system (new) | n/a | Daily play centres on a home system with many landable bodies; outposts grow outward on expedition targets. Fits pillar 3, "An empire you can see". |
| Drones (new) | n/a | Drone ships you send to scan regions; their reports appear on the map. |
| Knowledge map (new) | n/a | The galaxy map shows known space; unknown space is a haze. |
| Travel (§4) | Jumps and supercruise. | Plus the built-in route autopilot: plot a route, engage, and the ship flies it. |

---

## 5. Open questions

1. Drones: how fast, how far, how many; can other players find or shoot them down?
2. Who shares what you know: just you, your group, or partly everyone (like published discoveries)?
3. The start: one home system per player, or a shared starting region around Sol (GAME_DESIGN §4 proposes a
   populated region like Elite's bubble)?
4. Distance and rarity, to decide later. The owner's current thinking: not a straight line. The starting system
   doesn't have everything, but the next system over can have it all (a steep early curve that flattens out), and a
   few really rare things are scattered anywhere in the galaxy, regardless of distance.
5. Expeditions: what a typical one looks like, how long it takes, and what you bring back.
6. Travel between landmarks (owner, 14 October 2026: a gameplay question, decided here): jump range and how a drive
   grows, routes or gates, how the autopilot takes a 15,000 ly trip. The distances are in SCALE.md §4.6.

---

## 6. What it means for the galaxy map

- The default view is your home system and the known space around it. The galaxy view shows known regions lit and
  where your drones have been.
- Only known stars get shapes; unknown space has none, so the screen never fills with billions of dots.
- Anywhere stays reachable: search and route to the far edge; drones are how unknown space becomes known.
- Settled map conclusions so far (frame, the shapes lead, camera tilt): `prototypes/map-ux/LEADS.md` on branch `map-ux`.
