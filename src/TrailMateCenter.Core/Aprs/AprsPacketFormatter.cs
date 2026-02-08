using System.Globalization;
using System.Text;

namespace TrailMateCenter.Aprs;

public static class AprsPacketFormatter
{
    public static string BuildPacket(string source, string destination, IReadOnlyList<string> path, string info)
    {
        var sb = new StringBuilder();
        sb.Append(source);
        sb.Append('>');
        sb.Append(destination);
        if (path.Count > 0)
        {
            sb.Append(',');
            sb.Append(string.Join(',', path));
        }
        sb.Append(':');
        sb.Append(info);
        return sb.ToString();
    }

    public static string FormatLatLonUncompressed(double lat, double lon, char symbolTable)
    {
        var latAbs = Math.Abs(lat);
        var lonAbs = Math.Abs(lon);
        var latDeg = (int)Math.Floor(latAbs);
        var lonDeg = (int)Math.Floor(lonAbs);
        var latMin = (latAbs - latDeg) * 60.0;
        var lonMin = (lonAbs - lonDeg) * 60.0;
        var latHemi = lat >= 0 ? 'N' : 'S';
        var lonHemi = lon >= 0 ? 'E' : 'W';
        return string.Format(CultureInfo.InvariantCulture, "{0:00}{1:00.00}{2}{3}{4:000}{5:00.00}{6}",
            latDeg, latMin, latHemi, symbolTable, lonDeg, lonMin, lonHemi);
    }

    public static string FormatLatLonCompressed(double lat, double lon, char symbolTable, char symbolCode, out char symbolTableOut, out char symbolCodeOut)
    {
        symbolTableOut = symbolTable;
        symbolCodeOut = symbolCode;
        var latScaled = (int)Math.Round((90.0 - lat) * 380926.0);
        var lonScaled = (int)Math.Round((180.0 + lon) * 190463.0);
        var latChars = Base91Encode(latScaled, 4);
        var lonChars = Base91Encode(lonScaled, 4);
        return $"{latChars}{lonChars}{symbolCodeOut}";
    }

