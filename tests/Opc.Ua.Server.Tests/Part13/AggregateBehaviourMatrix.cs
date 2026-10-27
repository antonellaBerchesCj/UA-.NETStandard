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

namespace Opc.Ua.Server.Tests.Part13
{
    /// <summary>
    /// A raw data set of the behaviour matrix: an Annex A history or an Int32 copy of it.
    /// </summary>
    public sealed class MatrixDataSet
    {
        /// <summary>The data set name, e.g. "Historian1" or "Historian1-Int32".</summary>
        public string Name { get; set; }

        /// <summary>The raw values in chronological order.</summary>
        public List<DataValue> Raw { get; } = new List<DataValue>();

        /// <summary>The Stepped property of the variable.</summary>
        public bool Stepped { get; set; }

        /// <summary>The aggregate configuration of the data set.</summary>
        public AggregateConfiguration Configuration { get; set; }
    }

    /// <summary>
    /// One processed value of the behaviour matrix.
    /// </summary>
    public sealed class MatrixResult
    {
        /// <summary>Identifies the run: data set, request, configuration and aggregate.</summary>
        public string Key { get; set; }

        /// <summary>The index of the processed value in the run.</summary>
        public int Index { get; set; }

        /// <summary>The processed value as "timestamp type value statuscode".</summary>
        public string Result { get; set; }

        /// <summary>The data set of the run.</summary>
        public MatrixDataSet DataSet { get; set; }

        /// <summary>The aggregate name.</summary>
        public string Aggregate { get; set; }

        /// <summary>The request name (fwd5s, fwd16s, rev16s or whole).</summary>
        public string Request { get; set; }

        /// <summary>TreatUncertainAsBad of the run.</summary>
        public bool TreatUncertainAsBad { get; set; }

        /// <summary>The chronologically earlier time of the interval.</summary>
        public DateTime EarlyTime { get; set; }

        /// <summary>The chronologically later time of the interval.</summary>
        public DateTime LateTime { get; set; }
    }

    /// <summary>
    /// Runs every standard aggregate over the Annex A histories (and Int32 copies of the numeric
    /// ones) for a fixed set of requests. The same file is compiled against the 1.5.378 release
    /// branch to produce Master378AggregateResults.txt, so the results of both builds can be
    /// compared line by line. Uses only API that exists in both.
    /// </summary>
    public static class AggregateBehaviourMatrix
    {
        /// <summary>
        /// The standard aggregates in a fixed order.
        /// </summary>
        public static readonly (string Name, NodeId Id)[] Aggregates =
        [
            ("AnnotationCount", ObjectIds.AggregateFunction_AnnotationCount),
            ("Average", ObjectIds.AggregateFunction_Average),
            ("Count", ObjectIds.AggregateFunction_Count),
            ("Delta", ObjectIds.AggregateFunction_Delta),
            ("DeltaBounds", ObjectIds.AggregateFunction_DeltaBounds),
            ("DurationBad", ObjectIds.AggregateFunction_DurationBad),
            ("DurationGood", ObjectIds.AggregateFunction_DurationGood),
            ("DurationInStateNonZero", ObjectIds.AggregateFunction_DurationInStateNonZero),
            ("DurationInStateZero", ObjectIds.AggregateFunction_DurationInStateZero),
            ("End", ObjectIds.AggregateFunction_End),
            ("EndBound", ObjectIds.AggregateFunction_EndBound),
            ("Interpolative", ObjectIds.AggregateFunction_Interpolative),
            ("Maximum", ObjectIds.AggregateFunction_Maximum),
            ("Maximum2", ObjectIds.AggregateFunction_Maximum2),
            ("MaximumActualTime", ObjectIds.AggregateFunction_MaximumActualTime),
            ("MaximumActualTime2", ObjectIds.AggregateFunction_MaximumActualTime2),
            ("Minimum", ObjectIds.AggregateFunction_Minimum),
            ("Minimum2", ObjectIds.AggregateFunction_Minimum2),
            ("MinimumActualTime", ObjectIds.AggregateFunction_MinimumActualTime),
            ("MinimumActualTime2", ObjectIds.AggregateFunction_MinimumActualTime2),
            ("NumberOfTransitions", ObjectIds.AggregateFunction_NumberOfTransitions),
            ("PercentBad", ObjectIds.AggregateFunction_PercentBad),
            ("PercentGood", ObjectIds.AggregateFunction_PercentGood),
            ("Range", ObjectIds.AggregateFunction_Range),
            ("Range2", ObjectIds.AggregateFunction_Range2),
            ("StandardDeviationPopulation", ObjectIds.AggregateFunction_StandardDeviationPopulation),
            ("StandardDeviationSample", ObjectIds.AggregateFunction_StandardDeviationSample),
            ("Start", ObjectIds.AggregateFunction_Start),
            ("StartBound", ObjectIds.AggregateFunction_StartBound),
            ("TimeAverage", ObjectIds.AggregateFunction_TimeAverage),
            ("TimeAverage2", ObjectIds.AggregateFunction_TimeAverage2),
            ("Total", ObjectIds.AggregateFunction_Total),
            ("Total2", ObjectIds.AggregateFunction_Total2),
            ("VariancePopulation", ObjectIds.AggregateFunction_VariancePopulation),
            ("VarianceSample", ObjectIds.AggregateFunction_VarianceSample),
            ("WorstQuality", ObjectIds.AggregateFunction_WorstQuality),
            ("WorstQuality2", ObjectIds.AggregateFunction_WorstQuality2)
        ];

