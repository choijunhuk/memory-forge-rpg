// §16.2: shared snapshot serialization; this does not execute Battle/Run commands.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MemoryForge.Core
{
    public static class CanonicalState
    {
        public const long MaxSafeInteger = 9007199254740991;

        /// <summary>
        /// JSON graph of string-keyed object dictionaries, lists, strings, booleans, null,
        /// and CLR integer primitives in the JS safe range. Floating point, decimal,
        /// arbitrary objects, undefined and cyclic containers are rejected.
        /// </summary>
        public static string Serialize(object? value)
        {
            var text = new StringBuilder();
            Append(text, value, new List<object>());
            return text.ToString();
        }

        public static string Hash(object? value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(Serialize(value)));
                var hex = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        private static void Append(StringBuilder text, object? value, List<object> ancestors)
        {
            if (value == null) { text.Append("null"); return; }
            if (value is string str) { AppendString(text, str); return; }
            if (value is bool boolean) { text.Append(boolean ? "true" : "false"); return; }
            switch (value)
            {
                case sbyte n: AppendInteger(text, n); return;
                case byte n: AppendInteger(text, n); return;
                case short n: AppendInteger(text, n); return;
                case ushort n: AppendInteger(text, n); return;
                case int n: AppendInteger(text, n); return;
                case uint n: AppendInteger(text, n); return;
                case long n: AppendInteger(text, n); return;
                case ulong n when n <= (ulong)MaxSafeInteger: AppendInteger(text, (long)n); return;
                case ulong _: throw new ArgumentOutOfRangeException(nameof(value), "Canonical state numbers must be JS safe integers.");
                case float _:
                case double _:
                case decimal _: throw new ArgumentException("Canonical state requires integer primitives; floating point and decimal are unsupported.", nameof(value));
            }

            foreach (object ancestor in ancestors)
                if (ReferenceEquals(value, ancestor)) throw new ArgumentException("Canonical state cannot contain a cyclic container.", nameof(value));
            ancestors.Add(value);
            try
            {
                if (value is IReadOnlyDictionary<string, object?> readOnly)
                    AppendObject(text, readOnly.Keys, key => readOnly[key], ancestors);
                else if (value is IDictionary<string, object?> dictionary)
                    AppendObject(text, dictionary.Keys, key => dictionary[key], ancestors);
                else if (value is Array array && array.Rank != 1)
                    throw new ArgumentException("Canonical state requires one-dimensional arrays.", nameof(value));
                else if (value is IList list)
                    AppendArray(text, list, ancestors);
                else if (value is IReadOnlyList<object?> readOnlyList)
                    AppendArray(text, readOnlyList, ancestors);
                else
                    throw new ArgumentException("Unsupported canonical state type: " + value.GetType().FullName, nameof(value));
            }
            finally { ancestors.RemoveAt(ancestors.Count - 1); }
        }

        private static void AppendInteger(StringBuilder text, long value)
        {
            if (value < -MaxSafeInteger || value > MaxSafeInteger)
                throw new ArgumentOutOfRangeException(nameof(value), "Canonical state numbers must be JS safe integers.");
            text.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendObject(StringBuilder text, IEnumerable<string> sourceKeys, Func<string, object?> get, List<object> ancestors)
        {
            var keys = new List<string>(sourceKeys);
            keys.Sort(StringComparer.Ordinal);
            text.Append('{');
            bool first = true;
            foreach (string key in keys)
            {
                if (key == null) throw new ArgumentException("Canonical object keys must be strings.");
                if (key == "log" || key == "ev") continue;
                if (!first) text.Append(',');
                first = false;
                AppendString(text, key); text.Append(':');
                Append(text, get(key), ancestors);
            }
            text.Append('}');
        }

        private static void AppendArray(StringBuilder text, IEnumerable values, List<object> ancestors)
        {
            text.Append('[');
            bool first = true;
            foreach (object? value in values)
            {
                if (!first) text.Append(',');
                first = false;
                Append(text, value, ancestors);
            }
            text.Append(']');
        }

        private static void AppendString(StringBuilder text, string value)
        {
            text.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                        {
                            text.Append(c); text.Append(value[++i]);
                        }
                        else if (c < ' ' || char.IsSurrogate(c))
                            text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else text.Append(c);
                        break;
                }
            }
            text.Append('"');
        }
    }
}
