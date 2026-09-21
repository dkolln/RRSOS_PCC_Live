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
