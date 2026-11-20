using System.Globalization;
using WinTracer.Localization;

namespace WinTracer.Core;

public static class ByteSize
{
    private static string[] Units => [L.T("Unit.B"), L.T("Unit.KB"), L.T("Unit.MB"), L.T("Unit.GB"), L.T("Unit.TB"), L.T("Unit.PB")];

    public static string Format(long bytes)
    {
        if (bytes < 0) return "—";
        if (bytes < 1024) return $"{bytes} {Units[0]}";

        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        string format = value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00";
        return value.ToString(format, CultureInfo.CurrentCulture) + " " + Units[unit];
    }
}
