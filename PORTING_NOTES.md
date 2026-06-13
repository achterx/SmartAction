# SmartAction — SPT 4.0.13 Port Notes

## What changed vs. 3.11

Every EFT update shuffles BSG's obfuscated `GClass####` / `Class####` names.
This port eliminates all hardcoded class-number references and replaces them
with runtime reflection that finds the right type by its structure.

### Files changed

| File | Change |
|------|--------|
| `Plugin.cs` | Version bump to 2.0.0 |
| `Boot/StartRaid.cs` | Calls `Apply()` for manual Harmony patches after raid start |
| `Utils/LoopTime.cs` | Discovers `GClass2813` equivalent at runtime instead of by name |
| `Utils/DropSurgery.cs` | Discovers `Class1172_0` property and draw-op type at runtime |
| `Utils/ReflectionUtils.cs` | Added ⚠️ VERIFY comments for all obfuscated field names |
| `Patch/PatchEndHealingCycle.cs` | Discovers `Class1172` by signature, uses manual `harmony.Patch()` |
| `Patch/PatchMethod8Transpiler.cs` | Same — manual patch instead of `[HarmonyPatch]` attribute |
| `Patch/PatchMedController.cs` | WorkStateTime now looked up on `__instance` type directly |

### Things that still need manual verification

Open `Assembly-CSharp.dll` from your SPT 4.0.13 install in **dnSpy** or **ILSpy**
and confirm the following are still correct:

#### Obfuscated field names (likely stable but may change)

| Location | Name | What to look for |
|----------|------|-----------------|
| `ActiveHealthController+MedEffect` | `float_12` | Private float field storing work time in the MedEffect nested class |
| `ActiveHealthController+MedEffect` | `activeHealthController_0` | Field of type `ActiveHealthController` on MedEffect |
| `ActiveHealthController+MedEffect` | `MedItem` | Property returning the healing item |
| `ActiveHealthController` | `Player` | Field of type `Player` |
| `Player+MedsController+<ClassXXXX>` | `medsController_0` | Field of type `MedsController` on the operation class |
| `Player+MedsController+<ClassXXXX>` | `firearmsAnimator_0` | Field of type `FirearmsAnimator` used in method_8's IL guard |
| `Player+MedsController` | `_player` | Private `Player` field |

#### Human-readable names (these usually survive)

- `ActiveHealthController+MedEffect` — the "MedEffect" nested type name
- `ActiveHealthController+MedEffect.Added()` — lifecycle method
- `ActiveHealthController+MedEffect.Started()` — lifecycle method
- `ActiveHealthController+MedEffect.Residue()` — lifecycle method
- `ActiveHealthController+<GClass2813 equivalent>.WorkStateTime` — property

#### Draw operation type name

In `DropSurgery.cs`, `IsWeaponDrawOperation()` first tries the 3.11 name
`EFT.Player+FirearmController+GClass1824`, then falls back to finding any
`FirearmController` nested type with a `FastForward` method. If you get
double-speed on the wrong animation, narrow the fallback by adding the
exact new GClass name from dnSpy.

### Build requirements

- Target .NET Framework 4.7.2 (same as before)
- Reference EFT's patched `Assembly-CSharp.dll` from your 4.0.13 install
- Reference `BepInEx.dll`, `0Harmony.dll` from BepInEx

### Known limitations

- `LoopTime.Initialize()` is called on raid start. If the singleton pattern
  for the MedEffect factory changed in 0.16 (i.e. it's no longer a static
  singleton property), LoopTime will fail gracefully and log errors but the
  rest of the mod will still work for animation speed adjustments.
