// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// Does the part fit the printer? Pure and deterministic: the part's axis-aligned bounding box (mm)
// is tried in all six axis-aligned orientations against the build volume (mm). The answer names
// the recommended orientation, the axis that limits it and by how much, and refuses when nothing
// fits.
//
// "As exported" matters: the slicer receives the STL exactly as SolidWorks wrote it and treats the
// part's Z as up, so only the two Z-up orientations (as modelled, or turned 90 degrees on the bed)
// can be sliced without re-orienting the part in SolidWorks first. When the part fits only on its
// side, the result says which side, and slicing is refused with that advice.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PhiMech.SolidWorks.Print
{
    public sealed class FitOrientation
    {
        internal FitOrientation(int index, int[] axes, double[] part, double[] envelope)
        {
            Index = index;
            PartAxisAlong = axes;
            Size = axes.Select(a => part[a]).ToArray();
            Margin = Enumerable.Range(0, 3).Select(i => envelope[i] - Size[i]).ToArray();
            Fits = Margin.All(m => m >= -FitCheck.Tolerance);
            // The limiting axis: least room (relative) when it fits, the worst overflow when not.
            var relative = Enumerable.Range(0, 3).Select(i => Margin[i] / envelope[i]).ToArray();
            LimitAxis = Array.IndexOf(relative, relative.Min());
            Score = relative.Min();
        }

        public int Index { get; }

        /// <summary>PartAxisAlong[i] = which part axis (0 X, 1 Y, 2 Z) lies along printer axis i.</summary>
        public int[] PartAxisAlong { get; }

        /// <summary>The part's extent along the printer's X, Y, Z in this orientation (mm).</summary>
        public double[] Size { get; }

        /// <summary>Room left along X, Y, Z (mm); negative = too big by that much.</summary>
        public double[] Margin { get; }

        public bool Fits { get; }

        /// <summary>Printer axis (0 X, 1 Y, 2 Z) with the least room, or the worst overflow.</summary>
        public int LimitAxis { get; }

        /// <summary>Smallest margin relative to the build volume (higher is better).</summary>
        public double Score { get; }

        /// <summary>The slicer can print it like this without the part being re-oriented: part Z up.</summary>
        public bool AsExported => PartAxisAlong[2] == 2;

        public string Label
        {
            get
            {
                var up = FitCheck.AxisLetter(PartAxisAlong[2]);
                if (PartAxisAlong[2] == 2) return PartAxisAlong[0] == 0 ? "as modelled (part Z up)" : "as modelled, turned 90° on the bed";
                return "on its side (part " + up + " up" + (PartAxisAlong[0] < PartAxisAlong[1] ? "" : ", turned 90°") + ")";
            }
        }
    }

    public sealed class FitResult
    {
        public double[] Part;
        public double[] Envelope;
        public string EnvelopeSource;
        public List<FitOrientation> Orientations = new List<FitOrientation>();
        /// <summary>The recommended orientation: a fitting Z-up one if there is any, else the best fitting
        /// one, else (nothing fits) the one that comes closest.</summary>
        public FitOrientation Best;

        public bool Fits => Best != null && Best.Fits;

        /// <summary>Fits without re-orienting the part: the slicer can take the STL as exported.</summary>
        public bool FitsAsExported => Orientations.Any(o => o.Fits && o.AsExported);

        public string Headline =>
            !Fits ? "Too big for this printer"
            : FitsAsExported ? "Fits"
            : "Fits only on its side";

        /// <summary>Two or three plain sentences, with every number.</summary>
        public string Detail
        {
            get
            {
                var part = "Part " + FitCheck.Size(Part) + "; build volume " + FitCheck.Size(Envelope)
                           + (string.IsNullOrEmpty(EnvelopeSource) ? "" : " (" + EnvelopeSource + ")") + ".";
                if (Best == null) return part;
                var axis = FitCheck.AxisName(Best.LimitAxis);
                if (!Fits)
                {
                    return part + " It does not fit in any of the six orientations. Closest: " + Best.Label + ", still "
                           + FitCheck.Mm(-Best.Margin[Best.LimitAxis]) + " mm too " + FitCheck.TooWhat(Best.LimitAxis)
                           + " along " + axis + ". Nothing will be sliced.";
                }
                var limit = " The limit is " + axis + ": " + FitCheck.Mm(Best.Margin[Best.LimitAxis]) + " mm to spare.";
                if (FitsAsExported) return part + " Best fit " + Best.Label + "." + limit;
                return part + " It fits only " + Best.Label + "." + limit
                       + " The slicer takes the part as exported (its Z up): re-orient it in SolidWorks so part "
                       + FitCheck.AxisLetter(Best.PartAxisAlong[2]) + " points up, then slice.";
            }
        }
    }

    public static class FitCheck
    {
        public const double Tolerance = 1e-6;

        /// <summary>Example only: configure the real build volume in print-settings.json.</summary>
        public static readonly double[] DefaultEnvelope = { 200.0, 200.0, 200.0 };

        // Z-up orientations first: on a tie the as-modelled one wins.
        private static readonly int[][] Permutations =
        {
            new[] { 0, 1, 2 }, new[] { 1, 0, 2 },
            new[] { 0, 2, 1 }, new[] { 2, 0, 1 },
            new[] { 1, 2, 0 }, new[] { 2, 1, 0 },
        };

        public static FitResult Evaluate(double[] partMm, double[] envelopeMm, string envelopeSource = null)
        {
            if (partMm == null || partMm.Length != 3 || partMm.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0))
                throw new ArgumentException("the part size must be three non-negative numbers in mm", nameof(partMm));
            if (envelopeMm == null || envelopeMm.Length != 3 || envelopeMm.Any(v => double.IsNaN(v) || v <= 0))
                throw new ArgumentException("the build volume must be three positive numbers in mm", nameof(envelopeMm));
            var r = new FitResult { Part = partMm.ToArray(), Envelope = envelopeMm.ToArray(), EnvelopeSource = envelopeSource };
            for (var i = 0; i < Permutations.Length; i++) r.Orientations.Add(new FitOrientation(i, Permutations[i], partMm, envelopeMm));
            r.Best = r.Orientations.Where(o => o.Fits && o.AsExported).OrderByDescending(o => o.Score).ThenBy(o => o.Index).FirstOrDefault()
                     ?? r.Orientations.Where(o => o.Fits).OrderByDescending(o => o.Score).ThenBy(o => o.Index).FirstOrDefault()
                     ?? r.Orientations.OrderByDescending(o => o.Score).ThenBy(o => o.Index).First();
            return r;
        }

        public static string AxisLetter(int axis) => axis == 0 ? "X" : axis == 1 ? "Y" : "Z";

        public static string AxisName(int axis) => axis == 0 ? "X (bed width)" : axis == 1 ? "Y (bed depth)" : "Z (height)";

        internal static string TooWhat(int axis) => axis == 2 ? "tall" : axis == 0 ? "wide" : "deep";

        public static string Mm(double v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

        public static string Size(double[] v) => v == null ? "?" : Mm(v[0]) + " × " + Mm(v[1]) + " × " + Mm(v[2]) + " mm";
    }
}
