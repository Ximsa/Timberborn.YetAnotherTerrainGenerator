using System;
using HarmonyLib;
using Timberborn.ModManagerScene;
using UnityEngine;

namespace Timberborn.TerrainGenerator
{
    public class ModStarter : IModStarter
    {
        public void StartMod(IModEnvironment modEnvironment)
        {
            Debug.Log("Hello TerrainGenerator!");
            new Harmony("Timberborn.TerrainGenerator").PatchAll();
        }
    }
}