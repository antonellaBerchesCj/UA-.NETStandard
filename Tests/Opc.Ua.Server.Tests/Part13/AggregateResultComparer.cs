/* ========================================================================
 * OPC UA Part 13 (OPC 10000-13 v1.05.07) aggregate conformance tests.
 *
 * Runs aggregate calculators and compares processed values with expected rows.
 * The comparison checks everything the CTT compares (value, StatusCode including the
 * aggregate bits, SourceTimestamp) and reports every field that differs.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Opc.Ua.Server.Tests.Part13
{
    /// <summary>
    /// Feeds raw values to a calculator created by the stack's aggregate factory.
    /// </summary>
    public static class AggregateCalculatorRunner
    {
        /// <summary>
        /// Runs the standard calculator for the request. The raw values must be in
        /// chronological order; they are queued in request order (newest first when
        /// EndTime &lt; StartTime), as a server does for a backward read.
        /// </summary>
        public static List<DataValue> Run(
            NodeId aggregateId,
            IList<DataValue> rawChronological,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration)
        {
            return Run(aggregateId, rawChronological, startTime, endTime, processingInterval, stepped, configuration, null);
        }

        /// <summary>
        /// Runs the standard calculator for the request with the timestamps of the variable's
        /// Annotations (AnnotationCount, Part 13 §5.4.3.20).
        /// </summary>
        public static List<DataValue> Run(
            NodeId aggregateId,
            IList<DataValue> rawChronological,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration,
            IEnumerable<DateTime> annotations)
        {
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                aggregateId,
                startTime,
                endTime,
                processingInterval,
                stepped,
                configuration,
                null);

            if (calculator == null)
            {
                throw new NotSupportedException("No standard calculator for " + aggregateId);
            }

            if (annotations != null)
            {
                foreach (DateTime annotation in annotations)
                {
                    ((AggregateCalculator)calculator).QueueAnnotation(annotation);
                }
            }

            IEnumerable<DataValue> ordered = endTime < startTime
                ? rawChronological.Reverse()
                : rawChronological;

            foreach (DataValue raw in ordered)
            {
                calculator.QueueRawValue(new DataValue(raw.WrappedValue, raw.StatusCode, raw.SourceTimestamp, raw.ServerTimestamp));
            }

            List<DataValue> results = new List<DataValue>();

            for (int guard = 0; guard < 100000; guard++)
            {
                DataValue value = calculator.GetProcessedValue(true);

                if (value == null)
                {
                    break;
                }

                results.Add(value);
            }

            return results;
        }
    }

    /// <summary>
    /// The comparison of one processed value.
    /// </summary>
    public sealed class Part13RowResult
    {
        /// <summary>The row index (request order).</summary>
        public int Index { get; set; }

        /// <summary>The expected row, null if the server returned an extra value.</summary>
        public AnnexAExpectedValue Expected { get; set; }

        /// <summary>The actual value, null if the server returned too few values.</summary>
        public DataValue Actual { get; set; }

        /// <summary>The CSV row if the expectation was replaced by a normative deviation.</summary>
        public AnnexAExpectedValue AnnexARow { get; set; }

        /// <summary>The deviation that replaced the CSV row, if any.</summary>
        public Part13Deviation Deviation { get; set; }

        /// <summary>True if the SourceTimestamp matches.</summary>
        public bool TimestampMatches { get; set; }

        /// <summary>True if the StatusCode code bits match.</summary>
        public bool CodeMatches { get; set; }

        /// <summary>True if the aggregate bits match (data location, Partial, MultipleValues).</summary>
        public bool BitsMatches { get; set; }

        /// <summary>True if the value matches.</summary>
        public bool ValueMatches { get; set; }

        /// <summary>Explains a value mismatch.</summary>
        public string ValueReason { get; set; }

        /// <summary>True if every field matches.</summary>
        public bool Matches
        {
            get { return Expected != null && Actual != null && TimestampMatches && CodeMatches && BitsMatches && ValueMatches; }
        }

        /// <summary>The fields that differ, e.g. "status,bits".</summary>
        public string DifferingFields
        {
            get
            {
                if (Expected == null) { return "extra value"; }
                if (Actual == null) { return "missing value"; }
                List<string> fields = new List<string>();
                if (!TimestampMatches) { fields.Add("timestamp"); }
                if (!ValueMatches) { fields.Add("value"); }
                if (!CodeMatches) { fields.Add("status"); }
                if (!BitsMatches) { fields.Add("bits"); }
                return string.Join("+", fields);
            }
        }
    }

    /// <summary>
    /// Compares processed values with expected rows.
    /// </summary>
    public static class AggregateResultComparer
    {
        /// <summary>The aggregate bits that are compared.</summary>
        public const AggregateBits ComparedBits =
            AggregateBits.DataSourceMask | AggregateBits.Partial | AggregateBits.MultipleValues;

        /// <summary>
        /// Compares the actual values (request order) with the expected rows.
        /// </summary>
        public static List<Part13RowResult> Compare(
            IList<AnnexAExpectedValue> expected,
            IList<DataValue> actual,
            DateTime baseDate)
        {
            List<Part13RowResult> results = new List<Part13RowResult>();
            int count = Math.Max(expected.Count, actual.Count);

            for (int ii = 0; ii < count; ii++)
            {
                Part13RowResult row = new Part13RowResult {
                    Index = ii,
                    Expected = ii < expected.Count ? expected[ii] : null,
                    Actual = ii < actual.Count ? actual[ii] : null
                };

                if (row.Expected != null && row.Actual != null)
                {
                    StatusCode expectedStatus = row.Expected.StatusCode;
                    row.TimestampMatches = row.Actual.SourceTimestamp == baseDate + row.Expected.Time;
                    row.CodeMatches = row.Actual.StatusCode.CodeBits == expectedStatus.CodeBits;
                    row.BitsMatches = (row.Actual.StatusCode.AggregateBits & ComparedBits) == (expectedStatus.AggregateBits & ComparedBits);
                    string reason;
                    row.ValueMatches = ValueMatches(row.Expected.ValueText, row.Actual, out reason);
                    row.ValueReason = reason;
                }

                results.Add(row);
            }

            return results;
        }

        /// <summary>
        /// Compares a value. Numbers use the precision written in the expected text
        /// (half a unit of the last digit, at least 0.0005).
        /// </summary>
        public static bool ValueMatches(string expectedText, DataValue actual, out string reason)
        {
            reason = null;
            string text = (expectedText ?? string.Empty).Trim();
            object value = actual.Value;

            if (text.Length == 0)
            {
                if (value == null)
                {
                    return true;
                }

                reason = "expected no value";
                return false;
            }

            if (value == null)
            {
                reason = "no value returned";
                return false;
            }

            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
            {
                bool expectedBool = bool.Parse(text);
                if (value is bool actualBool)
                {
                    return actualBool == expectedBool;
                }

                reason = "expected Boolean, got " + value.GetType().Name;
                return false;
            }

            double expectedNumber;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out expectedNumber))
            {
                if (value is StatusCode || !(value is IConvertible))
                {
                    reason = "expected a number, got " + value.GetType().Name;
                    return false;
                }

                double actualNumber = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                int dot = text.IndexOf('.');
                int decimals = dot < 0 ? 0 : text.Length - dot - 1;
                double tolerance = Math.Max(0.5 * Math.Pow(10, -decimals), 0.0005);

                if (decimals == 0)
                {
                    tolerance = 0.0005;
                }

                if (Math.Abs(actualNumber - expectedNumber) <= tolerance + 1e-9)
                {
                    return true;
                }

                reason = string.Format(CultureInfo.InvariantCulture, "|{0} - {1}| > {2}", actualNumber, expectedNumber, tolerance);
                return false;
            }

            // a StatusCode name (WorstQuality, WorstQuality2).
            StatusCode expectedCode = AnnexAExamples.ParseStatusCodeName(text);

            if (value is StatusCode actualCode)
            {
                if (actualCode.CodeBits == expectedCode.CodeBits)
                {
                    return true;
                }

                reason = "status value " + FormatCode(actualCode);
                return false;
            }

            reason = "expected a StatusCode, got " + value.GetType().Name;
            return false;
        }

        /// <summary>Formats a StatusCode the way the CSV does ("Good, Calculated, Partial").</summary>
        public static string FormatStatus(StatusCode code)
        {
            StringBuilder buffer = new StringBuilder(FormatCode(code));
            AggregateBits bits = code.AggregateBits;

            if ((bits & AggregateBits.DataSourceMask) == AggregateBits.Calculated) { buffer.Append(", Calculated"); }
            if ((bits & AggregateBits.DataSourceMask) == AggregateBits.Interpolated) { buffer.Append(", Interpolated"); }
            if ((bits & AggregateBits.DataSourceMask) == AggregateBits.DataSourceMask) { buffer.Append(", Calculated+Interpolated(invalid)"); }
            if ((bits & AggregateBits.Partial) != 0) { buffer.Append(", Partial"); }
            if ((bits & AggregateBits.MultipleValues) != 0) { buffer.Append(", MultipleValues"); }
            if ((bits & AggregateBits.ExtraData) != 0) { buffer.Append(", ExtraData"); }

            return buffer.ToString();
        }

        /// <summary>Formats the code bits of a StatusCode by symbolic name.</summary>
        public static string FormatCode(StatusCode code)
        {
            string name = StatusCodes.GetBrowseName(code.CodeBits);

            if (string.IsNullOrEmpty(name))
            {
                name = string.Format(CultureInfo.InvariantCulture, "0x{0:X8}", code.CodeBits);
            }

            return name;
        }

        /// <summary>Formats a value for reports.</summary>
        public static string FormatValue(DataValue value)
        {
            if (value == null || value.Value == null)
            {
                return string.Empty;
            }

            if (value.Value is StatusCode code)
            {
                return FormatCode(code);
            }

            if (value.Value is bool flag)
            {
                return flag ? "true" : "false";
            }

            if (value.Value is double number)
            {
                return number.ToString("R", CultureInfo.InvariantCulture);
            }

            if (value.Value is float single)
            {
                return single.ToString("R", CultureInfo.InvariantCulture);
            }

            if (value.Value is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }

            return value.Value.ToString();
        }

        /// <summary>Formats an actual value as a CSV-like row.</summary>
        public static string FormatActual(DataValue value, DateTime baseDate)
        {
            if (value == null)
            {
                return "(none)";
            }

            TimeSpan time = value.SourceTimestamp - baseDate;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},\"{2}\"",
                FormatTime(time),
                FormatValue(value),
                FormatStatus(value.StatusCode));
        }

        /// <summary>Formats a time of day.</summary>
        public static string FormatTime(TimeSpan time)
        {
            if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
            {
                return time.ToString();
            }

            return time.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Builds a readable diff table. Rows that match are marked with a space, rows that differ
        /// with '!' and the differing fields.
        /// </summary>
        public static string FormatDiff(IEnumerable<Part13RowResult> rows, DateTime baseDate)
        {
            StringBuilder buffer = new StringBuilder();
            buffer.AppendLine(
                "   # | expected (Part 13)                                         " +
                "| actual                                                     | differs");

            foreach (Part13RowResult row in rows)
            {
                string expected = row.Expected == null ? "(none)" : row.Expected.ToString();
                string actual = FormatActual(row.Actual, baseDate);
                string marker = row.Matches ? " " : "!";
                string fields = row.Matches ? string.Empty : row.DifferingFields;

                if (!row.Matches && row.ValueReason != null)
                {
                    fields += " (" + row.ValueReason + ")";
                }

                if (row.Deviation != null)
                {
                    fields += " [normative override " + row.Deviation.Id + "; Annex A CSV: " + row.AnnexARow + "]";
                }

                buffer.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{0}{1,3} | {2,-58} | {3,-58} | {4}",
                    marker,
                    row.Index,
                    expected,
                    actual,
                    fields);
                buffer.AppendLine();
            }

            return buffer.ToString();
        }
    }
}
