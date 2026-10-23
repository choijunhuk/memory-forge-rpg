using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

static class JsonGraphComparison
{
    public static string? Difference(object? expected, object? actual, string path, int depth)
    {
        if (depth > 256) return path + " too deep";
        if (expected == null || actual == null) return expected == actual ? null : path + " null/absence mismatch";
        if (expected is Dictionary<string, object?> obj)
        {
            if (actual is not Dictionary<string, object?> got) return path + " object type mismatch";
            if (obj.Count != got.Count) return path + " key count " + got.Count + " expected " + obj.Count;
            foreach (var pair in obj)
            {
                if (!got.TryGetValue(pair.Key, out var value)) return path + " missing key " + pair.Key;
                string? diff = Difference(pair.Value, value, path + "." + pair.Key, depth + 1); if (diff != null) return diff;
            }
            return null;
        }
        if (expected is IList array)
        {
            if (actual is not IList got || array.Count != got.Count) return path + " array length/type mismatch";
            for (int i = 0; i < array.Count; i++) { string? diff = Difference(array[i], got[i], path + "[" + i + "]", depth + 1); if (diff != null) return diff; }
            return null;
        }
        if (expected is long number && (actual is int || actual is long || actual is uint))
            return number == Convert.ToInt64(actual, CultureInfo.InvariantCulture) ? null : path + " number " + actual + " expected " + number;
        return expected.Equals(actual) ? null : path + " value " + actual + " expected " + expected;
    }
}
