# Building

1. Install the .NET 8 SDK.
2. Fill `lib/` as described in [lib/README.md](lib/README.md).
3. Build:

   ```sh
   ./tools/build.sh
   ```

   Pass: the last line reads `dist/MarsSetup.exe 40M`.

Models: each script in `models/` runs in Blender 4.2 (or the `bpy` 4.2
Python module) and writes a `.glb` and preview pictures:

```sh
python models/drill.py -- out/drill
```

Copy the `.glb` into `assets/models/` and rebuild.

## Rules for code here

- No hook on a method with a struct parameter or return value
  ([docs/HOOKS.md](docs/HOOKS.md)).
- Everything Mars-only checks that a Mars world is open.
- New things other mods could collide with get a string id
  (`marsmod.category.name`).
- No em dashes or en dashes anywhere; CI checks.
