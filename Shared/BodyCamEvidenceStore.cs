using System.Reflection;
using System.Text.Json;

namespace FLMods.Shared;

public sealed record BodyCamBookmark(
    DateTime Timestamp,
    string UnitId,
    string CameraId,
    string CameraMode,
    string EventType,
    string Note,
    string Location,
    float GpsX = 0f,
    float GpsZ = 0f
);

public sealed record DriverLicenseScan(
    DateTime Timestamp,
    string RawText,
    string FirstName,
    string LastName,
    string LicensePlate,
    string LicenseStatus,
    bool WeaponLicense
);

public static class BodyCamEvidenceStore
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string EvidenceDirectory
    {
        get
        {
            string root = FindGameRoot();
            return Path.Combine(root, "UserData", "FLMods", "BodyCam");
        }
    }

    public static string BookmarkPath => Path.Combine(EvidenceDirectory, "bookmarks.json");
    public static string LicenseScanPath => Path.Combine(EvidenceDirectory, "latest_license_scan.json");
    public static string LicenseScanInboxPath => Path.Combine(EvidenceDirectory, "license_scan.txt");

    public static void AddBookmark(BodyCamBookmark bookmark)
    {
        lock (Sync)
        {
            var bookmarks = LoadBookmarksInternal();
            bookmarks.Add(bookmark);
            SaveBookmarksInternal(bookmarks);
        }
    }

    public static IReadOnlyList<BodyCamBookmark> LoadBookmarks()
    {
        lock (Sync)
        {
            return LoadBookmarksInternal();
        }
    }

    public static void SaveLicenseScan(DriverLicenseScan scan)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(EvidenceDirectory);
            File.WriteAllText(LicenseScanPath, JsonSerializer.Serialize(scan, JsonOptions));
        }
    }

    public static DriverLicenseScan? LoadLatestLicenseScan()
    {
        lock (Sync)
        {
            try
            {
                if (!File.Exists(LicenseScanPath)) return null;
                return JsonSerializer.Deserialize<DriverLicenseScan>(File.ReadAllText(LicenseScanPath));
            }
            catch
            {
                return null;
            }
        }
    }

    private static List<BodyCamBookmark> LoadBookmarksInternal()
    {
        try
        {
            if (!File.Exists(BookmarkPath))
                return new List<BodyCamBookmark>();

            var json = File.ReadAllText(BookmarkPath);
            return JsonSerializer.Deserialize<List<BodyCamBookmark>>(json) ?? new List<BodyCamBookmark>();
        }
        catch
        {
            return new List<BodyCamBookmark>();
        }
    }

    private static void SaveBookmarksInternal(List<BodyCamBookmark> bookmarks)
    {
        Directory.CreateDirectory(EvidenceDirectory);
        File.WriteAllText(BookmarkPath, JsonSerializer.Serialize(bookmarks, JsonOptions));
    }

    private static string FindGameRoot()
    {
        try
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".");
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "flashinglights.exe")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch { }

        return Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            ".."));
    }
}