    public static string BuildPositionInfo(
        double lat,
        double lon,
        char symbolTable,
        char symbolCode,
        bool compressed,
        DateTimeOffset? timestamp,
        double? courseDeg,
        double? speedKts,
        double? altitudeMeters,
        string? comment)
    {
        var hasTimestamp = timestamp.HasValue;
        var typeChar = hasTimestamp ? '@' : '!';
        var sb = new StringBuilder();
        sb.Append(typeChar);
        if (hasTimestamp)
        {
            sb.Append(FormatTimestamp(timestamp!.Value));
        }

        if (compressed)
        {
            var compressedBody = FormatLatLonCompressed(lat, lon, symbolTable, symbolCode, out var st, out _);
            sb.Append(st);
            sb.Append(compressedBody);
            sb.Append(BuildCompressedExtension(courseDeg, speedKts, altitudeMeters));
        }
        else
        {
            sb.Append(FormatLatLonUncompressed(lat, lon, symbolTable));
            sb.Append(symbolCode);
            if (courseDeg.HasValue && speedKts.HasValue)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:000}/{1:000}", Math.Round(courseDeg.Value), Math.Round(speedKts.Value));
            }
            if (altitudeMeters.HasValue)
            {
                var feet = altitudeMeters.Value * 3.28084;
                sb.AppendFormat(CultureInfo.InvariantCulture, "/A={0:000000}", Math.Round(feet));
            }
        }

        if (!string.IsNullOrWhiteSpace(comment))
            sb.Append(comment);

        return sb.ToString();
    }

    public static string BuildMessageInfo(string addressee, string text, string? messageId)
    {
        var padded = addressee.PadRight(9).Substring(0, 9);
        var sb = new StringBuilder();
        sb.Append(':');
        sb.Append(padded);
        sb.Append(':');
        sb.Append(text);
        if (!string.IsNullOrWhiteSpace(messageId))
        {
            sb.Append('{');
            sb.Append(messageId);
        }
        return sb.ToString();
    }

    public static string BuildStatusInfo(string text)
    {
        return $">{text}";
    }

    public static string BuildObjectInfo(string name, bool alive, DateTimeOffset timestamp, double lat, double lon, char symbolTable, char symbolCode, string? comment)
    {
        var objName = name.PadRight(9).Substring(0, 9);
        var flag = alive ? '*' : '_';
        var sb = new StringBuilder();
        sb.Append(';');
        sb.Append(objName);
        sb.Append(flag);
        sb.Append(FormatTimestamp(timestamp));
        sb.Append(FormatLatLonUncompressed(lat, lon, symbolTable));
        sb.Append(symbolCode);
        if (!string.IsNullOrWhiteSpace(comment))
            sb.Append(comment);
        return sb.ToString();
    }

    public static string BuildItemInfo(string name, double lat, double lon, char symbolTable, char symbolCode, string? comment)
    {
        var itemName = name.Length > 9 ? name[..9] : name;
        var sb = new StringBuilder();
        sb.Append(')');
        sb.Append(itemName);
        sb.Append('!');
        sb.Append(FormatLatLonUncompressed(lat, lon, symbolTable));
        sb.Append(symbolCode);
        if (!string.IsNullOrWhiteSpace(comment))
            sb.Append(comment);
        return sb.ToString();
    }

    public static string BuildTelemetryInfo(int sequence, int[] analog, bool[] digital)
    {
        var a = analog.Select(v => Math.Clamp(v, 0, 255)).ToArray();
        var d = digital.Select(v => v ? '1' : '0').ToArray();
        return string.Format(CultureInfo.InvariantCulture,
            "T#{0:000},{1:000},{2:000},{3:000},{4:000},{5:000},{6}{7}{8}{9}{10}{11}{12}{13}",
            sequence % 1000,
            a.ElementAtOrDefault(0, 0),
            a.ElementAtOrDefault(1, 0),
            a.ElementAtOrDefault(2, 0),
            a.ElementAtOrDefault(3, 0),
            a.ElementAtOrDefault(4, 0),
            d.ElementAtOrDefault(0, '0'),
            d.ElementAtOrDefault(1, '0'),
            d.ElementAtOrDefault(2, '0'),
            d.ElementAtOrDefault(3, '0'),
            d.ElementAtOrDefault(4, '0'),
            d.ElementAtOrDefault(5, '0'),
            d.ElementAtOrDefault(6, '0'),
            d.ElementAtOrDefault(7, '0'));
    }

    public static string BuildParmLine(string[] names) => $"PARM.{string.Join(',', names.Select(n => n.Replace(',', ' ')))}";
    public static string BuildUnitLine(string[] units) => $"UNIT.{string.Join(',', units.Select(n => n.Replace(',', ' ')))}";
    public static string BuildEqnsLine(double[] coeffs) => $"EQNS.{string.Join(',', coeffs.Select(c => c.ToString("0.###", CultureInfo.InvariantCulture)))}";
    public static string BuildBitsLine(string bits, string[] labels)
    {
        var safeBits = bits.Length >= 8 ? bits[..8] : bits.PadRight(8, '0');
        var safeLabels = labels.Length == 8 ? labels : Enumerable.Range(1, 8).Select(i => $"D{i}").ToArray();
        return $"BITS.{safeBits},{string.Join(',', safeLabels)}";
    }

    public static string BuildWeatherInfo(
        double? windDirDeg,
        double? windSpeedMps,
        double? windGustMps,
        double? temperatureC,
        double? rain1hMm,
        double? rain24hMm,
        double? rainSinceMidnightMm,
        double? humidityPct,
        double? pressureHpa,
        string? comment)
    {
        var sb = new StringBuilder();
        sb.Append('_');
        if (windDirDeg.HasValue)
            sb.AppendFormat(CultureInfo.InvariantCulture, "c{0:000}", Math.Round(windDirDeg.Value));
        if (windSpeedMps.HasValue)
            sb.AppendFormat(CultureInfo.InvariantCulture, "s{0:000}", Math.Round(MpsToMph(windSpeedMps.Value)));
        if (windGustMps.HasValue)
            sb.AppendFormat(CultureInfo.InvariantCulture, "g{0:000}", Math.Round(MpsToMph(windGustMps.Value)));
        if (temperatureC.HasValue)
            sb.AppendFormat(CultureInfo.InvariantCulture, "t{0:000}", Math.Round(CToF(temperatureC.Value)));
        if (rain1hMm.HasValue)
            sb.AppendFormat(CultureInfo.InvariantCulture, "r{0:000}", Math.Round(MmToHundredthsIn(rain1hMm.Value)));
        if (rain24hMm.HasValue)
            sb.AppendFormat(CultureInfo.InvariantCulture, "p{0:000}", Math.Round(MmToHundredthsIn(rain24hMm.Value)));
        if (rainSinceMidnightMm.HasValue)
            sb.AppendFormat(CultureInfo.InvariantCulture, "P{0:000}", Math.Round(MmToHundredthsIn(rainSinceMidnightMm.Value)));
        if (humidityPct.HasValue)
            sb.AppendFormat(CultureInfo.InvariantCulture, "h{0:00}", Math.Round(humidityPct.Value));
        if (pressureHpa.HasValue)
            sb.AppendFormat(CultureInfo.InvariantCulture, "b{0:00000}", Math.Round(pressureHpa.Value * 10.0));
        if (!string.IsNullOrWhiteSpace(comment))
            sb.Append(comment);
        return sb.ToString();
    }

    private static string FormatTimestamp(DateTimeOffset ts)
    {
        var utc = ts.ToUniversalTime();
        return utc.ToString("ddHHmm", CultureInfo.InvariantCulture) + "z";
    }

    private static string BuildCompressedExtension(double? courseDeg, double? speedKts, double? altitudeMeters)
    {
        if (altitudeMeters.HasValue)
        {
            var feet = altitudeMeters.Value * 3.28084;
            var alt = (int)Math.Round(feet);
            var code = (int)Math.Round(Math.Log(alt + 1) / Math.Log(1.002));
            return Base91Encode(code, 2);
        }
        if (courseDeg.HasValue && speedKts.HasValue)
        {
            var course = (int)Math.Round(courseDeg.Value / 4.0);
            var speed = (int)Math.Round(Math.Log(speedKts.Value + 1) / Math.Log(1.08));
            return $"{Base91Encode(course, 1)}{Base91Encode(speed, 1)}";
        }
        return "  ";
    }

    private static string Base91Encode(int value, int length)
    {
        var chars = new char[length];
        for (var i = length - 1; i >= 0; i--)
        {
            chars[i] = (char)(value % 91 + 33);
            value /= 91;
        }
        return new string(chars);
    }

    private static double MpsToMph(double value) => value * 2.2369362920544;
    private static double CToF(double c) => c * 9.0 / 5.0 + 32.0;
    private static double MmToHundredthsIn(double mm) => mm / 25.4 * 100.0;
}

internal static class AprsEnumerableExtensions
{
    public static T ElementAtOrDefault<T>(this IReadOnlyList<T> list, int index, T fallback)
    {
        if (index < 0 || index >= list.Count)
            return fallback;
        return list[index];
    }
}
