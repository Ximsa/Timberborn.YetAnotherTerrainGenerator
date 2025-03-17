using System;
using HarmonyLib;
using Timberborn.ModManagerScene;
using UnityEngine;

namespace Timberborn.TerrainGenerator;

public class ModStarter : IModStarter
{
    public static string OriginPath = string.Empty;
    public static string ModPath = string.Empty;

    public void StartMod(IModEnvironment modEnvironment)
    {
        ModPath = modEnvironment.ModPath;
        OriginPath = modEnvironment.OriginPath;
        Debug.Log($"Hello TerrainGenerator! Paths: {ModPath} {OriginPath}");
        new Harmony("Timberborn.TerrainGenerator").PatchAll();
    }
}