using Meshtastic.Protobufs;
using System.Linq;

namespace TrailMateCenter.Aprs;

public sealed record AprsTelemetryDefinition(
    string[] Parm,
    string[] Unit,
    double[] Eqns,
    string Bits,
    string[] BitLabels);

public sealed record AprsTelemetrySample(
    int Sequence,
    int[] Analog,
    bool[] Digital,
    AprsTelemetryDefinition Definition);

public static class AprsTelemetryMapper
{
    public static AprsTelemetrySample? Map(Telemetry telemetry, int sequence)
    {
        return telemetry.VariantCase switch
        {
            Telemetry.VariantOneofCase.DeviceMetrics => MapDevice(telemetry.DeviceMetrics, sequence),
            Telemetry.VariantOneofCase.EnvironmentMetrics => MapEnvironment(telemetry.EnvironmentMetrics, sequence),
            Telemetry.VariantOneofCase.AirQualityMetrics => MapAir(telemetry.AirQualityMetrics, sequence),
            Telemetry.VariantOneofCase.PowerMetrics => MapPower(telemetry.PowerMetrics, sequence),
            Telemetry.VariantOneofCase.LocalStats => MapLocal(telemetry.LocalStats, sequence),
            Telemetry.VariantOneofCase.HealthMetrics => MapHealth(telemetry.HealthMetrics, sequence),
            Telemetry.VariantOneofCase.HostMetrics => MapHost(telemetry.HostMetrics, sequence),
            _ => null,
        };
    }

    private static AprsTelemetrySample MapDevice(DeviceMetrics m, int seq)
    {
        var a = new double?[] { m.HasBatteryLevel ? m.BatteryLevel : null, m.HasVoltage ? m.Voltage : null, m.HasChannelUtilization ? m.ChannelUtilization : null, m.HasAirUtilTx ? m.AirUtilTx : null, m.HasUptimeSeconds ? m.UptimeSeconds : null };
        var parm = new[] { "Battery", "Voltage", "ChanUtil", "AirTx", "Uptime" };
        var unit = new[] { "%", "V", "%", "%", "s" };
        return BuildSample(a, parm, unit, seq);
    }

    private static AprsTelemetrySample MapEnvironment(EnvironmentMetrics m, int seq)
    {
        var a = new double?[] { m.HasTemperature ? m.Temperature : null, m.HasRelativeHumidity ? m.RelativeHumidity : null, m.HasBarometricPressure ? m.BarometricPressure : null, m.HasWindSpeed ? m.WindSpeed : null, m.HasWindDirection ? m.WindDirection : null };
        var parm = new[] { "Temp", "Humidity", "Pressure", "WindSpd", "WindDir" };
        var unit = new[] { "C", "%", "hPa", "m/s", "deg" };
        return BuildSample(a, parm, unit, seq);
    }

    private static AprsTelemetrySample MapAir(AirQualityMetrics m, int seq)
    {
        var a = new double?[] { m.HasPm25Standard ? m.Pm25Standard : null, m.HasPm10Standard ? m.Pm10Standard : null, m.HasCo2 ? m.Co2 : null, m.HasPmTemperature ? m.PmTemperature : null, m.HasPmHumidity ? m.PmHumidity : null };
        var parm = new[] { "PM2.5", "PM10", "CO2", "Temp", "Humidity" };
        var unit = new[] { "ug/m3", "ug/m3", "ppm", "C", "%" };
        return BuildSample(a, parm, unit, seq);
    }

    private static AprsTelemetrySample MapPower(PowerMetrics m, int seq)
    {
        var a = new double?[] { m.HasCh1Voltage ? m.Ch1Voltage : null, m.HasCh1Current ? m.Ch1Current : null, m.HasCh2Voltage ? m.Ch2Voltage : null, m.HasCh2Current ? m.Ch2Current : null, null };
        var parm = new[] { "CH1V", "CH1A", "CH2V", "CH2A", "N/A" };
        var unit = new[] { "V", "A", "V", "A", "" };
        return BuildSample(a, parm, unit, seq);
    }

    private static AprsTelemetrySample MapLocal(LocalStats m, int seq)
    {
        var a = new double?[] { m.ChannelUtilization, m.AirUtilTx, m.NumPacketsTx, m.NumPacketsRx, m.NoiseFloor };
        var parm = new[] { "ChanUtil", "AirTx", "PktTx", "PktRx", "Noise" };
        var unit = new[] { "%", "%", "cnt", "cnt", "dBm" };
        return BuildSample(a, parm, unit, seq);
    }

    private static AprsTelemetrySample MapHealth(HealthMetrics m, int seq)
    {
        var a = new double?[] { m.HasHeartBpm ? m.HeartBpm : null, m.HasSpO2 ? m.SpO2 : null, m.HasTemperature ? m.Temperature : null, null, null };
        var parm = new[] { "Heart", "SpO2", "Temp", "N/A", "N/A" };
        var unit = new[] { "bpm", "%", "C", "", "" };
        return BuildSample(a, parm, unit, seq);
    }

    private static AprsTelemetrySample MapHost(HostMetrics m, int seq)
    {
        var a = new double?[] { m.UptimeSeconds, m.FreememBytes, m.Diskfree1Bytes, m.Load1, null };
        var parm = new[] { "Uptime", "FreeMem", "DiskFree", "Load1", "N/A" };
        var unit = new[] { "s", "B", "B", "x100", "" };
        return BuildSample(a, parm, unit, seq);
    }

    private static AprsTelemetrySample BuildSample(double?[] values, string[] parm, string[] unit, int seq)
    {
        var analog = new int[5];
        var eqns = new double[15];
        for (var i = 0; i < 5; i++)
        {
            var v = values[i] ?? 0;
            var (raw, a, b, c) = ScaleToRaw(v);
            analog[i] = raw;
            eqns[i * 3 + 0] = a;
            eqns[i * 3 + 1] = b;
            eqns[i * 3 + 2] = c;
        }
        var bits = "00000000";
        var labels = Enumerable.Range(1, 8).Select(i => $"D{i}").ToArray();
        return new AprsTelemetrySample(seq, analog, new bool[8], new AprsTelemetryDefinition(parm, unit, eqns, bits, labels));
    }

    private static (int raw, double a, double b, double c) ScaleToRaw(double value)
    {
        var raw = (int)Math.Round(Math.Clamp(value, 0, 255));
        // linear mapping: actual ~= raw
        return (raw, 0, 1, 0);
    }
}
