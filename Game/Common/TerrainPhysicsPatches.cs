using HarmonyLib;
using Timberborn.TerrainPhysics;

namespace Timberborn.TerrainGenerator.Game.Common;

// The game deletes terrain (and buildings) that is not connected to the ground when a map is loaded.
// Generated terrain may contain overhangs and floating chunks on purpose, so that load-time check is skipped.
// Terrain physics while playing (e.g. collapsing terrain when its support is removed) is not affected.
[HarmonyPatch(typeof(TerrainPhysicsPostLoader), "Validate")]
public static class SkipTerrainPhysicsLoadValidation
{
    public static bool Prefix()
    {
        return false;
    }
}
