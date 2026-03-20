namespace Tysl.Ai.Core.Map;

public static class CoordinateTypeCatalog
{
    public static bool HasCompleteCoordinate(double? longitude, double? latitude)
    {
        return longitude.HasValue && latitude.HasValue;
    }

    public static bool HasPartialCoordinate(double? longitude, double? latitude)
    {
        return longitude.HasValue ^ latitude.HasValue;
    }

    public static bool IsRecognized(string? coordinateType)
    {
        return IsDirectDisplayType(coordinateType) || RequiresMapHostConversion(coordinateType);
    }

    public static bool IsDirectDisplayType(string? coordinateType)
    {
        return Normalize(coordinateType) switch
        {
            "" => true,
            "gcj02" => true,
            "amap" => true,
            "gaode" => true,
            "unknown" => true,
            _ => false
        };
    }

    public static bool RequiresMapHostConversion(string? coordinateType)
    {
        return Normalize(coordinateType) switch
        {
            "bd09" => true,
            "baidu" => true,
            "wgs84" => true,
            "gps" => true,
            "mapbar" => true,
            _ => false
        };
    }

    public static string Normalize(string? coordinateType)
    {
        return string.IsNullOrWhiteSpace(coordinateType)
            ? string.Empty
            : coordinateType.Trim().ToLowerInvariant();
    }

    public static string GetDisplayLabel(string? coordinateType)
    {
        return Normalize(coordinateType) switch
        {
            "" => "gcj02",
            "amap" => "gcj02",
            "gaode" => "gcj02",
            "gcj02" => "gcj02",
            "bd09" => "bd09",
            "baidu" => "bd09",
            "wgs84" => "wgs84",
            "gps" => "wgs84/gps",
            "mapbar" => "mapbar",
            "unknown" => "未知",
            _ => "未知"
        };
    }
}
