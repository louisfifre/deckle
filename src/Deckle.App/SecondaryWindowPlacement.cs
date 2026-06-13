using System.Runtime.InteropServices;
using System.Text.Json;
using Deckle.Core;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Deckle.App;

internal static class SecondaryWindowPlacement
{
    public const string Settings = "settings";
    public const string Log = "log";
    public const string Playground = "playground";

    private const int SW_SHOWNORMAL = 1;
    private const int SW_SHOWMINIMIZED = 2;
    private const int SW_SHOWMAXIMIZED = 3;

    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static Dictionary<string, PlacementDto>? _cache;

    private static string PlacementPath => Path.Combine(AppPaths.UserDataRoot, "window-placement.json");

    public static void Restore(Window window, string key)
    {
        try
        {
            if (!TryRead(key, out var dto)) return;
            if (dto.NormalRight <= dto.NormalLeft || dto.NormalBottom <= dto.NormalTop) return;

            var placement = new WINDOWPLACEMENT
            {
                length = Marshal.SizeOf<WINDOWPLACEMENT>(),
                flags = dto.Flags,
                showCmd = dto.ShowCmd == SW_SHOWMINIMIZED ? SW_SHOWNORMAL : dto.ShowCmd,
                ptMinPosition = new POINT { X = dto.MinX, Y = dto.MinY },
                ptMaxPosition = new POINT { X = dto.MaxX, Y = dto.MaxY },
                rcNormalPosition = new RECT
                {
                    left = dto.NormalLeft,
                    top = dto.NormalTop,
                    right = dto.NormalRight,
                    bottom = dto.NormalBottom,
                },
            };

            if (placement.showCmd is not (SW_SHOWNORMAL or SW_SHOWMAXIMIZED))
            {
                placement.showCmd = SW_SHOWNORMAL;
            }

            SetWindowPlacement(WindowNative.GetWindowHandle(window), ref placement);
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.LogWindowWarning();
            DeckleAppSource.Log.LogWindowWarningDetail(
                $"window placement restore failed | window={key} | error={ex.Message}");
        }
    }

    public static void Save(Window window, string key)
    {
        try
        {
            var placement = new WINDOWPLACEMENT
            {
                length = Marshal.SizeOf<WINDOWPLACEMENT>(),
            };
            if (!GetWindowPlacement(WindowNative.GetWindowHandle(window), ref placement)) return;
            if (placement.showCmd == SW_SHOWMINIMIZED) placement.showCmd = SW_SHOWNORMAL;

            Write(key, PlacementDto.From(placement));
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.LogWindowWarning();
            DeckleAppSource.Log.LogWindowWarningDetail(
                $"window placement save failed | window={key} | error={ex.Message}");
        }
    }

    private static bool TryRead(string key, out PlacementDto dto)
    {
        lock (Gate)
        {
            _cache ??= Load();
            return _cache.TryGetValue(key, out dto!);
        }
    }

    private static void Write(string key, PlacementDto dto)
    {
        lock (Gate)
        {
            _cache ??= Load();
            _cache[key] = dto;

            Directory.CreateDirectory(AppPaths.UserDataRoot);
            var store = new PlacementStore { Windows = _cache };
            File.WriteAllText(PlacementPath, JsonSerializer.Serialize(store, JsonOptions));
        }
    }

    private static Dictionary<string, PlacementDto> Load()
    {
        try
        {
            if (!File.Exists(PlacementPath)) return new Dictionary<string, PlacementDto>(StringComparer.Ordinal);
            var json = File.ReadAllText(PlacementPath);
            var store = JsonSerializer.Deserialize<PlacementStore>(json, JsonOptions);
            return store?.Windows is null
                ? new Dictionary<string, PlacementDto>(StringComparer.Ordinal)
                : new Dictionary<string, PlacementDto>(store.Windows, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.LogWindowWarning();
            DeckleAppSource.Log.LogWindowWarningDetail(
                $"window placement load failed | error={ex.Message}");
            return new Dictionary<string, PlacementDto>(StringComparer.Ordinal);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    private sealed class PlacementStore
    {
        public Dictionary<string, PlacementDto> Windows { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class PlacementDto
    {
        public int Flags { get; set; }
        public int ShowCmd { get; set; }
        public int MinX { get; set; }
        public int MinY { get; set; }
        public int MaxX { get; set; }
        public int MaxY { get; set; }
        public int NormalLeft { get; set; }
        public int NormalTop { get; set; }
        public int NormalRight { get; set; }
        public int NormalBottom { get; set; }

        public static PlacementDto From(WINDOWPLACEMENT placement) => new()
        {
            Flags = placement.flags,
            ShowCmd = placement.showCmd,
            MinX = placement.ptMinPosition.X,
            MinY = placement.ptMinPosition.Y,
            MaxX = placement.ptMaxPosition.X,
            MaxY = placement.ptMaxPosition.Y,
            NormalLeft = placement.rcNormalPosition.left,
            NormalTop = placement.rcNormalPosition.top,
            NormalRight = placement.rcNormalPosition.right,
            NormalBottom = placement.rcNormalPosition.bottom,
        };
    }
}