        private static readonly DateTime s_start = new DateTime(2011, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Loads the five Annex A histories and Int32 copies of the numeric ones.
        /// </summary>
        public static List<MatrixDataSet> LoadDataSets(TextReader annexACsv)
        {
            var sets = new List<MatrixDataSet>();
            string[] lines = annexACsv.ReadToEnd().Split('\n');
            DateTime day = s_start.Date;

            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd('\r');
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (line.StartsWith("Aggregate,", StringComparison.Ordinal))
                {
                    break;
                }

                if (!line.StartsWith("Historian", StringComparison.Ordinal) || line.Split(',').Length > 1)
                {
                    continue;
                }

                var set = new MatrixDataSet {
                    Name = line,
                    Configuration = new AggregateConfiguration { UseServerCapabilitiesDefaults = false }
                };
                int j = i + 1;

                for (; j < lines.Length && !lines[j].StartsWith("Timestamp,", StringComparison.Ordinal); j++)
                {
                    string[] kv = lines[j].Split(',');

                    if (kv.Length < 2)
                    {
                        continue;
                    }

                    string value = kv[1].Trim();

                    switch (kv[0].Trim())
                    {
                        case "Stepped": set.Stepped = bool.Parse(value); break;
                        case "Treat Uncertain as Bad": set.Configuration.TreatUncertainAsBad = bool.Parse(value); break;
                        case "Percent Bad": set.Configuration.PercentDataBad = byte.Parse(value, CultureInfo.InvariantCulture); break;
                        case "Percent Good": set.Configuration.PercentDataGood = byte.Parse(value, CultureInfo.InvariantCulture); break;
                        case "Use Sloped Extrapolation": set.Configuration.UseSlopedExtrapolation = bool.Parse(value); break;
                    }
                }

                for (j++; j < lines.Length && lines[j].Trim().Length > 0; j++)
                {
                    string[] f = lines[j].Split(',');

                    if (f[0].Length == 0)
                    {
                        continue;
                    }

                    if (!char.IsDigit(f[0][0]))
                    {
                        break;
                    }

                    DateTime timestamp = day + TimeSpan.Parse(f[0], CultureInfo.InvariantCulture);
                    StatusCode status = f[2].Trim('"') switch {
                        "Bad_NoData" => StatusCodes.BadNoData,
                        "Good" => StatusCodes.Good,
                        "Bad" => StatusCodes.Bad,
                        "Uncertain" => StatusCodes.Uncertain,
                        _ => throw new InvalidDataException(f[2])
                    };
                    Variant raw = f[1].Length == 0 || f[1] == "undefined" ? Variant.Null
                        : bool.TryParse(f[1], out bool b) ? new Variant(b)
                        : new Variant(double.Parse(f[1], CultureInfo.InvariantCulture));
                    set.Raw.Add(new DataValue(raw, status, timestamp, timestamp));
                }

                sets.Add(set);
                i = j;
            }

            // Int32 copies of the numeric histories exercise integer results and bounds.
            int count = sets.Count;

            for (int i = 0; i < count; i++)
            {
                MatrixDataSet source = sets[i];

                if (source.Raw.Find(v => v.Value is bool) != null)
                {
                    continue;
                }

                var copy = new MatrixDataSet {
                    Name = source.Name + "-Int32",
                    Stepped = source.Stepped,
                    Configuration = source.Configuration
                };

                foreach (DataValue value in source.Raw)
                {
                    Variant raw = value.Value is double d ? new Variant((int)d) : Variant.Null;
                    copy.Raw.Add(new DataValue(raw, value.StatusCode, value.SourceTimestamp, value.ServerTimestamp));
                }

                sets.Add(copy);
            }

            return sets;
        }

