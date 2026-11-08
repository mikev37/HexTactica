# HexTactica

## Ground rules

- Nothing about UI, unit or level design is written in code, and code is never used for configuration.
  - UI lives in prefabs and scenes edited in Unity. Code does not build, lay out, style or patch widgets at run time,
    and has no fallbacks for an older prefab: if a prefab lacks something, change the prefab.
  - Units are their prefabs; levels are scenario files.
  - Tunable values live in assets or serialized fields, not constants in code.
- More standing rules for working on this project: `Tests/HANDOFF.md`, section 1.

## Supply carried by transports, ships and aircraft

A supply unit aboard something links to its side's network through what carries it (SupplyNetwork.CarriedLinked):

- A land transport or an aircraft links when, in one move of its own (its real move: range, terrain, fuel, enemies in
  the way), it can get onto supplied ground. Beside supplied ground is not enough.
- A ship links when, in one move, it can get beside supplied ground or onto a supplied harbor. Any supplied shore
  counts, not only a harbor: a shoreline city in supply links a supply ship lying off it.
- A ship also links through another carrier with supply aboard that is linked (a convoy, any length), and through an
  aircraft; an aircraft links through a linked ship that lies beside land. A ship tries its convoy first, so its supply
  line follows the water.
- What a carried supply unit then gives: its own range around its carrier; a ship only while beside land, an aircraft
  only over land. Water is never supplied ground.
- An HQ supplies only the side it began with: a captured HQ supplies nobody until it is retaken.
