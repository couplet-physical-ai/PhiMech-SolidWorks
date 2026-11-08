// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// The exact bounding box of an exported STL, read from its vertices (binary or ASCII). This is the
// size the slicer will see, so the fit check after export uses it rather than SolidWorks'
// approximate part box. STL carries no unit: the add-in exports it in millimetres (PrintExport).

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace PhiMech.SolidWorks.Print
{
    public sealed class StlBox
    {
        public double[] Min;
        public double[] Max;
        public int Triangles;

        public double[] Size => new[] { Max[0] - Min[0], Max[1] - Min[1], Max[2] - Min[2] };
    }

    public static class StlBounds
    {
        public static StlBox Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 84)
            {
                var count = BitConverter.ToUInt32(bytes, 80);
                if ((long)84 + (long)count * 50 == bytes.LongLength) return Binary(bytes, (int)count);
            }
            return Ascii(Encoding.ASCII.GetString(bytes));
        }

        private static StlBox Binary(byte[] b, int count)
        {
            var box = New();
            for (var t = 0; t < count; t++)
            {
                var o = 84 + t * 50 + 12;   // skip the normal
                for (var v = 0; v < 3; v++)
                {
                    for (var a = 0; a < 3; a++) Grow(box, a, BitConverter.ToSingle(b, o + v * 12 + a * 4));
                }
            }
            box.Triangles = count;
            return Done(box);
        }

        private static StlBox Ascii(string text)
        {
            var box = New();
            var vertices = 0;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) throw new FormatException("bad STL vertex line: " + line);
                for (var a = 0; a < 3; a++)
                    Grow(box, a, double.Parse(parts[a + 1], NumberStyles.Float, CultureInfo.InvariantCulture));
                vertices++;
            }
            box.Triangles = vertices / 3;
            return Done(box);
        }

        private static StlBox New() => new StlBox
        {
            Min = new[] { double.MaxValue, double.MaxValue, double.MaxValue },
            Max = new[] { double.MinValue, double.MinValue, double.MinValue },
        };

        private static void Grow(StlBox box, int axis, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new FormatException("the STL has a non-finite coordinate");
            if (v < box.Min[axis]) box.Min[axis] = v;
            if (v > box.Max[axis]) box.Max[axis] = v;
        }

        private static StlBox Done(StlBox box)
        {
            if (box.Triangles == 0) throw new FormatException("the STL has no triangles");
            return box;
        }
    }
}
