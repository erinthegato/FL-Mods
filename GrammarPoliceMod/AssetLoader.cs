using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GrammarPoliceMod;

public static class AssetLoader
{
    private static readonly Dictionary<string, Texture2D> _textures = new();
    private static readonly string AssetRoot = Path.Combine(
        Path.GetDirectoryName(typeof(GrammarPoliceMod).Assembly.Location) ?? ".",
        "GrammarPolice", "Assets");

    static AssetLoader()
    {
        if (!Directory.Exists(AssetRoot))
            Directory.CreateDirectory(AssetRoot);
    }

    public static Texture2D? LoadTexture(string filename, bool persistent = true)
    {
        if (_textures.TryGetValue(filename, out var tex))
            return tex;

        string path = Path.Combine(AssetRoot, filename);
        if (!File.Exists(path)) return null;

        byte[] bytes = File.ReadAllBytes(path);
        tex = new Texture2D(2, 2);
        if (!tex.LoadImage(bytes))
        {
            MelonLoader.MelonLogger.Warning($"Failed to load texture: {filename}");
            return null;
        }
        if (persistent)
            _textures[filename] = tex;
        return tex;
    }

    public static string? ReadTextFile(string filename)
    {
        string path = Path.Combine(AssetRoot, filename);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}