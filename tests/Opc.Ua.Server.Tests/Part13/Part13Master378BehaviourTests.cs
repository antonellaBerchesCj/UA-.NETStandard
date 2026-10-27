/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.Part13
{
    /// <summary>
    /// Compares every aggregate result with the result of the 1.5.378 release branch.
    /// </summary>
    /// <remarks>
    /// Master378AggregateResults.txt.gz holds the output of <see cref="AggregateBehaviourMatrix"/>
    /// compiled against the 1.5.378 release branch (see README.md). Every result that differs from
    /// it must be explained by one of the rules below, and each rule names the Part 13 clause that
    /// requires the change. A rule computes the expected result from the raw data, from the
    /// definition of the aggregate or from another 1.5.378 result; only where noted it leaves a
    /// field open. The rules are also applied to the results that did not change, so a rule that
    /// is too broad fails as well. A rule that explains no difference fails the test, so the list
    /// cannot keep stale entries.
    /// </remarks>
    [TestFixture]
    [Category("Part13")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class Part13Master378BehaviourTests
    {
        /// <summary>The embedded 1.5.378 results.</summary>
        public const string GoldenResourceName = "Part13.Master378AggregateResults.txt.gz";

        /// <summary>SHA-256 of the uncompressed 1.5.378 results (see README.md).</summary>
        private const string kGoldenSha256 = "2197c56809f8ad36f7c8ef28d6ad8b7ea2ad79263e3fbb537d71071a190972e5";

        private const uint kGood = StatusCodes.Good;
        private const uint kUncertain = StatusCodes.UncertainDataSubNormal;
        private const uint kBad = StatusCodes.Bad;
        private const uint kNoData = StatusCodes.BadNoData;
        private const uint kInfoTypeDataValue = 0x400;
        private const uint kCalculated = (uint)AggregateBits.Calculated;
        private const uint kInterpolated = (uint)AggregateBits.Interpolated;
        private const uint kPartial = (uint)AggregateBits.Partial;

        private static readonly DateTime s_start = new DateTime(2011, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private static readonly string[] s_timeBased = [
            "TimeAverage", "Total", "TimeAverage2", "Total2", "Minimum2", "Maximum2", "MinimumActualTime2",
            "MaximumActualTime2", "Range2", "DurationInStateZero", "DurationInStateNonZero"
        ];

        private static readonly string[] s_percentBased = [.. s_timeBased, "Average", "Count", "NumberOfTransitions"];

        /// <summary>Part 13 Table 50: the aggregates with the valid data type "Numeric".</summary>
        private static readonly string[] s_numericOnly = [
            "Interpolative", "Average", "TimeAverage", "TimeAverage2", "Total", "Total2",
            "Minimum", "Maximum", "MinimumActualTime", "MaximumActualTime", "Range",
            "Minimum2", "Maximum2", "MinimumActualTime2", "MaximumActualTime2", "Range2",
            "Delta", "DeltaBounds", "StandardDeviationSample", "VarianceSample",
            "StandardDeviationPopulation", "VariancePopulation"
        ];

        /// <summary>The numeric-only aggregates that use Interpolated or Simple Bounding Values.</summary>
        private static readonly string[] s_numericWithBounds = [
            "TimeAverage", "Total", "TimeAverage2", "Total2", "Minimum2", "Maximum2",
            "MinimumActualTime2", "MaximumActualTime2", "Range2"
        ];

        /// <summary>
        /// The embedded file is the 1.5.378 output: it must not be regenerated from this branch.
        /// </summary>
        [Test]
        public void GoldenFileIsThe1_5_378Output()
        {
            string text = ReadGoldenText();

            byte[] bytes = Encoding.UTF8.GetBytes(text);
#if NET
            byte[] digest = SHA256.HashData(bytes);
#else
            byte[] digest;

            using (var sha = SHA256.Create())
            {
                digest = sha.ComputeHash(bytes);
            }
#endif
            var hash = new StringBuilder();

            foreach (byte b in digest)
            {
                hash.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            Assert.That(hash.ToString(), Is.EqualTo(kGoldenSha256), "Master378AggregateResults.txt changed; see README.md");
        }

        /// <summary>
        /// Every result that differs from 1.5.378 is explained by a rule, and every rule is used.
        /// </summary>
        [Test]
        public void EveryDifferenceFrom1_5_378IsExplainedByAPart13Rule()
        {
            Dictionary<string, Result> master = ParseGolden(ReadGoldenText());
            List<MatrixDataSet> sets;

            using (var reader = new StringReader(AnnexAExamples.ReadCsvText()))
            {
                sets = AggregateBehaviourMatrix.LoadDataSets(reader);
            }

            List<MatrixResult> results = AggregateBehaviourMatrix.Run(sets);
            var branch = new Dictionary<string, Result>(StringComparer.Ordinal);

            foreach (MatrixResult result in results)
            {
                branch[RowKey(result.Key, result.Index)] = Result.Parse(result.Result);
            }

            Assert.That(branch.Keys.OrderBy(k => k, StringComparer.Ordinal),
                Is.EqualTo(master.Keys.OrderBy(k => k, StringComparer.Ordinal)),
                "the same requests must return the same number of values as 1.5.378");

            var context = new Context(master, branch);
            var used = new Dictionary<string, int>(StringComparer.Ordinal);
            var failures = new List<string>();
            int differences = 0;

            foreach (Rule rule in s_rules)
            {
                used[rule.Id] = 0;
            }

            foreach (MatrixResult result in results)
            {
                var row = new Row(context, result);
                var expected = new Expectation(row.Master);
                var hits = new List<string>();

                foreach (Rule rule in s_rules)
                {
                    if (rule.Applies(row))
                    {
                        rule.Apply(row, expected);
                        hits.Add(rule.Id);
                    }
                }

                bool changed = !row.Master.Equals(row.Branch);
                List<string> mismatches = expected.Compare(row.Branch);

                if (changed)
                {
                    differences++;
                }

                if (mismatches.Count > 0)
                {
                    failures.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} #{1} [{2}, {3}): 1.5.378 '{4}', now '{5}', expected {6} (fields {7}; rules: {8})",
                        result.Key,
                        result.Index,
                        row.Early,
                        row.Late,
                        row.Master,
                        row.Branch,
                        expected,
                        string.Join(", ", mismatches),
                        hits.Count == 0 ? "none" : string.Join(", ", hits)));
                }
                else if (changed)
                {
                    foreach (string id in hits)
                    {
                        used[id]++;
                    }
                }
            }

            var summary = new StringBuilder();
            summary.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} results, {1} differ from 1.5.378", results.Count, differences));

            foreach (Rule rule in s_rules)
            {
                summary.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-30} {1,5}  {2}", rule.Id, used[rule.Id], rule.Reason));
            }

            TestContext.Out.WriteLine(summary.ToString());

            Assert.That(failures, Is.Empty,
                "results that no Part 13 rule explains (first 40):" + Environment.NewLine +
                string.Join(Environment.NewLine, failures.Take(40)));

            string[] unused = used.Where(u => u.Value == 0).Select(u => u.Key).ToArray();
            Assert.That(unused, Is.Empty, "rules that explain no difference must be removed");
        }

        #region Rules
        /// <summary>
        /// The changes from 1.5.378, in the order they are applied.
        /// </summary>
        private static readonly Rule[] s_rules = [
            new Rule(
                "REVERSE-PARTIAL",
                "5.3.3.2, 5.4.2.2: Partial marks the interval holding the start of data (or the end of data unless " +
                "extrapolated) in either time direction; 1.5.378 used the queue order, so with time flowing backward " +
                "it took the last value as the start of data.",
                r => r.Reverse && r.Aggregate != "Average" && r.Aggregate != "Interpolative",
                (r, e) => {
                    if (r.Early < r.EndOfData && r.EndOfData < r.Late)
                    {
                        e.Bits &= ~kPartial;
                    }

                    if (r.Early < r.StartOfData && r.StartOfData < r.Late)
                    {
                        e.AddBits(kPartial);
                    }

                    // Partial is not set when the end bound was extrapolated (aggregate specific).
                    if (r.Early <= r.EndOfData && r.EndOfData < r.Late)
                    {
                        e.IgnoredBits |= kPartial;
                    }
                }),
            new Rule(
                "TA-REAL-BOUNDS",
                "3.1.8, Tables 54, 56: TimeAverage and Total return a Double, so the interpolated bounds stay real " +
                "values instead of being converted to Int32. Expected: the 1.5.378 value of the Double history.",
                r => IsOneOf(r.Aggregate, "TimeAverage", "Total") && r.IsInt32,
                (r, e) => e.Value = r.MasterOf(r.DoubleDataSet, r.Aggregate, r.TreatUncertainAsBad).Value),
            new Rule(
                "TA-ALWAYS-SLOPED",
                "5.4.3.6, 5.4.3.8: TimeAverage and Total always use a sloped line, also for a Stepped variable. " +
                "Expected: the 1.5.378 result of Historian2 (the raw data of Historian3 with Stepped = False).",
                r => IsOneOf(r.Aggregate, "TimeAverage", "Total") && r.HasSlopedTwin && r.Late <= r.EndOfData,
                (r, e) => {
                    Result twin = r.MasterOf("Historian2", r.Aggregate, r.TreatUncertainAsBad);
                    e.Value = twin.Value;
                    e.Code = twin.Code;
                }),
            new Rule(
                "TA-ALWAYS-SLOPED-EXTRAPOLATED",
                "5.4.3.6, 5.4.3.8, 3.1.8: after the end of data the sloped line ends in an extrapolated bound " +
                "(UseSlopedExtrapolation = False): Uncertain_DataSubNormal, as for the sloped Historian1.",
                r => IsOneOf(r.Aggregate, "TimeAverage", "Total") && r.HasSlopedTwin && r.Late > r.EndOfData && r.Master.Code != kNoData,
                (r, e) => {
                    e.Code = kUncertain;
                    e.ValueOpen = true;
                }),
            new Rule(
                "TA-BOUNDS-IGNORE-TUAB",
                "3.1.8, 5.4.3.6, 5.4.3.8: TimeAverage and Total use Interpolated Bounding Values, and TreatUncertainAsBad " +
                "does not apply to bounds (WG answer to Mantis 11462): with TreatUncertainAsBad = True a bound next to an " +
                "Uncertain raw value is interpolated from it. The value is computed from the raw data: a sloped line from " +
                "the start bound through the Good raw values to the end bound (the other raw values are left out, as in " +
                "1.5.378).",
                r => IsOneOf(r.Aggregate, "TimeAverage", "Total") && r.TreatUncertainAsBad &&
                    InterpolatedBound(r, r.Early) != null && InterpolatedBound(r, r.Late) != null &&
                    (InterpolatedBoundUsesUncertain(r, r.Early) || InterpolatedBoundUsesUncertain(r, r.Late)),
                (r, e) => {
                    double area = SlopedArea(r);
                    e.Value = Number(r.Aggregate == "Total" ? area : area / (r.Late - r.Early));
                }),
            new Rule(
                "MINMAX-UNCERTAIN-STATUS",
                "Tables 58-62: Uncertain_DataSubNormal if Bad values (or, with TreatUncertainAsBad = True, Uncertain " +
                "values) exist, or if an Uncertain value lies beyond the Good result; Calculated is set when the " +
                "status is Uncertain (Tables 60, 61; Mantis 11427).",
                r => IsOneOf(r.Aggregate, "Minimum", "Maximum", "MinimumActualTime", "MaximumActualTime", "Range") &&
                    (r.Master.Code == kGood || r.Master.Code == kUncertain),
                (r, e) => {
                    double[] good = r.Inside.Where(p => p.IsGood).Select(p => p.Number).ToArray();
                    double[] uncertain = r.Inside.Where(p => p.IsUncertain).Select(p => p.Number).ToArray();
                    double min = good.Min();
                    double max = good.Max();
                    bool beyond =
                        r.Aggregate.StartsWith("Minimum", StringComparison.Ordinal) ? uncertain.Any(u => u < min) :
                        r.Aggregate.StartsWith("Maximum", StringComparison.Ordinal) ? uncertain.Any(u => u > max) :
                        uncertain.Any(u => u < min || u > max);
                    bool isUncertain =
                        r.Inside.Any(p => p.IsBad) ||
                        (r.TreatUncertainAsBad ? uncertain.Length > 0 : beyond);

                    e.Code = isUncertain ? kUncertain : kGood;

                    if (isUncertain)
                    {
                        e.AddBits(kCalculated);
                    }
                }),
            new Rule(
                "DURATION-DATA-POINT-STATUS",
                "5.4.3.31-34: a region is Good (Bad) if its data point is Good (Bad); the first region has the status " +
                "of the data point at or before the start, Bad if there is none; Uncertain counts for neither and " +
                "TreatUncertainAsBad does not apply (Mantis 11425). The region that starts at the last value runs to " +
                "the end of the interval like any other region (1.5.378 ended it after 1 ms). Durations are rounded to " +
                "whole milliseconds (Mantis 11499), percentages use the unrounded durations.",
                r => IsOneOf(r.Aggregate, "DurationGood", "DurationBad", "PercentGood", "PercentBad") && r.Master.Code == kGood,
                (r, e) => {
                    bool good = r.Aggregate.EndsWith("Good", StringComparison.Ordinal);
                    double duration = Duration(r, good, out double width);
                    e.Value = Number(r.Aggregate.StartsWith("Duration", StringComparison.Ordinal)
                        ? Math.Round(duration, MidpointRounding.ToEven)
                        : duration / width * 100);
                }),
            new Rule(
                "BOUND-SIMPLE-BOUNDING-VALUE",
                "3.1.9, Tables 76, 77: StartBound and EndBound return the Simple Bounding Value with its status " +
                "(Bound Uncertain: Same as bound); TreatUncertainAsBad does not apply to bounds, so an Uncertain " +
                "raw value is used (WG answer to Mantis 11462).",
                r => IsOneOf(r.Aggregate, "StartBound", "EndBound"),
                (r, e) => {
                    bool start = r.Aggregate == "StartBound";
                    double time = r.Reverse ? (start ? r.Late : r.Early) : (start ? r.Early : r.Late);
                    Bound bound = SimpleBound(r, time);

                    if (bound.Code == kNoData)
                    {
                        e.SetNoData();
                    }
                    else
                    {
                        e.Code = bound.Code;
                        e.Type = bound.Value is bool ? "Boolean" : r.IsInt32 ? "Int32" : "Double";
                        e.Value = bound.Value is bool flag ? (flag ? "True" : "False") : Number(Convert.ToDouble(bound.Value, CultureInfo.InvariantCulture));
                    }

                    if (r.Master.Code == kNoData || bound.Code == kNoData)
                    {
                        e.BitsOpen = true;
                    }
                }),
            new Rule(
                "DELTABOUNDS-FROM-BOUNDS",
                "Table 78: DeltaBounds is EndBound - StartBound of the interval (chronologically, 5.4.2.2); " +
                "Bad_NoData if either bound is Bad, Uncertain_DataSubNormal if either bound is Uncertain.",
                r => r.Aggregate == "DeltaBounds",
                (r, e) => {
                    Result first = r.BranchOf(r.Reverse ? "EndBound" : "StartBound");
                    Result last = r.BranchOf(r.Reverse ? "StartBound" : "EndBound");

                    if (StatusCode.IsBad(first.Code) || StatusCode.IsBad(last.Code))
                    {
                        e.SetNoData();
                        e.BitsOpen = true;
                        return;
                    }

                    e.Code = StatusCode.IsUncertain(first.Code) || StatusCode.IsUncertain(last.Code) ? kUncertain : kGood;
                    e.Type = first.Type;
                    e.AddBits(kCalculated);

                    if (first.Type == "Boolean")
                    {
                        e.ValueOpen = true;
                    }
                    else
                    {
                        e.Value = Number(ParseNumber(last.Value) - ParseNumber(first.Value));
                    }
                }),
            new Rule(
                "WORSTQUALITY2-IGNORES-TUAB",
                "5.4.3.36: the worst status of the raw values and of the Simple Bounding start bound, ignoring " +
                "TreatUncertainAsBad; the start bound is taken at the chronological start (5.4.2.2).",
                r => r.Aggregate == "WorstQuality2" && r.Master.Code != kNoData,
                (r, e) => {
                    var codes = r.Chronological.Select(p => p.Code).ToList();

                    if (!r.Real.Any(p => p.T == r.Early))
                    {
                        codes.Insert(0, SimpleBound(r, r.Early).Code);
                    }

                    uint worst = codes.OrderByDescending(Severity).First();
                    e.Value = string.Format(CultureInfo.InvariantCulture, "0x{0:X8}", worst);

                    if (r.Reverse)
                    {
                        e.BitsOpen = true;
                    }
                }),
            new Rule(
                "GOOD-ONLY",
                "5.4.3.27, 5.4.3.37-40: Delta, StandardDeviation and Variance use Good raw values only, whatever " +
                "TreatUncertainAsBad is (upstream a07f4eb1a, Mantis 11495); Delta is Uncertain if non-Good values are " +
                "skipped to find the first or last value, the others if non-Good values were ignored.",
                r => IsOneOf(r.Aggregate, "Delta", "StandardDeviationPopulation", "StandardDeviationSample", "VariancePopulation", "VarianceSample") &&
                    !r.TreatUncertainAsBad && r.Inside.Any(p => p.IsUncertain),
                (r, e) => {
                    Point[] good = r.Inside.Where(p => p.IsGood).ToArray();

                    if (good.Length == 0)
                    {
                        e.SetNoData();
                        e.BitsOpen = true;
                        return;
                    }

                    if (r.Aggregate == "Delta")
                    {
                        bool skipped = r.Inside.Any(p => !p.IsGood && (p.T < good[0].T || p.T > good[good.Length - 1].T));
                        e.Code = skipped ? kUncertain : kGood;

                        if (good[0].Value is bool)
                        {
                            e.ValueOpen = true;
                        }
                        else
                        {
                            e.Value = Number(good[good.Length - 1].Number - good[0].Number);
                        }
                    }
                    else
                    {
                        double mean = good.Average(p => p.Number);
                        double squares = good.Sum(p => (p.Number - mean) * (p.Number - mean));
                        int n = r.Aggregate.EndsWith("Population", StringComparison.Ordinal) ? good.Length : good.Length - 1;
                        double variance = n > 0 ? squares / n : 0;
                        e.Value = Number(r.Aggregate.StartsWith("StandardDeviation", StringComparison.Ordinal) ? Math.Sqrt(variance) : variance);
                        e.Code = r.Inside.Any(p => !p.IsGood) ? kUncertain : kGood;
                    }

                    if (e.Type == "Null")
                    {
                        e.Type = good[0].Value is bool ? "Boolean" : r.IsInt32 && r.Aggregate == "Delta" ? "Int32" : "Double";
                    }

                    e.AddBits(kCalculated);
                }),
            new Rule(
                "INTERPOLATIVE-BOUND-IGNORES-TUAB",
                "3.1.8, Table 52: Interpolative returns the Interpolated Bounding Value, and TreatUncertainAsBad does " +
                "not apply to bounds (WG answer to Mantis 11462): an Uncertain neighbour is used, so the result is the " +
                "result of the same interval with TreatUncertainAsBad = False.",
                r => r.Aggregate == "Interpolative" && r.TreatUncertainAsBad,
                (r, e) => e.CopyFrom(r.BranchOf("Interpolative", false))),
            new Rule(
                "TA2-TOTAL2-GOOD-DATA-ONLY",
                "5.4.3.7, 5.4.3.9, Tables 55, 57: TimeAverage2 and Total2 use only Good data, also with " +
                "TreatUncertainAsBad = False (the definition names non-Good data, as Count does; Mantis 11495); a " +
                "sub-interval that ends at a value left out, or at the missing end bound after the last value, uses " +
                "its Good starting value; without Good data the result is Bad. The value is computed from the raw data.",
                r => IsOneOf(r.Aggregate, "TimeAverage2", "Total2"),
                (r, e) => {
                    GoodDataArea(r, out double area, out double duration, out bool beyondData, out bool nonBadData);

                    if (beyondData || duration == 0)
                    {
                        e.Code = beyondData || !nonBadData ? kNoData : kBad;
                        e.Type = "Null";
                        e.Value = "null";
                        e.ValueOpen = false;
                        e.TypeOpen = false;
                        e.BitsOpen = true;
                        return;
                    }

                    e.Type = "Double";
                    e.Value = Number(r.Aggregate == "Total2" ? area : area / duration);
                    e.ValueOpen = false;
                    e.TypeOpen = false;

                    // 1.5.378 returned no value where the region after the last value was 1 ms long.
                    if (StatusCode.IsBad(e.Code))
                    {
                        e.CodeOpen = true;
                    }
                }),
            new Rule(
                "DURATIONINSTATE-LAST-REGION",
                "5.4.3.22, 5.4.3.23: DurationInState uses the same regions as DurationGood, so the region that starts " +
                "at the last value runs to the end of the interval (1.5.378 ended it after 1 ms). The duration is " +
                "computed from the raw data.",
                r => IsOneOf(r.Aggregate, "DurationInStateZero", "DurationInStateNonZero") &&
                    r.Early <= r.EndOfData && r.EndOfData < r.Late && !StatusCode.IsBad(r.Master.Code),
                (r, e) => e.Value = Number(DurationInState(r, r.Aggregate == "DurationInStateNonZero"))),
            new Rule(
                "ANNOTATIONCOUNT-COUNTS-ANNOTATIONS",
                "5.4.3.20, Table 68: AnnotationCount counts the Annotations in the interval, not the raw values (Mantis " +
                "11500). The histories have no Annotations, so an interval within the data is 0 (Good, Calculated), and " +
                "an interval before the start or after the end of data is Bad_NoData.",
                r => r.Aggregate == "AnnotationCount",
                (r, e) => {
                    bool beyondData = r.Reverse
                        ? r.Late < r.StartOfData || r.Early >= r.EndOfData
                        : r.Late <= r.StartOfData || r.Early > r.EndOfData;

                    if (beyondData)
                    {
                        // an interval that ends at the first raw value was Good 0 in 1.5.378.
                        if (r.Master.Code != kNoData)
                        {
                            e.SetNoData();
                            e.Bits = 0;
                        }

                        return;
                    }

                    e.Code = kGood;
                    e.Type = "Int32";
                    e.Value = "0";
                }),
            new Rule(
                "TRANSITIONS-UNCERTAIN-IS-BAD",
                "5.4.3.24, 4.2.1.2: NumberOfTransitions uses the non-Bad values and says nothing about Uncertain " +
                "values, so with TreatUncertainAsBad = True an Uncertain value in the interval is Bad and is not " +
                "counted. The previous value is the bound, a non-Bad value prior to the interval (Table 72), and " +
                "TreatUncertainAsBad does not apply to bounds (Mantis 11462). The count is computed from the raw data.",
                r => r.Aggregate == "NumberOfTransitions" && r.TreatUncertainAsBad && !StatusCode.IsBad(r.Master.Code) &&
                    (r.Inside.Any(p => p.IsUncertain) || PreviousNonBad(r)?.IsUncertain == true),
                (r, e) => e.Value = Number(Transitions(r))),
            new Rule(
                "TIME-BASED-UNCERTAIN-AS-BAD",
                "5.4.3.2.1: with TreatUncertainAsBad = True Uncertain regions are Bad regions; 5.4.3.2.2: a region that " +
                "ends in a Bad or Uncertain value (or a missing end bound) is Uncertain when sloped interpolation is " +
                "used. The status can only get worse; a Bad result has no value.",
                r => s_timeBased.Contains(r.Aggregate, StringComparer.Ordinal) && r.TreatUncertainAsBad &&
                    !r.EqualPercentages && HasUncertainRegion(r),
                MayGetWorse),
            new Rule(
                "VALUE-BASED-UNCERTAIN-AS-BAD",
                "4.2.1.2, 5.4.3.2.1: with TreatUncertainAsBad = True an Uncertain value counts as Bad for " +
                "PercentDataBad (Count, NumberOfTransitions).",
                r => IsOneOf(r.Aggregate, "Count", "NumberOfTransitions") && r.TreatUncertainAsBad &&
                    !r.EqualPercentages && r.Inside.Any(p => p.IsUncertain),
                MayGetWorse),
            new Rule(
                "EQUAL-PERCENT-NEVER-BAD",
                "4.2.1.2: with PercentDataGood = 100 - PercentDataBad the result of the PercentDataGood calculation " +
                "is used, so an interval below PercentDataGood is Uncertain_DataSubNormal, not Bad (Annex A Historian3).",
                r => s_percentBased.Contains(r.Aggregate, StringComparer.Ordinal) && r.EqualPercentages && r.Master.Code == kBad,
                (r, e) => {
                    e.Code = kUncertain;
                    e.ValueOpen = true;
                    e.TypeOpen = true;
                    e.BitsOpen = true;
                }),
            new Rule(
                "TABLE-50-NUMERIC-ONLY",
                "5.4.2.3, Table 50: the numeric aggregates do not support Boolean data; an interval that uses a " +
                "non-Bad Boolean value (in the interval, or the value a bound is taken from) is " +
                "Bad_AggregateInvalidInputs (Table 45). Historian4 is the Boolean history.",
                r => s_numericOnly.Contains(r.Aggregate, StringComparer.Ordinal) && r.HasBooleanValues && UsesNonBadValue(r),
                (r, e) => {
                    e.Constraints.Clear();
                    e.Timestamp = s_start.AddSeconds(r.Reverse ? r.Late : r.Early).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    e.Code = StatusCodes.BadAggregateInvalidInputs;
                    e.Type = "Null";
                    e.Value = "null";
                    e.CodeOpen = false;
                    e.TypeOpen = false;
                    e.ValueOpen = false;
                    e.BitsOpen = false;
                    e.Bits &= kPartial | kInfoTypeDataValue;
                    e.IgnoredBits |= kPartial | kInfoTypeDataValue;
                }),
            new Rule(
                "BAD-KEEPS-CALCULATED",
                "Tables 55-57, 63-72: Calculated is Set Always, also on a Bad result; 1.5.378 replaced the whole " +
                "StatusCode with Bad.",
                r => (s_timeBased.Contains(r.Aggregate, StringComparer.Ordinal) || r.Aggregate == "NumberOfTransitions") &&
                    r.Master.Code == kBad && r.Master.Bits == 0,
                (r, e) => e.AddBits(kCalculated))
        ];

        private static void MayGetWorse(Row row, Expectation e)
        {
            Result master = row.Master;
            e.CodeOpen = true;
            e.TypeOpen = true;
            bool valueWasOpen = e.ValueOpen;
            e.ValueOpen = true;

            // a Bad Count has no aggregate bits (as in 1.5.378).
            if (row.Aggregate == "Count")
            {
                e.BitsOpen = true;
            }

            e.Constraints.Add((actual, expected) => {
                if (Severity(actual.Code) < Severity(master.Code))
                {
                    return false;
                }

                if (StatusCode.IsBad(actual.Code))
                {
                    return actual.Type == "Null";
                }

                return valueWasOpen || SameValue(actual.Value, expected.Value);
            });
        }

        /// <summary>
        /// True if the aggregate uses a non-Bad raw value for the interval: a value in the interval
        /// (not for Interpolative and DeltaBounds) or the value a bound at the start or end time is
        /// taken from. The data is Stepped, so a bound uses the value at or before its time;
        /// Interpolated Bounding Values skip Bad values (3.1.8), Simple Bounding Values do not (3.1.9).
        /// </summary>
        private static bool UsesNonBadValue(Row row)
        {
            bool interpolated = IsOneOf(row.Aggregate, "Interpolative", "TimeAverage", "Total");

            bool BoundSource(double time)
            {
                Point source = interpolated
                    ? row.Real.LastOrDefault(p => p.T <= time && !p.IsBad)
                    : row.Real.LastOrDefault(p => p.T <= time);
                return source != null && !source.IsBad;
            }

            if (row.Aggregate == "Interpolative")
            {
                return BoundSource(row.Reverse ? row.Late : row.Early);
            }

            if (row.Aggregate != "DeltaBounds" && row.Inside.Any(p => !p.IsBad))
            {
                return true;
            }

            if (row.Aggregate == "DeltaBounds" || s_numericWithBounds.Contains(row.Aggregate, StringComparer.Ordinal))
            {
                return BoundSource(row.Early) || BoundSource(row.Late);
            }

            return false;
        }

        /// <summary>
        /// The Interpolated Bounding Value (3.1.8) from the non-Bad raw values (Mantis 11462), or
        /// null before the start of data. After the end of data it is extrapolated: sloped from the
        /// last two non-Bad values with UseSlopedExtrapolation, else stepped.
        /// </summary>
        private static double? InterpolatedBound(Row row, double time)
        {
            Point[] sources = InterpolatedBoundSources(row, time);

            if (sources == null)
            {
                return null;
            }

            if (sources.Length == 1)
            {
                return sources[0].Number;
            }

            Point first = sources[0];
            Point second = sources[1];
            return first.Number + ((second.Number - first.Number) * (time - first.T) / (second.T - first.T));
        }

        /// <summary>True if the Interpolated Bounding Value at the time uses an Uncertain raw value.</summary>
        private static bool InterpolatedBoundUsesUncertain(Row row, double time)
        {
            Point[] sources = InterpolatedBoundSources(row, time);
            return sources != null && sources.Any(p => p.IsUncertain);
        }

        /// <summary>
        /// The non-Bad raw values an Interpolated Bounding Value is calculated from (3.1.8): the
        /// value at the time, the values before and after it, or after the end of data the last
        /// value (stepped) or the last two values (sloped extrapolation). Null before the start of data.
        /// </summary>
        private static Point[] InterpolatedBoundSources(Row row, double time)
        {
            Point exact = row.Real.FirstOrDefault(p => p.T == time && !p.IsBad);

            if (exact != null)
            {
                return [exact];
            }

            Point before = row.Real.LastOrDefault(p => p.T < time && !p.IsBad);
            Point after = row.Real.FirstOrDefault(p => p.T > time && !p.IsBad);

            if (before == null)
            {
                return null;
            }

            if (after != null)
            {
                return [before, after];
            }

            Point second = row.Real.LastOrDefault(p => p.T < before.T && !p.IsBad);
            return row.UseSlopedExtrapolation && second != null ? [second, before] : [before];
        }

        /// <summary>
        /// The area under the sloped line from the start bound through the Good raw values inside the
        /// interval to the end bound (TimeAverage and Total with TreatUncertainAsBad = True).
        /// </summary>
        private static double SlopedArea(Row row)
        {
            var points = new List<(double T, double Value)> { (row.Early, InterpolatedBound(row, row.Early).Value) };
            points.AddRange(row.Real.Where(p => p.T > row.Early && p.T < row.Late && p.IsGood).Select(p => (p.T, p.Number)));
            points.Add((row.Late, InterpolatedBound(row, row.Late).Value));
            double area = 0;

            for (int ii = 0; ii < points.Count - 1; ii++)
            {
                area += (points[ii].Value + points[ii + 1].Value) * (points[ii + 1].T - points[ii].T) / 2;
            }

            return area;
        }

        /// <summary>
        /// TimeAverage2/Total2 over the Good data (5.4.3.7): the Simple Bounding Values and the raw
        /// values strictly inside the interval are the points; a region counts if it starts at a Good
        /// point, with a sloped line to a Good end point (Stepped = False) and the starting value
        /// otherwise.
        /// </summary>
        private static void GoodDataArea(Row row, out double area, out double duration, out bool beyondData, out bool nonBadData)
        {
            area = 0;
            duration = 0;
            nonBadData = false;
            beyondData = row.Early > row.EndOfData || row.Late < row.StartOfData;

            if (beyondData)
            {
                return;
            }

            var points = new List<(double T, double Value, bool Good, bool NonBad)>();
            Bound start = SimpleBound(row, row.Early, false);
            points.Add((row.Early, ToNumber(start.Value), start.Code == kGood, !StatusCode.IsBad(start.Code)));
            points.AddRange(row.Real.Where(p => p.T > row.Early && p.T < row.Late)
                .Select(p => (p.T, p.IsBad ? 0 : p.Number, p.IsGood, !p.IsBad)));
            Bound end = SimpleBound(row, row.Late, false);
            points.Add((row.Late, ToNumber(end.Value), end.Code == kGood, !StatusCode.IsBad(end.Code)));

            for (int ii = 0; ii < points.Count - 1; ii++)
            {
                double length = points[ii + 1].T - points[ii].T;

                if (length > 0 && points[ii].NonBad)
                {
                    nonBadData = true;
                }

                if (!points[ii].Good)
                {
                    continue;
                }

                bool sloped = !row.IsStepped && points[ii + 1].Good;
                area += sloped ? (points[ii].Value + points[ii + 1].Value) * length / 2 : points[ii].Value * length;
                duration += length;
            }
        }

        /// <summary>
        /// DurationInStateZero/NonZero in milliseconds: the regions start at the Simple Bounding
        /// start value and at every raw value inside the interval; a region counts if its point is
        /// Good and in the state.
        /// </summary>
        private static double DurationInState(Row row, bool nonZero)
        {
            var points = new List<(double T, object Value, bool Good)>();
            Bound start = SimpleBound(row, row.Early, false);
            points.Add((row.Early, start.Value, start.Code == kGood));
            points.AddRange(row.Real.Where(p => p.T > row.Early && p.T < row.Late).Select(p => (p.T, p.Value, p.IsGood)));
            double total = 0;

            for (int ii = 0; ii < points.Count; ii++)
            {
                double end = ii + 1 < points.Count ? points[ii + 1].T : row.Late;

                if (!points[ii].Good)
                {
                    continue;
                }

                bool isNonZero = ToNumber(points[ii].Value) != 0;

                if (isNonZero == nonZero)
                {
                    total += end - points[ii].T;
                }
            }

            // rounded to whole milliseconds (Mantis 11499).
            return Math.Round(Math.Round(total * 1000, 6), MidpointRounding.ToEven);
        }

        /// <summary>The last non-Bad raw value before the interval (chronologically).</summary>
        private static Point PreviousNonBad(Row row)
        {
            return row.Real.LastOrDefault(p => !p.IsBad && (row.Reverse ? p.T <= row.Early : p.T < row.Early));
        }

        /// <summary>
        /// NumberOfTransitions with TreatUncertainAsBad = True: the Good values in the interval are
        /// compared, starting with the last non-Bad value before the interval (the bound, Table 72).
        /// </summary>
        private static int Transitions(Row row)
        {
            Point previous = PreviousNonBad(row);
            object last = previous?.Value;
            bool hasLast = previous != null;
            int count = 0;

            foreach (Point point in row.Inside.Where(p => p.IsGood))
            {
                if (!hasLast || !Equals(last, point.Value))
                {
                    count++;
                }

                hasLast = true;
                last = point.Value;
            }

            return count;
        }

        private static double ToNumber(object value)
        {
            return value == null ? 0 : value is bool flag ? (flag ? 1 : 0) : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        private static bool HasUncertainRegion(Row row)
        {
            Point[] inside = row.Chronological;
            Point[] after = row.Real.Where(p => p.T >= row.Late).Take(1).ToArray();

            if (inside.Any(p => p.IsUncertain))
            {
                return true;
            }

            if (SimpleBound(row, row.Early).Code == kUncertain || SimpleBound(row, row.Late).Code == kUncertain)
            {
                return true;
            }

            if (!row.IsStepped)
            {
                // a region that ends in a Bad or Uncertain value, or in a missing end bound (Bad_NoData).
                if (inside.Skip(1).Concat(after).Any(p => !p.IsGood) || SimpleBound(row, row.Late).Code == kNoData)
                {
                    return true;
                }
            }

            return false;
        }
        #endregion

        #region Part 13 helpers
        /// <summary>
        /// The Simple Bounding Value (3.1.9) as GetSimpleBound calculates it, from the non-Bad raw
        /// values: TreatUncertainAsBad does not apply to bounds (WG answer to Mantis 11462).
        /// </summary>
        private static Bound SimpleBound(Row row, double time, bool roundToSourceType = true)
        {
            Point exact = row.Real.FirstOrDefault(p => p.T == time);

            if (exact != null)
            {
                return exact.IsBad ? Bound.NoData : new Bound(exact.Value, exact.Code);
            }

            Point before = row.Real.LastOrDefault(p => p.T < time);
            Point after = row.Real.FirstOrDefault(p => p.T > time);

            if (before == null || before.IsBad)
            {
                return Bound.NoData;
            }

            bool revertToStepped = false;

            if (!row.IsStepped)
            {
                if (after != null && !after.IsBad)
                {
                    double value = before.Number + ((after.Number - before.Number) * (time - before.T) / (after.T - before.T));

                    if (row.IsInt32 && roundToSourceType)
                    {
                        value = Math.Round(value, MidpointRounding.ToEven);
                    }

                    return new Bound(value, before.IsGood && after.IsGood ? kGood : kUncertain);
                }

                revertToStepped = true;
            }

            if (after == null)
            {
                return Bound.NoData;
            }

            // Uncertain_DataSubNormal if the value before is Uncertain, or if the value after is Bad or
            // Uncertain when sloped interpolation is used.
            return new Bound(before.Value, before.IsGood && !revertToStepped ? kGood : kUncertain);
        }

        /// <summary>
        /// DurationGood or DurationBad (5.4.3.31, 5.4.3.32) in milliseconds, and the width of the regions.
        /// </summary>
        private static double Duration(Row row, bool good, out double width)
        {
            Point first = row.Real.LastOrDefault(p => p.T <= row.Early);
            Point[] inside = row.Chronological;
            var edges = new List<(double Time, Point Point)> { (row.Early, first) };
            edges.AddRange(inside.Where(p => p.T > row.Early).Select(p => (p.T, p)));
            double total = 0;
            width = 0;

            for (int ii = 0; ii < edges.Count; ii++)
            {
                double end = ii + 1 < edges.Count ? edges[ii + 1].Time : row.Late;

                Point point = edges[ii].Point;
                bool counts = point == null ? !good : good ? point.IsGood : point.IsBad;
                width += end - edges[ii].Time;

                if (counts)
                {
                    total += end - edges[ii].Time;
                }
            }

            width = Math.Round(width * 1000, 6);
            return Math.Round(total * 1000, 6);
        }

        private static int Severity(uint code)
        {
            return StatusCode.IsBad(code) ? 2 : StatusCode.IsUncertain(code) ? 1 : 0;
        }

        private static bool IsOneOf(string value, params string[] values)
        {
            return Array.IndexOf(values, value) >= 0;
        }

        private static string Number(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static double ParseNumber(string value)
        {
            return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static bool SameValue(string actual, string expected)
        {
            if (string.Equals(actual, expected, StringComparison.Ordinal))
            {
                return true;
            }

            if (double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) &&
                double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
            {
                return Math.Abs(a - b) <= 1e-9 * Math.Max(1.0, Math.Abs(b));
            }

            return false;
        }
        #endregion

        #region Golden file
        private static string ReadGoldenText()
        {
            Assembly assembly = typeof(Part13Master378BehaviourTests).Assembly;

            using (Stream stream = assembly.GetManifestResourceStream(GoldenResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Embedded resource " + GoldenResourceName + " not found.");
                }

                using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
                using (var reader = new StreamReader(gzip, new UTF8Encoding(false)))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static Dictionary<string, Result> ParseGolden(string text)
        {
            var rows = new Dictionary<string, Result>(StringComparer.Ordinal);

            foreach (string line in text.Split('\n'))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                string[] fields = line.Split('\t');
                rows.Add(RowKey(fields[0], int.Parse(fields[1], CultureInfo.InvariantCulture)), Result.Parse(fields[2]));
            }

            return rows;
        }

        private static string RowKey(string key, int index)
        {
            return key + "#" + index.ToString(CultureInfo.InvariantCulture);
        }
        #endregion

        #region Model
        /// <summary>A processed value: "timestamp type value statuscode".</summary>
        private sealed class Result : IEquatable<Result>
        {
            private static readonly char[] s_space = [' '];

            public string Timestamp { get; private set; }
            public string Type { get; private set; }
            public string Value { get; private set; }
            public uint Code { get; private set; }
            public uint Bits { get; private set; }

            public static Result Parse(string text)
            {
                string[] f = text.Split(s_space, 4);
                uint status = Convert.ToUInt32(f[3], 16);
                return new Result { Timestamp = f[0], Type = f[1], Value = f[2], Code = status & 0xFFFF0000, Bits = status & 0xFFFF };
            }

            public bool Equals(Result other)
            {
                return other != null &&
                    Timestamp == other.Timestamp && Type == other.Type && Value == other.Value &&
                    Code == other.Code && Bits == other.Bits;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as Result);
            }

            public override int GetHashCode()
            {
                return StringComparer.Ordinal.GetHashCode(ToString());
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} 0x{3:X8}", Timestamp, Type, Value, Code | Bits);
            }
        }

        /// <summary>The expected result: the 1.5.378 result changed by the rules.</summary>
        private sealed class Expectation
        {
            public Expectation(Result master)
            {
                Timestamp = master.Timestamp;
                Type = master.Type;
                Value = master.Value;
                Code = master.Code;
                Bits = master.Bits;
            }

            public string Timestamp { get; set; }
            public string Type { get; set; }
            public string Value { get; set; }
            public uint Code { get; set; }
            public uint Bits { get; set; }
            public uint IgnoredBits { get; set; }
            public bool TypeOpen { get; set; }
            public bool ValueOpen { get; set; }
            public bool CodeOpen { get; set; }
            public bool BitsOpen { get; set; }
            public List<Func<Result, Expectation, bool>> Constraints { get; } = new List<Func<Result, Expectation, bool>>();

            /// <summary>Sets aggregate bits (with the DataValue info type).</summary>
            public void AddBits(uint bits)
            {
                Bits |= bits | kInfoTypeDataValue;
            }

            /// <summary>Expects another result.</summary>
            public void CopyFrom(Result result)
            {
                Timestamp = result.Timestamp;
                Type = result.Type;
                Value = result.Value;
                Code = result.Code;
                Bits = result.Bits;
            }

            public void SetNoData()
            {
                Code = kNoData;
                Type = "Null";
                Value = "null";
            }

            public List<string> Compare(Result actual)
            {
                var mismatches = new List<string>();

                if (Timestamp != actual.Timestamp)
                {
                    mismatches.Add("timestamp");
                }

                if (!TypeOpen && Type != actual.Type)
                {
                    mismatches.Add("type");
                }

                if (!ValueOpen && !SameValue(actual.Value, Value))
                {
                    mismatches.Add("value");
                }

                if (!CodeOpen && Code != actual.Code)
                {
                    mismatches.Add("code");
                }

                if (!BitsOpen && (Bits | IgnoredBits) != (actual.Bits | IgnoredBits))
                {
                    mismatches.Add("bits");
                }

                for (int ii = 0; ii < Constraints.Count; ii++)
                {
                    if (!Constraints[ii](actual, this))
                    {
                        mismatches.Add("constraint");
                    }
                }

                return mismatches;
            }

            public override string ToString()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "'{0} {1} {2} 0x{3:X8}'",
                    Timestamp,
                    TypeOpen ? "*" : Type,
                    ValueOpen ? "*" : Value,
                    Code | Bits);
            }
        }

        /// <summary>A raw value of a data set (seconds after 12:00:00).</summary>
        private sealed class Point
        {
            public Point(DataValue value)
            {
                T = (value.SourceTimestamp - s_start).TotalSeconds;
                Value = value.Value;
                Code = value.StatusCode.CodeBits;
            }

            public double T { get; }
            public object Value { get; }
            public uint Code { get; }
            public double Number => Convert.ToDouble(Value, CultureInfo.InvariantCulture);
            public bool IsGood => StatusCode.IsGood(Code);
            public bool IsUncertain => StatusCode.IsUncertain(Code);
            public bool IsBad => StatusCode.IsBad(Code);
        }

        /// <summary>A Simple Bounding Value.</summary>
        private sealed class Bound
        {
            public static readonly Bound NoData = new Bound(null, kNoData);

            public Bound(object value, uint code)
            {
                Value = value;
                Code = code;
            }

            public object Value { get; }
            public uint Code { get; }
        }

        /// <summary>The 1.5.378 and the current results.</summary>
        private sealed class Context
        {
            public Context(Dictionary<string, Result> master, Dictionary<string, Result> branch)
            {
                Master = master;
                Branch = branch;
            }

            public Dictionary<string, Result> Master { get; }
            public Dictionary<string, Result> Branch { get; }
        }

        /// <summary>One processed value with the data it was calculated from.</summary>
        private sealed class Row
        {
            private readonly Context m_context;
            private readonly MatrixResult m_result;

            public Row(Context context, MatrixResult result)
            {
                m_context = context;
                m_result = result;
                Master = context.Master[RowKey(result.Key, result.Index)];
                Branch = context.Branch[RowKey(result.Key, result.Index)];
                Aggregate = result.Aggregate;
                TreatUncertainAsBad = result.TreatUncertainAsBad;
                Reverse = result.Request == "rev16s";
                Early = (result.EarlyTime - s_start).TotalSeconds;
                Late = (result.LateTime - s_start).TotalSeconds;

                // placeholders (Bad_NoData) are not raw data.
                Real = result.DataSet.Raw
                    .Where(v => v.StatusCode.CodeBits != StatusCodes.BadNoData)
                    .Select(v => new Point(v))
                    .ToArray();
                StartOfData = Real[0].T;
                EndOfData = Real[Real.Length - 1].T;

                // the interval is [Early, Late) and (Early, Late] when time flows backward (5.4.2.2).
                Inside = Real.Where(p => Reverse ? p.T > Early && p.T <= Late : p.T >= Early && p.T < Late).ToArray();
                Chronological = Real.Where(p => p.T >= Early && p.T < Late).ToArray();
                IsInt32 = result.DataSet.Name.EndsWith("-Int32", StringComparison.Ordinal);
                IsStepped = result.DataSet.Stepped || Real.Any(p => p.Value is bool);
                AggregateConfiguration configuration = result.DataSet.Configuration;
                EqualPercentages = configuration.PercentDataGood + configuration.PercentDataBad == 100;
                UseSlopedExtrapolation = configuration.UseSlopedExtrapolation;
            }

            public Result Master { get; }
            public Result Branch { get; }
            public string Aggregate { get; }
            public bool TreatUncertainAsBad { get; }
            public bool Reverse { get; }
            public double Early { get; }
            public double Late { get; }
            public Point[] Real { get; }
            public Point[] Inside { get; }
            public Point[] Chronological { get; }
            public double StartOfData { get; }
            public double EndOfData { get; }
            public bool IsInt32 { get; }
            public bool IsStepped { get; }
            public bool EqualPercentages { get; }
            public bool UseSlopedExtrapolation { get; }

            /// <summary>The history holds Boolean values (Annex A Historian4).</summary>
            public bool HasBooleanValues => Real.Any(p => p.Value is bool);

            /// <summary>Historian3 has the raw data of Historian2 with Stepped = True.</summary>
            public bool HasSlopedTwin => m_result.DataSet.Name.StartsWith("Historian3", StringComparison.Ordinal);

            /// <summary>The Double history that an Int32 copy was made from.</summary>
            public string DoubleDataSet => m_result.DataSet.Name.Substring(0, m_result.DataSet.Name.Length - "-Int32".Length);

            /// <summary>The 1.5.378 result of the same request on another data set or aggregate.</summary>
            public Result MasterOf(string dataSet, string aggregate, bool treatUncertainAsBad)
            {
                return m_context.Master[RowKey(KeyOf(dataSet, aggregate, treatUncertainAsBad), m_result.Index)];
            }

            /// <summary>The current result of another aggregate for the same interval.</summary>
            public Result BranchOf(string aggregate)
            {
                return BranchOf(aggregate, TreatUncertainAsBad);
            }

            /// <summary>The current result of an aggregate for the same interval and TreatUncertainAsBad.</summary>
            public Result BranchOf(string aggregate, bool treatUncertainAsBad)
            {
                return m_context.Branch[RowKey(KeyOf(m_result.DataSet.Name, aggregate, treatUncertainAsBad), m_result.Index)];
            }

            private string KeyOf(string dataSet, string aggregate, bool treatUncertainAsBad)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}|{1}|TUAB={2}|{3}",
                    dataSet,
                    m_result.Request,
                    treatUncertainAsBad,
                    aggregate);
            }
        }

        /// <summary>A change from 1.5.378 with the Part 13 clause that requires it.</summary>
        private sealed class Rule
        {
            public Rule(string id, string reason, Func<Row, bool> applies, Action<Row, Expectation> apply)
            {
                Id = id;
                Reason = reason;
                Applies = applies;
                Apply = apply;
            }

            public string Id { get; }
            public string Reason { get; }
            public Func<Row, bool> Applies { get; }
            public Action<Row, Expectation> Apply { get; }
        }
        #endregion
    }
}
