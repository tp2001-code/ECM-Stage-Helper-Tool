using System;

public static class CsvParser
{
    // Beispielsignatur — anpassen an benötigte Typen
    private static bool TryParseScalar(string token, out object value)
    {
        value = null;
        if (int.TryParse(token, out var i)) { value = i; return true; }
        if (double.TryParse(token, out var d)) { value = d; return true; }
        if (!string.IsNullOrEmpty(token)) { value = token; return true; }
        return false;
    }

    public static object ParseCell(string token)
    {
        if (TryParseScalar(token, out var v))
            return v;
        return null;
    }
}