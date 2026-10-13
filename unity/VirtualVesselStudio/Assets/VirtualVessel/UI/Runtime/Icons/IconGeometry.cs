using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace VirtualVessel.UI.Icons
{
    /// <summary>One continuous line of an icon, in the 24×24 icon grid.</summary>
    public sealed class IconPolyline
    {
        public IconPolyline(IReadOnlyList<Vector2> points, bool closed)
        {
            Points = points ?? throw new ArgumentNullException(nameof(points));
            Closed = closed;
        }

        public IReadOnlyList<Vector2> Points { get; }

        public bool Closed { get; }
    }

    /// <summary>
    /// Turns SVG path data into polylines, so that icons can be drawn with plain line segments.
    /// Pure: the result depends only on the arguments.
    /// </summary>
    /// <remarks>
    /// Supports the commands used by the project icons: M, L, H, V, C, Q, A, and Z, in absolute and
    /// relative forms. Curves are sampled into short segments; at icon sizes the difference is invisible,
    /// and plain segments avoid the artifacts seen with Painter2D curve commands.
    /// </remarks>
    public static class IconGeometry
    {
        private const int CurveSegments = 12;
        private const float ArcStepDegrees = 15f;

        /// <exception cref="FormatException">The path uses an unsupported command or has missing numbers.</exception>
        public static IReadOnlyList<IconPolyline> ParsePath(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            var lines = new List<IconPolyline>();
            var points = new List<Vector2>();
            Vector2 current = Vector2.zero;
            Vector2 start = Vector2.zero;
            var reader = new Reader(path);
            char command = '\0';

            void Finish(bool closed)
            {
                if (points.Count > 1)
                {
                    lines.Add(new IconPolyline(points.ToArray(), closed));
                }

                points.Clear();
            }

            while (reader.SkipSeparators())
            {
                if (reader.PeekIsCommand())
                {
                    command = reader.ReadCommand();
                }
                else if (command == '\0')
                {
                    throw new FormatException($"Icon path must start with a command: '{path}'.");
                }

                bool relative = char.IsLower(command);
                Vector2 origin = relative ? current : Vector2.zero;
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        Finish(false);
                        current = origin + reader.ReadPoint();
                        start = current;
                        points.Add(current);

                        // Further pairs after a move are lines, as in SVG.
                        command = relative ? 'l' : 'L';
                        break;
                    case 'L':
                        current = origin + reader.ReadPoint();
                        points.Add(current);
                        break;
                    case 'H':
                        current = new Vector2((relative ? current.x : 0) + reader.ReadNumber(), current.y);
                        points.Add(current);
                        break;
                    case 'V':
                        current = new Vector2(current.x, (relative ? current.y : 0) + reader.ReadNumber());
                        points.Add(current);
                        break;
                    case 'C':
                    {
                        Vector2 c1 = origin + reader.ReadPoint();
                        Vector2 c2 = origin + reader.ReadPoint();
                        Vector2 end = origin + reader.ReadPoint();
                        AddCubic(points, current, c1, c2, end);
                        current = end;
                        break;
                    }

                    case 'Q':
                    {
                        Vector2 control = origin + reader.ReadPoint();
                        Vector2 end = origin + reader.ReadPoint();
                        AddCubic(points, current, current + (control - current) * (2f / 3f), end + (control - end) * (2f / 3f), end);
                        current = end;
                        break;
                    }

                    case 'A':
                    {
                        float rx = reader.ReadNumber();
                        float ry = reader.ReadNumber();
                        float rotation = reader.ReadNumber();
                        bool largeArc = reader.ReadFlag();
                        bool sweep = reader.ReadFlag();
                        Vector2 end = origin + reader.ReadPoint();
                        AddArc(points, current, rx, ry, rotation, largeArc, sweep, end);
                        current = end;
                        break;
                    }

                    case 'Z':
                        current = start;
                        Finish(true);
                        points.Add(current);
                        command = '\0';
                        break;
                    default:
                        throw new FormatException($"Unsupported icon path command '{command}' in '{path}'.");
                }
            }

            Finish(false);
            return lines;
        }

        /// <summary>A circle as a closed polyline.</summary>
        public static IconPolyline Circle(Vector2 center, float radius)
        {
            int segments = Mathf.Max(12, Mathf.CeilToInt(360f / ArcStepDegrees));
            var points = new Vector2[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }

            return new IconPolyline(points, closed: true);
        }

        private static void AddCubic(List<Vector2> points, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            for (int i = 1; i <= CurveSegments; i++)
            {
                float t = (float)i / CurveSegments;
                float u = 1f - t;
                points.Add((u * u * u * p0) + (3f * u * u * t * p1) + (3f * u * t * t * p2) + (t * t * t * p3));
            }
        }

        /// <summary>SVG elliptical arc, converted from endpoint to center form (SVG 1.1, F.6.5).</summary>
        private static void AddArc(List<Vector2> points, Vector2 from, float rx, float ry, float rotationDegrees, bool largeArc, bool sweep, Vector2 to)
        {
            rx = Mathf.Abs(rx);
            ry = Mathf.Abs(ry);
            if (rx < 1e-6f || ry < 1e-6f || (from - to).sqrMagnitude < 1e-12f)
            {
                points.Add(to);
                return;
            }

            float phi = rotationDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(phi);
            float sin = Mathf.Sin(phi);
            Vector2 half = (from - to) / 2f;
            var prime = new Vector2((cos * half.x) + (sin * half.y), (-sin * half.x) + (cos * half.y));

            // Radii too small for the endpoints are scaled up, as SVG requires.
            float lambda = (prime.x * prime.x / (rx * rx)) + (prime.y * prime.y / (ry * ry));
            if (lambda > 1f)
            {
                float scale = Mathf.Sqrt(lambda);
                rx *= scale;
                ry *= scale;
            }

            float numerator = (rx * rx * ry * ry) - (rx * rx * prime.y * prime.y) - (ry * ry * prime.x * prime.x);
            float denominator = (rx * rx * prime.y * prime.y) + (ry * ry * prime.x * prime.x);
            float factor = Mathf.Sqrt(Mathf.Max(0f, numerator / denominator)) * (largeArc == sweep ? -1f : 1f);
            var centerPrime = new Vector2(factor * rx * prime.y / ry, -factor * ry * prime.x / rx);
            Vector2 center = new Vector2((cos * centerPrime.x) - (sin * centerPrime.y), (sin * centerPrime.x) + (cos * centerPrime.y)) + ((from + to) / 2f);

            float startAngle = Angle(new Vector2(1f, 0f), new Vector2((prime.x - centerPrime.x) / rx, (prime.y - centerPrime.y) / ry));
            float delta = Angle(
                new Vector2((prime.x - centerPrime.x) / rx, (prime.y - centerPrime.y) / ry),
                new Vector2((-prime.x - centerPrime.x) / rx, (-prime.y - centerPrime.y) / ry));
            if (!sweep && delta > 0f)
            {
                delta -= Mathf.PI * 2f;
            }
            else if (sweep && delta < 0f)
            {
                delta += Mathf.PI * 2f;
            }

            int segments = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(delta) * Mathf.Rad2Deg / ArcStepDegrees));
            for (int i = 1; i <= segments; i++)
            {
                float angle = startAngle + (delta * i / segments);
                var unit = new Vector2(rx * Mathf.Cos(angle), ry * Mathf.Sin(angle));
                points.Add(i == segments ? to : center + new Vector2((cos * unit.x) - (sin * unit.y), (sin * unit.x) + (cos * unit.y)));
            }
        }

        private static float Angle(Vector2 u, Vector2 v)
        {
            float angle = Mathf.Atan2((u.x * v.y) - (u.y * v.x), Vector2.Dot(u, v));
            return angle;
        }

        /// <summary>Reads numbers, flags, and commands from path data.</summary>
        private sealed class Reader
        {
            private readonly string _text;
            private int _index;

            public Reader(string text)
            {
                _text = text;
            }

            /// <summary>Skips spaces and commas. Returns false at the end.</summary>
            public bool SkipSeparators()
            {
                while (_index < _text.Length && (char.IsWhiteSpace(_text[_index]) || _text[_index] == ','))
                {
                    _index++;
                }

                return _index < _text.Length;
            }

            public bool PeekIsCommand()
            {
                char c = _text[_index];
                return char.IsLetter(c) && c != 'e' && c != 'E';
            }

            public char ReadCommand()
            {
                return _text[_index++];
            }

            public Vector2 ReadPoint()
            {
                float x = ReadNumber();
                float y = ReadNumber();
                return new Vector2(x, y);
            }

            /// <summary>Arc flags may be written without separators, such as "a2 2 0 011 1".</summary>
            public bool ReadFlag()
            {
                SkipSeparators();
                if (_index < _text.Length && (_text[_index] == '0' || _text[_index] == '1'))
                {
                    return _text[_index++] == '1';
                }

                throw new FormatException($"Expected an arc flag at position {_index} in '{_text}'.");
            }

            public float ReadNumber()
            {
                SkipSeparators();
                int begin = _index;
                if (_index < _text.Length && (_text[_index] == '-' || _text[_index] == '+'))
                {
                    _index++;
                }

                bool dot = false;
                bool exponent = false;
                while (_index < _text.Length)
                {
                    char c = _text[_index];
                    if (char.IsDigit(c))
                    {
                        _index++;
                    }
                    else if (c == '.' && !dot && !exponent)
                    {
                        dot = true;
                        _index++;
                    }
                    else if ((c == 'e' || c == 'E') && !exponent)
                    {
                        exponent = true;
                        _index++;
                        if (_index < _text.Length && (_text[_index] == '-' || _text[_index] == '+'))
                        {
                            _index++;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                // "1.5.5" means 1.5 then .5, as in SVG.
                if (_index == begin || !float.TryParse(_text.Substring(begin, _index - begin), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    throw new FormatException($"Expected a number at position {begin} in '{_text}'.");
                }

                return value;
            }
        }
    }
}