        /// <summary>
        /// Runs every aggregate on every data set, request and TreatUncertainAsBad setting.
        /// </summary>
        public static List<MatrixResult> Run(IEnumerable<MatrixDataSet> sets)
        {
            var results = new List<MatrixResult>();

            foreach (MatrixDataSet set in sets)
            {
                foreach ((string request, DateTime start, DateTime end, double interval) in Requests())
                {
                    foreach (bool treatUncertainAsBad in new[] { set.Configuration.TreatUncertainAsBad, !set.Configuration.TreatUncertainAsBad })
                    {
                        var configuration = new AggregateConfiguration {
                            UseServerCapabilitiesDefaults = false,
                            TreatUncertainAsBad = treatUncertainAsBad,
                            PercentDataBad = set.Configuration.PercentDataBad,
                            PercentDataGood = set.Configuration.PercentDataGood,
                            UseSlopedExtrapolation = set.Configuration.UseSlopedExtrapolation
                        };

                        foreach ((string name, NodeId id) in Aggregates)
                        {
                            string key = string.Format(
                                CultureInfo.InvariantCulture,
                                "{0}|{1}|TUAB={2}|{3}",
                                set.Name,
                                request,
                                treatUncertainAsBad,
                                name);
                            List<string> values = Process(id, set, start, end, interval, configuration);

                            for (int ii = 0; ii < values.Count; ii++)
                            {
                                GetInterval(start, end, interval, ii, out DateTime early, out DateTime late);
                                results.Add(new MatrixResult {
                                    Key = key,
                                    Index = ii,
                                    Result = values[ii],
                                    DataSet = set,
                                    Aggregate = name,
                                    Request = request,
                                    TreatUncertainAsBad = treatUncertainAsBad,
                                    EarlyTime = early,
                                    LateTime = late
                                });
                            }
                        }
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// The requests: 5 s and 16 s intervals forward, 16 s backward, one interval for the range.
        /// </summary>
        private static IEnumerable<(string Name, DateTime Start, DateTime End, double Interval)> Requests()
        {
            DateTime end = s_start.AddSeconds(100);
            yield return ("fwd5s", s_start, end, 5000);
            yield return ("fwd16s", s_start, end, 16000);
            yield return ("rev16s", end, s_start, 16000);
            yield return ("whole", s_start, end, 0);
        }

        private static void GetInterval(DateTime start, DateTime end, double interval, int index, out DateTime early, out DateTime late)
        {
            if (interval == 0)
            {
                early = start < end ? start : end;
                late = start < end ? end : start;
                return;
            }

            if (start < end)
            {
                early = start.AddMilliseconds(index * interval);
                late = early.AddMilliseconds(interval);
                late = late > end ? end : late;
            }
            else
            {
                late = start.AddMilliseconds(-index * interval);
                early = late.AddMilliseconds(-interval);
                early = early < end ? end : early;
            }
        }

        private static List<string> Process(
            NodeId aggregateId,
            MatrixDataSet set,
            DateTime start,
            DateTime end,
            double interval,
            AggregateConfiguration configuration)
        {
            var results = new List<string>();
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                aggregateId,
                start,
                end,
                interval,
                set.Stepped,
                configuration,
                null);

            IList<DataValue> raw = set.Raw;

            if (end < start)
            {
                var reversed = new List<DataValue>(raw);
                reversed.Reverse();
                raw = reversed;
            }

            foreach (DataValue value in raw)
            {
                calculator.QueueRawValue(new DataValue(value.WrappedValue, value.StatusCode, value.SourceTimestamp, value.ServerTimestamp));
            }

            for (int guard = 0; guard < 1000; guard++)
            {
                DataValue processed = calculator.GetProcessedValue(true);

                if (processed == null)
                {
                    break;
                }

                results.Add(Format(processed));
            }

            return results;
        }

        /// <summary>
        /// Formats a processed value as "timestamp type value statuscode".
        /// </summary>
        public static string Format(DataValue value)
        {
            string type = value.WrappedValue.TypeInfo?.BuiltInType.ToString() ?? "Null";
            string text;

            if (value.WrappedValue.IsNull)
            {
                text = "null";
            }
            else if (value.Value is double d)
            {
                text = d.ToString("R", CultureInfo.InvariantCulture);
            }
            else if (value.Value is float f)
            {
                text = f.ToString("R", CultureInfo.InvariantCulture);
            }
            else if (value.Value is StatusCode statusCode)
            {
                text = string.Format(CultureInfo.InvariantCulture, "0x{0:X8}", statusCode.Code);
            }
            else
            {
                text = Convert.ToString(value.Value, CultureInfo.InvariantCulture);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:HH:mm:ss.fff} {1} {2} 0x{3:X8}",
                value.SourceTimestamp,
                type,
                text,
                value.StatusCode.Code);
        }
    }
}
