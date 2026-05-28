namespace FLMods.Shared;

public sealed record MapZone(string Name, string Street, string ZipCode, float MinX, float MaxX, float MinZ, float MaxZ);

public static class FlashingLightsMap
{
    public const float WorldMinX = -3000f;
    public const float WorldMaxX = 3000f;
    public const float WorldMinZ = -3000f;
    public const float WorldMaxZ = 3000f;

    private static readonly MapZone[] Zones =
    {
        new("City Center", "city",           "01-01", -1000f, 1000f,  -500f,  800f),
        new("City Marina",  "city marina",    "01-02", -1200f, -400f,  -1200f, -400f),
        new("Suburbs West", "suburbs west",   "02-01", -2800f, -1000f, -1000f, 1000f),
        new("Suburbs East", "suburbs east",   "03-01", 1000f,  2800f,  -800f,  1000f),
        new("Cod Town",     "cod town",       "04-01", -2200f, -800f,  1500f,  2800f),
        new("Route 20",     "route 20",       "05-01", 500f,   2000f,  1500f,  2800f),
        new("Route 600",    "route 600",      "05-02", -2800f, -1200f, -2500f, -1400f),
        new("Interstate 69","interstate 69",  "06-01", 200f,   1000f,  800f,   1800f),
        new("Beach Town",   "beach town",     "07-01", 1500f,  2800f,  -2800f, -1200f),
        new("Port",         "port",           "08-01", 1500f,  2800f,  1000f,  2500f),
    };

    public static string ResolveStreet(float x, float z)
    {
        for (int i = 0; i < Zones.Length; i++)
        {
            var zz = Zones[i];
            if (x >= zz.MinX && x <= zz.MaxX && z >= zz.MinZ && z <= zz.MaxZ)
                return zz.Street;
        }
        return "unknown";
    }

    public static string ResolveZipCode(float x, float z)
    {
        for (int i = 0; i < Zones.Length; i++)
        {
            var zz = Zones[i];
            if (x >= zz.MinX && x <= zz.MaxX && z >= zz.MinZ && z <= zz.MaxZ)
                return zz.ZipCode;
        }
        return "00-00";
    }

    public static string BuildLocationLabel(float x, float z)
    {
        string street = ResolveStreet(x, z);
        string zip = ResolveZipCode(x, z);
        return $"{zip} {street}";
    }

    public static bool TryParseGps(string location, out float x, out float z)
    {
        x = 0; z = 0;
        if (string.IsNullOrEmpty(location)) return false;
        if (!location.StartsWith("GPS ", StringComparison.OrdinalIgnoreCase)) return false;
        string coord = location[4..];
        int comma = coord.IndexOf(',');
        if (comma < 0) return false;
        return float.TryParse(coord.AsSpan(0, comma), out x) &&
               float.TryParse(coord.AsSpan(comma + 1), out z);
    }

    public static void GameToMapUV(float gameX, float gameZ, float mapWidth, float mapHeight, out float uvX, out float uvY)
    {
        float nx = (gameX - WorldMinX) / (WorldMaxX - WorldMinX);
        float nz = (gameZ - WorldMinZ) / (WorldMaxZ - WorldMinZ);
        uvX = nx * mapWidth;
        uvY = (1f - nz) * mapHeight;
    }
}
