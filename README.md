# HexTactica

## Ground rules

- Nothing about UI, unit or level design is written in code, and code is never used for configuration.
  - UI lives in prefabs and scenes edited in Unity. Code does not build, lay out, style or patch widgets at run time,
    and has no fallbacks for an older prefab: if a prefab lacks something, change the prefab.
  - Units are their prefabs; levels are scenario files.
  - Tunable values live in assets or serialized fields, not constants in code.
- More standing rules for working on this project: `Tests/HANDOFF.md`, section 1.
