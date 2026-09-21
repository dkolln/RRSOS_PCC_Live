using System;
using System.Globalization;
using System.Text;

namespace RRSOS.PCC.Live
{
    /// <summary>
    /// A tiny JSON writer. Unity's own serializers need extra game assemblies and only handle attributed
    /// classes; the live file is small and flat, so plain text building is simpler and has no dependencies.
    /// </summary>
    internal sealed class Json
    {
        private readonly StringBuilder _sb = new StringBuilder(512);
        private bool _needComma;

        public Json Begin()
        {
            Separator();
            _sb.Append('{');
            _needComma = false;
            return this;
        }

        public Json Begin(string name)
        {
            Key(name);
            _sb.Append('{');
            _needComma = false;
            return this;
        }

        public Json End()
        {
            _sb.Append('}');
            _needComma = true;
            return this;
        }

        public Json Str(string name, string value)
        {
            Key(name);
            if (value == null) _sb.Append("null"); else Quote(value);
            _needComma = true;
            return this;
        }

        public Json Bool(string name, bool value)
        {
            Key(name);
            _sb.Append(value ? "true" : "false");
            _needComma = true;
            return this;
        }

        public Json Int(string name, int value)
        {
            Key(name);
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            _needComma = true;
            return this;
        }

        /// <summary>A number. NaN and infinity are not valid JSON, so they are written as null.</summary>
        public Json Num(string name, double value)
        {
            Key(name);
            if (double.IsNaN(value) || double.IsInfinity(value))
                _sb.Append("null");
            else
                _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
            _needComma = true;
            return this;
        }

        public Json BeginArray(string name)
        {
            Key(name);
            _sb.Append('[');
            _needComma = false;
            return this;
        }

        public Json EndArray()
        {
            _sb.Append(']');
            _needComma = true;
            return this;
        }

        /// <summary>
        /// Inserts JSON that was built elsewhere (a whole section). Sections are built separately so that
        /// one failing to read can be replaced by null without breaking the rest of the file.
        /// </summary>
        public Json Raw(string name, string jsonFragment)
        {
            Key(name);
            _sb.Append(string.IsNullOrEmpty(jsonFragment) ? "null" : jsonFragment);
            _needComma = true;
            return this;
        }

        /// <summary>A list of whole numbers, as {"name":[1,2,3]}. A null list is written as null.</summary>
        public Json IntList(string name, System.Collections.Generic.IEnumerable<int> values)
        {
            Key(name);

            if (values == null)
            {
                _sb.Append("null");
            }
            else
            {
                _sb.Append('[');
                var first = true;
                foreach (var value in values)
                {
                    if (!first) _sb.Append(',');
                    _sb.Append(value.ToString(CultureInfo.InvariantCulture));
                    first = false;
                }
                _sb.Append(']');
            }

            _needComma = true;
            return this;
        }

        /// <summary>A world position as {"x":..,"y":..,"z":..}, rounded to two decimals (a hundredth of a metre is plenty and keeps the file small).</summary>
        public Json Point(string name, float x, float y, float z)
        {
            return Begin(name).Num("x", Math.Round(x, 2)).Num("y", Math.Round(y, 2)).Num("z", Math.Round(z, 2)).End();
        }

        /// <summary>A number that may be missing: written as null when it is.</summary>
        public Json OptNum(string name, float? value)
        {
            return value.HasValue ? Num(name, value.Value) : Null(name);
        }

        public Json Null(string name)
        {
            Key(name);
            _sb.Append("null");
            _needComma = true;
            return this;
        }

        public override string ToString() => _sb.ToString();

        private void Key(string name)
        {
            Separator();
            Quote(name);
            _sb.Append(':');
            _needComma = false;
        }

        private void Separator()
        {
            if (_needComma) _sb.Append(',');
        }

        private void Quote(string s)
        {
            _sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < ' ') _sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }
    }
}
