/* ========================================================================
 * OPC UA Part 13 (OPC 10000-13 v1.05.07) aggregate conformance tests.
 *
 * Annex A is informative. Where an example row of AggregateExamples.csv contradicts the
 * normative text of Part 13, the tests expect the normative behaviour. Every such row is
 * listed here with the clause it is based on, so the list doubles as the analysis for the
 * CTT team ("the certification tests are always against the specification").
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

// One deviation per entry and the full reason text keep the table readable (Roslynator line length).
#pragma warning disable RCS0056

namespace Opc.Ua.Server.Tests.Part13
{
    /// <summary>
    /// Why an Annex A row is not used as the expectation.
    /// </summary>
    public enum Part13DeviationKind
    {
        /// <summary>The CSV row contradicts an explicit normative sentence.</summary>
        AnnexAContradictsNormativeText,

        /// <summary>
        /// The normative text allows more than one result; the test accepts the server's choice
        /// and documents it (a clarification request is open or should be filed).
        /// </summary>
        SpecificationAmbiguous
    }

    /// <summary>
    /// Replaces the expectation of one Annex A row with the normative one.
    /// </summary>
    public sealed class Part13Deviation
    {
        /// <summary>A stable id used in reports and in the CTT analysis.</summary>
        public string Id { get; set; }

        /// <summary>The kind of deviation.</summary>
        public Part13DeviationKind Kind { get; set; }

        /// <summary>The aggregate BrowseName.</summary>
        public string Aggregate { get; set; }

        /// <summary>The data set name.</summary>
        public string DataSet { get; set; }

        /// <summary>The timestamp of the CSV row (time of day, "hh:mm:ss.fff").</summary>
        public string RowTime { get; set; }

        /// <summary>The expected value text; null keeps the CSV value.</summary>
        public string ValueText { get; set; }

        /// <summary>The expected status text; null keeps the CSV status.</summary>
        public string StatusText { get; set; }

        /// <summary>The expected timestamp ("hh:mm:ss.fff"); null keeps the CSV timestamp.</summary>
        public string Timestamp { get; set; }

        /// <summary>The Part 13 clause(s) the expectation is based on.</summary>
        public string Clause { get; set; }

        /// <summary>A short explanation.</summary>
        public string Reason { get; set; }

        /// <summary>The related Mantis issue(s), if any.</summary>
        public string Mantis { get; set; }

        /// <summary>True if the deviation applies to the row.</summary>
        public bool AppliesTo(AnnexAExampleTable table, AnnexAExpectedValue row)
        {
            return string.Equals(Aggregate, table.AggregateName, StringComparison.Ordinal) &&
                   string.Equals(DataSet, table.DataSetName, StringComparison.Ordinal) &&
                   AnnexAExamples.ParseTime(RowTime) == row.Time;
        }

        /// <summary>Applies the deviation to a copy of the row.</summary>
        public AnnexAExpectedValue Apply(AnnexAExpectedValue row)
        {
            AnnexAExpectedValue copy = row.Clone();

            if (ValueText != null)
            {
                copy.ValueText = ValueText;
            }

            if (StatusText != null)
            {
                copy.StatusText = StatusText;
            }

            if (Timestamp != null)
            {
                copy.Time = AnnexAExamples.ParseTime(Timestamp);
            }

            return copy;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} ({1} {2} {3}; {4})", Id, Aggregate, DataSet, RowTime, Clause);
        }
    }

    /// <summary>
    /// An expected row after the normative deviations are applied.
    /// </summary>
    public sealed class Part13ExpectedRow
    {
        /// <summary>The expectation used by the test.</summary>
        public AnnexAExpectedValue Expected { get; set; }

        /// <summary>The CSV row.</summary>
        public AnnexAExpectedValue AnnexARow { get; set; }

        /// <summary>The deviation, null if the CSV row is used unchanged.</summary>
        public Part13Deviation Deviation { get; set; }
    }

    /// <summary>
    /// The list of Annex A rows that the tests replace with the normative expectation.
    /// </summary>
    public static class Part13Deviations
    {
        private const string R_M11426_MINMAX =
            "Mantis 11426 answer (~0025852, OPC Classic semantics): Minimum, Maximum, MinimumActualTime, MaximumActualTime and Range look at Uncertain values as if they were Good, whatever TreatUncertainAsBad is. The result is Uncertain_DataSubNormal if Bad values exist in the interval or the selected extremum is Uncertain, and Bad_NoData only if all values are Bad; Minimum, Maximum and Range are Calculated; the ActualTime variants keep Raw unless the status is Uncertain because of non-Good values that carry a value (an Uncertain extremum or a rejected Bad sample with a value), then they are Calculated, as the CTT expects. [12:01:04, 12:01:20) contains the Uncertain value 70 (Historian1 at 12:01:10, Historian2/3/5 at 12:01:17). Annex A ignores it (Bad_NoData for Historian1, Uncertain from TreatUncertainAsBad for Historian2/3, Good maximum 30 for Historian5).";

        private const string R_M11462_BOUND_UNCERTAIN =
            "Mantis 11462 answer: TreatUncertainAsBad never applies to bounding values (3.1.8, 3.1.9). Bounds are built from the non-Bad raw values around the timestamp; an Uncertain neighbour makes the bound Uncertain_DataSubNormal. Historian2/3 (TreatUncertainAsBad = True): the Uncertain value 70 at 12:01:17 is a bound candidate, so the bound at 12:01:15 is 66 (sloped 60 -> 70) and the bound at 12:01:20 is 70 (sloped 70 -> 70 or stepped 70), both Uncertain_DataSubNormal. Annex A skips the Uncertain value (or returns Bad_NoData) because it applies TreatUncertainAsBad to the bound.";

        private const string R_S13_NOT_UNCERTAIN =
            "NumberOfTransitions (5.4.3.24) compares non-Bad values and Table 72 says \"Bound Uncertain: Use as value\", so an Uncertain value is counted and can be the previous value whatever TreatUncertainAsBad is. This follows the principle of the Mantis 11425 and 11426 answers (the aggregate definition wins over TreatUncertainAsBad), as the OPC Foundation stack implements it (UA-.NETStandard #4702). Annex A skips the Uncertain value at 12:01:17 because TreatUncertainAsBad = True.";

        private const string R_A13_WQ2_STARTBOUND =
            "\"The start bound calculated using Simple Bounding Values (see 3.1.9) is always included when determining the worst quality.\" Only the start bound is named and the interval is [Start, End) (5.4.2.2 Table 49). The Annex A row can only be reproduced by also including the end bound.";

        private const string R_A13_WQ2_IGNORE_TUAB =
            "\"The server shall ignore the TreatUncertainAsBad for this aggregate.\" The raw value before the start time (12:01:17) is Uncertain, so the simple start bound is Uncertain_DataSubNormal (3.1.9: \"If the value before the timestamp is Uncertain the StatusCode is Uncertain_DataSubNormal\"). It becomes Bad_NoData only if TreatUncertainAsBad = True is applied to the bound, which 5.4.3.36 excludes; the end bound (also used by Annex A) is not part of the aggregate.";

        private const string R_A13_WQ_MULTIPLEVALUES =
            "\"If multiple values exist with the worst quality the MultipleValues bit is set.\" Table 48: \"multiple worst quality at different timestamps within the same ProcessingInterval\". The interval holds several values whose (worst) quality is Good. Annex A sets the bit only when the worst quality is Uncertain or Bad; if that is the intent, the sentence needs to say so.";

        private const string R_A13_TA_AFTEREND =
            "TimeAverage and Total, \"After End of Data: Value extrapolated, Uncertain status\" and \"No End Bound: Extrapolate data, Uncertain status\". 5.3.3.2 defers to these tables (\"See the Aggregate specific characteristics for more details\"). Annex A returns Bad_NoData; its own Interpolative table does extrapolate after the end of data.";

        private const string R_S13_TA_PARTIAL_EXTRAPOLATION =
            "\"For those Aggregates with extrapolation, the Partial bit may be set.\" The last interval with data is extrapolated to its end; Annex A sets Partial, the server does not. Both are allowed.";

        private const string R_A13_NOT_CALCULATED =
            "NumberOfTransitions, Calculated: \"Set Always\". The interval contains only the Bad value at 12:00:40 (100 % Bad, PercentValues), the status is Bad with the Calculated bit. Annex A omits the bit.";

        private const string R_A13_DURATION_FIRSTREGION =
            "\"The status of the first region is determined by finding the first data point at or before the start of the interval.\" Historian1 [12:00:32, 12:00:48): the data point before is Good 30 at 12:00:30, so [12:00:32, 12:00:40) is Good (8 s). Historian1 [12:01:04, 12:01:20): the data point before is Good 60 at 12:01:00, so [12:01:04, 12:01:10) is Good (6 s); the Uncertain value at 12:01:10 counts for neither (Mantis 11425 ~0025847). (Historian3/4 [12:01:20, 12:01:36): the data point before is Uncertain (12:01:17), so the first 3 s count as neither Good nor Bad; Annex A is right there, Mantis 11425 ~0025847.) Annex A uses the status of the simple bound instead (Table 79/80 \"Uses status of bounding value\"), which also depends on the value after the start time.";

        private const string R_A13_DURATION_UNCERTAIN =
            "Mantis 11425 answer (~0025847): DurationGood counts only Good regions and DurationBad only Bad regions; an Uncertain region counts for neither and TreatUncertainAsBad does not apply (5.4.3.31-5.4.3.34). Annex A counts the Uncertain region that starts at 12:01:17 as Bad because TreatUncertainAsBad = True (Historian2 [12:01:04, 12:01:20) and [12:01:20, 12:01:36), Historian3/4 [12:01:04, 12:01:20)), so DurationBad and PercentBad are 0 there.";

        private const string R_S13_UNCERTAIN_AS_GOOD =
            "TreatUncertainAsBad = False: 4.2.1.2 makes an Uncertain value \"equivalent to Good, unless the Aggregate definition says otherwise\" and 5.4.3.2.1 counts Uncertain values/regions as Good. Average, Delta, NumberOfTransitions, StandardDeviation* and Variance* therefore include the Uncertain value (Historian1 70 at 12:01:10, Historian5 70 at 12:01:17) and the status is Good, as the OPC Foundation stack (UA-.NETStandard #4655) and the patched CTT 1.05.513 Aggregate 003/004 tests do. Annex A excludes the Uncertain value and reports Uncertain_DataSubNormal or Bad_NoData. Clarification needed; the test accepts the server reading. Count follows the Mantis 11495 answer (12.10.2026): only Good values are counted and the Uncertain value makes the status Uncertain_DataSubNormal, as in Annex A, so Count is no longer a deviation.";

        /// <summary>
        /// All deviations. Each entry quotes the normative sentence it relies on. The rows were
        /// found with an implementation of the normative text that is independent of the stack and
        /// cross-checked against the Mantis analyses (11422, 11425, 11426, 11427, 11462, 11466).
        /// Kind = SpecificationAmbiguous rows accept the server reading of an unclear clause.
        /// </summary>
        public static readonly IReadOnlyList<Part13Deviation> All = new List<Part13Deviation> {
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "350", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.8", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-UNCERTAIN-AS-GOOD", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "Average", DataSet = "Historian1", RowTime = "12:01:10.000", ValueText = "70", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 4.2.1.2, 5.4.3.5", Reason = R_S13_UNCERTAIN_AS_GOOD
            },
            new Part13Deviation {
                Id = "S13-UNCERTAIN-AS-GOOD", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "Average", DataSet = "Historian5", RowTime = "12:01:15.000", ValueText = "70", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 4.2.1.2, 5.4.3.5", Reason = R_S13_UNCERTAIN_AS_GOOD
            },
            new Part13Deviation {
                Id = "S13-NOT-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11425, 11426",
                Aggregate = "NumberOfTransitions", DataSet = "Historian2", RowTime = "12:01:04.000", ValueText = "2", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.24, Table 72, 4.2.1.2", Reason = R_S13_NOT_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-NOT-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11425, 11426",
                Aggregate = "NumberOfTransitions", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "2", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.24, Table 72, 4.2.1.2", Reason = R_S13_NOT_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-NOT-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11425, 11426",
                Aggregate = "NumberOfTransitions", DataSet = "Historian3", RowTime = "12:01:04.000", ValueText = "2", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.24, Table 72, 4.2.1.2", Reason = R_S13_NOT_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-NOT-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11425, 11426",
                Aggregate = "NumberOfTransitions", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "2", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.24, Table 72, 4.2.1.2", Reason = R_S13_NOT_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-NOT-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11425, 11426",
                Aggregate = "NumberOfTransitions", DataSet = "Historian4", RowTime = "12:01:04.000", ValueText = "1", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.24, Table 72, 4.2.1.2", Reason = R_S13_NOT_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-NOT-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11425, 11426",
                Aggregate = "NumberOfTransitions", DataSet = "Historian4", RowTime = "12:01:20.000", ValueText = "2", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.24, Table 72, 4.2.1.2", Reason = R_S13_NOT_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "Minimum", DataSet = "Historian1", RowTime = "12:01:04.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "Maximum", DataSet = "Historian1", RowTime = "12:01:04.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "Range", DataSet = "Historian1", RowTime = "12:01:04.000", ValueText = "0", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "MinimumActualTime", DataSet = "Historian1", RowTime = "12:01:04.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated", Timestamp = "12:01:10.000",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "MaximumActualTime", DataSet = "Historian1", RowTime = "12:01:04.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated", Timestamp = "12:01:10.000",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "Minimum", DataSet = "Historian2", RowTime = "12:01:04.000", ValueText = "60", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "Maximum", DataSet = "Historian2", RowTime = "12:01:04.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "Range", DataSet = "Historian2", RowTime = "12:01:04.000", ValueText = "10", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "MinimumActualTime", DataSet = "Historian2", RowTime = "12:01:12.000", ValueText = "60", StatusText = "Good",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "MaximumActualTime", DataSet = "Historian2", RowTime = "12:01:12.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated", Timestamp = "12:01:17.000",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "Minimum", DataSet = "Historian3", RowTime = "12:01:04.000", ValueText = "60", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "Maximum", DataSet = "Historian3", RowTime = "12:01:04.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "Range", DataSet = "Historian3", RowTime = "12:01:04.000", ValueText = "10", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "MinimumActualTime", DataSet = "Historian3", RowTime = "12:01:12.000", ValueText = "60", StatusText = "Good",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "MaximumActualTime", DataSet = "Historian3", RowTime = "12:01:12.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated", Timestamp = "12:01:17.000",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "Maximum", DataSet = "Historian5", RowTime = "12:01:04.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "Range", DataSet = "Historian5", RowTime = "12:01:04.000", ValueText = "40", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11426-MINMAX-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11426",
                Aggregate = "MaximumActualTime", DataSet = "Historian5", RowTime = "12:01:12.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated", Timestamp = "12:01:17.000",
                Clause = "OPC 10000-13 5.4.3.10-5.4.3.14", Reason = R_M11426_MINMAX
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "StartBound", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Interpolated, Partial",
                Clause = "OPC 10000-13 3.1.9, StartBound", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "EndBound", DataSet = "Historian2", RowTime = "12:01:04.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 3.1.9, EndBound", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "StartBound", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Interpolated, Partial",
                Clause = "OPC 10000-13 3.1.9, StartBound", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "EndBound", DataSet = "Historian3", RowTime = "12:01:04.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 3.1.9, EndBound", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "DeltaBounds", DataSet = "Historian2", RowTime = "12:01:04.000", ValueText = "14", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 3.1.9, DeltaBounds", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "DeltaBounds", DataSet = "Historian3", RowTime = "12:01:04.000", ValueText = "20", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 3.1.9, DeltaBounds", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "Interpolative", DataSet = "Historian2", RowTime = "12:01:15.000", ValueText = "66", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, Interpolative", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "Interpolative", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "70", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, Interpolative", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "Interpolative", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "70", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, Interpolative", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "TimeAverage", DataSet = "Historian2", RowTime = "12:01:10.000", ValueText = "61.6", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.6", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "TimeAverage", DataSet = "Historian2", RowTime = "12:01:15.000", ValueText = "68", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.6", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "TimeAverage", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "71.333", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.6", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "TimeAverage", DataSet = "Historian3", RowTime = "12:01:10.000", ValueText = "61.6", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.6", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "TimeAverage", DataSet = "Historian3", RowTime = "12:01:15.000", ValueText = "68", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.6", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "TimeAverage", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "71.333", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.6", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "Total", DataSet = "Historian2", RowTime = "12:01:10.000", ValueText = "308", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.8", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "Total", DataSet = "Historian2", RowTime = "12:01:15.000", ValueText = "340", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.8", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11462",
                Aggregate = "Total", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "356.667", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.8", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "A13-WQ2-STARTBOUND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11422",
                Aggregate = "WorstQuality2", DataSet = "Historian1", RowTime = "12:00:16.000", ValueText = "Good", StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.36", Reason = R_A13_WQ2_STARTBOUND
            },
            new Part13Deviation {
                Id = "A13-WQ2-STARTBOUND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11422",
                Aggregate = "WorstQuality2", DataSet = "Historian1", RowTime = "12:00:32.000", ValueText = null, StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.36", Reason = R_A13_WQ2_STARTBOUND
            },
            new Part13Deviation {
                Id = "A13-WQ2-STARTBOUND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11422",
                Aggregate = "WorstQuality2", DataSet = "Historian1", RowTime = "12:01:20.000", ValueText = "Good", StatusText = "Good, Calculated, Partial, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.36", Reason = R_A13_WQ2_STARTBOUND
            },
            new Part13Deviation {
                Id = "A13-WQ2-STARTBOUND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11422",
                Aggregate = "WorstQuality2", DataSet = "Historian2", RowTime = "12:01:04.000", ValueText = "Uncertain", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.36", Reason = R_A13_WQ2_STARTBOUND
            },
            new Part13Deviation {
                Id = "A13-WQ2-IGNORE-TUAB", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11422, 11462",
                Aggregate = "WorstQuality2", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "UncertainDataSubNormal", StatusText = "Good, Calculated, Partial",
                Clause = "OPC 10000-13 5.4.3.36, 3.1.9", Reason = R_A13_WQ2_IGNORE_TUAB
            },
            new Part13Deviation {
                Id = "A13-WQ2-STARTBOUND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11422",
                Aggregate = "WorstQuality2", DataSet = "Historian3", RowTime = "12:01:04.000", ValueText = null, StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.36", Reason = R_A13_WQ2_STARTBOUND
            },
            new Part13Deviation {
                Id = "A13-WQ2-IGNORE-TUAB", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11422, 11462",
                Aggregate = "WorstQuality2", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "UncertainDataSubNormal", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.36, 3.1.9", Reason = R_A13_WQ2_IGNORE_TUAB
            },
            new Part13Deviation {
                Id = "A13-WQ2-STARTBOUND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11422",
                Aggregate = "WorstQuality2", DataSet = "Historian4", RowTime = "12:01:04.000", ValueText = null, StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.36", Reason = R_A13_WQ2_STARTBOUND
            },
            new Part13Deviation {
                Id = "A13-WQ2-IGNORE-TUAB", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11422, 11462",
                Aggregate = "WorstQuality2", DataSet = "Historian4", RowTime = "12:01:20.000", ValueText = "UncertainDataSubNormal", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.36, 3.1.9", Reason = R_A13_WQ2_IGNORE_TUAB
            },
            new Part13Deviation {
                Id = "A13-WQ2-STARTBOUND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11422",
                Aggregate = "WorstQuality2", DataSet = "Historian5", RowTime = "12:01:04.000", ValueText = null, StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.36", Reason = R_A13_WQ2_STARTBOUND
            },
            new Part13Deviation {
                Id = "A13-WQ2-STARTBOUND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11422",
                Aggregate = "WorstQuality2", DataSet = "Historian5", RowTime = "12:01:20.000", ValueText = "UncertainDataSubNormal", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.36", Reason = R_A13_WQ2_STARTBOUND
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian1", RowTime = "12:00:16.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian1", RowTime = "12:00:48.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian1", RowTime = "12:01:20.000", ValueText = null, StatusText = "Good, Calculated, Partial, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian2", RowTime = "12:00:16.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian2", RowTime = "12:00:48.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = null, StatusText = "Good, Calculated, Partial, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian3", RowTime = "12:00:16.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian3", RowTime = "12:00:48.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = null, StatusText = "Good, Calculated, Partial, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian4", RowTime = "12:00:16.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian4", RowTime = "12:00:48.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian4", RowTime = "12:01:20.000", ValueText = null, StatusText = "Good, Calculated, Partial, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian5", RowTime = "12:00:16.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian5", RowTime = "12:00:48.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality", DataSet = "Historian5", RowTime = "12:01:20.000", ValueText = null, StatusText = "Good, Calculated, Partial, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality2", DataSet = "Historian2", RowTime = "12:00:16.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality2", DataSet = "Historian2", RowTime = "12:00:48.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality2", DataSet = "Historian3", RowTime = "12:00:16.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality2", DataSet = "Historian3", RowTime = "12:00:48.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality2", DataSet = "Historian4", RowTime = "12:00:16.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality2", DataSet = "Historian4", RowTime = "12:00:48.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality2", DataSet = "Historian5", RowTime = "12:00:16.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-WQ-MULTIPLEVALUES", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "WorstQuality2", DataSet = "Historian5", RowTime = "12:00:48.000", ValueText = null, StatusText = "Good, Calculated, MultipleValues",
                Clause = "OPC 10000-13 5.4.3.35, 5.4.3.36, 5.3.3.1 Table 48", Reason = R_A13_WQ_MULTIPLEVALUES
            },
            new Part13Deviation {
                Id = "A13-TA-AFTEREND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "TimeAverage", DataSet = "Historian1", RowTime = "12:01:35.000", ValueText = "90", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.6 Table 54, 5.4.3.8 Table 56", Reason = R_A13_TA_AFTEREND
            },
            new Part13Deviation {
                Id = "A13-TA-AFTEREND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian1", RowTime = "12:01:35.000", ValueText = "450", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.6 Table 54, 5.4.3.8 Table 56", Reason = R_A13_TA_AFTEREND
            },
            new Part13Deviation {
                Id = "A13-TA-AFTEREND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "TimeAverage", DataSet = "Historian2", RowTime = "12:01:35.000", ValueText = "108.75", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.6 Table 54, 5.4.3.8 Table 56", Reason = R_A13_TA_AFTEREND
            },
            new Part13Deviation {
                Id = "A13-TA-AFTEREND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian2", RowTime = "12:01:35.000", ValueText = "543.75", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.6 Table 54, 5.4.3.8 Table 56", Reason = R_A13_TA_AFTEREND
            },
            new Part13Deviation {
                Id = "A13-TA-AFTEREND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "TimeAverage", DataSet = "Historian3", RowTime = "12:01:35.000", ValueText = "90", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.6 Table 54, 5.4.3.8 Table 56", Reason = R_A13_TA_AFTEREND
            },
            new Part13Deviation {
                Id = "A13-TA-AFTEREND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:01:35.000", ValueText = "450", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.6 Table 54, 5.4.3.8 Table 56", Reason = R_A13_TA_AFTEREND
            },
            new Part13Deviation {
                Id = "A13-TA-AFTEREND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "TimeAverage", DataSet = "Historian5", RowTime = "12:01:35.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.6 Table 54, 5.4.3.8 Table 56", Reason = R_A13_TA_AFTEREND
            },
            new Part13Deviation {
                Id = "A13-TA-AFTEREND", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian5", RowTime = "12:01:35.000", ValueText = "350", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.6 Table 54, 5.4.3.8 Table 56", Reason = R_A13_TA_AFTEREND
            },
            new Part13Deviation {
                Id = "S13-TA-PARTIAL-EXTRAPOLATION", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "",
                Aggregate = "TimeAverage", DataSet = "Historian1", RowTime = "12:01:30.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.3.3.2", Reason = R_S13_TA_PARTIAL_EXTRAPOLATION
            },
            new Part13Deviation {
                Id = "S13-TA-PARTIAL-EXTRAPOLATION", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "",
                Aggregate = "Total", DataSet = "Historian1", RowTime = "12:01:30.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.3.3.2", Reason = R_S13_TA_PARTIAL_EXTRAPOLATION
            },
            new Part13Deviation {
                Id = "S13-TA-PARTIAL-EXTRAPOLATION", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "",
                Aggregate = "TimeAverage", DataSet = "Historian2", RowTime = "12:01:30.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.3.3.2", Reason = R_S13_TA_PARTIAL_EXTRAPOLATION
            },
            new Part13Deviation {
                Id = "S13-TA-PARTIAL-EXTRAPOLATION", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "",
                Aggregate = "Total", DataSet = "Historian2", RowTime = "12:01:30.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.3.3.2", Reason = R_S13_TA_PARTIAL_EXTRAPOLATION
            },
            new Part13Deviation {
                Id = "S13-TA-PARTIAL-EXTRAPOLATION", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "",
                Aggregate = "TimeAverage", DataSet = "Historian3", RowTime = "12:01:30.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.3.3.2", Reason = R_S13_TA_PARTIAL_EXTRAPOLATION
            },
            new Part13Deviation {
                Id = "S13-TA-PARTIAL-EXTRAPOLATION", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:01:30.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.3.3.2", Reason = R_S13_TA_PARTIAL_EXTRAPOLATION
            },
            new Part13Deviation {
                Id = "S13-TA-PARTIAL-EXTRAPOLATION", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "",
                Aggregate = "TimeAverage", DataSet = "Historian5", RowTime = "12:01:30.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.3.3.2", Reason = R_S13_TA_PARTIAL_EXTRAPOLATION
            },
            new Part13Deviation {
                Id = "S13-TA-PARTIAL-EXTRAPOLATION", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "",
                Aggregate = "Total", DataSet = "Historian5", RowTime = "12:01:30.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.3.3.2", Reason = R_S13_TA_PARTIAL_EXTRAPOLATION
            },
            new Part13Deviation {
                Id = "A13-NOT-CALCULATED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "NumberOfTransitions", DataSet = "Historian1", RowTime = "12:00:32.000", ValueText = null, StatusText = "Bad, Calculated",
                Clause = "OPC 10000-13 5.4.3.24 Table 72", Reason = R_A13_NOT_CALCULATED
            },
            new Part13Deviation {
                Id = "A13-DURATION-FIRSTREGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11425",
                Aggregate = "DurationGood", DataSet = "Historian1", RowTime = "12:00:32.000", ValueText = "8000", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.33, 5.4.3.34", Reason = R_A13_DURATION_FIRSTREGION
            },
            new Part13Deviation {
                Id = "A13-DURATION-FIRSTREGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11425",
                Aggregate = "PercentGood", DataSet = "Historian1", RowTime = "12:00:32.000", ValueText = "50", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.33, 5.4.3.34", Reason = R_A13_DURATION_FIRSTREGION
            },
            new Part13Deviation {
                Id = "A13-DURATION-FIRSTREGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11425",
                Aggregate = "DurationGood", DataSet = "Historian1", RowTime = "12:01:04.000", ValueText = "6000", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.33, 5.4.3.34", Reason = R_A13_DURATION_FIRSTREGION
            },
            new Part13Deviation {
                Id = "A13-DURATION-FIRSTREGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11425",
                Aggregate = "PercentGood", DataSet = "Historian1", RowTime = "12:01:04.000", ValueText = "37.5", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.33, 5.4.3.34", Reason = R_A13_DURATION_FIRSTREGION
            },
            new Part13Deviation {
                Id = "A13-DURATION-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11425",
                Aggregate = "DurationBad", DataSet = "Historian2", RowTime = "12:01:04.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.33, 5.4.3.34", Reason = R_A13_DURATION_UNCERTAIN
            },
            new Part13Deviation {
                Id = "A13-DURATION-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11425",
                Aggregate = "PercentBad", DataSet = "Historian2", RowTime = "12:01:04.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.33, 5.4.3.34", Reason = R_A13_DURATION_UNCERTAIN
            },
            new Part13Deviation {
                Id = "A13-DURATION-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11425",
                Aggregate = "DurationBad", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.33, 5.4.3.34", Reason = R_A13_DURATION_UNCERTAIN
            },
            new Part13Deviation {
                Id = "A13-DURATION-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11425",
                Aggregate = "PercentBad", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.33, 5.4.3.34", Reason = R_A13_DURATION_UNCERTAIN
            },
            new Part13Deviation {
                Id = "A13-DURATION-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11425",
                Aggregate = "DurationBad", DataSet = "Historian3", RowTime = "12:01:04.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.33, 5.4.3.34", Reason = R_A13_DURATION_UNCERTAIN
            },
            new Part13Deviation {
                Id = "A13-DURATION-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11425",
                Aggregate = "PercentBad", DataSet = "Historian3", RowTime = "12:01:04.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.33, 5.4.3.34", Reason = R_A13_DURATION_UNCERTAIN
            },
            new Part13Deviation {
                Id = "A13-DURATION-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11425",
                Aggregate = "DurationBad", DataSet = "Historian4", RowTime = "12:01:04.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.33, 5.4.3.34", Reason = R_A13_DURATION_UNCERTAIN
            },
            new Part13Deviation {
                Id = "A13-DURATION-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11425",
                Aggregate = "PercentBad", DataSet = "Historian4", RowTime = "12:01:04.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.33, 5.4.3.34", Reason = R_A13_DURATION_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-UNCERTAIN-AS-GOOD", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "NumberOfTransitions", DataSet = "Historian1", RowTime = "12:01:04.000", ValueText = null, StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 4.2.1.2, 5.4.3.2.1 versus 5.4.3.21, 5.4.3.24, 5.4.3.27, 5.4.3.31, 5.4.3.37-5.4.3.40", Reason = R_S13_UNCERTAIN_AS_GOOD
            },
            new Part13Deviation {
                Id = "S13-UNCERTAIN-AS-GOOD", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "NumberOfTransitions", DataSet = "Historian5", RowTime = "12:01:04.000", ValueText = null, StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 4.2.1.2, 5.4.3.2.1 versus 5.4.3.21, 5.4.3.24, 5.4.3.27, 5.4.3.31, 5.4.3.37-5.4.3.40", Reason = R_S13_UNCERTAIN_AS_GOOD
            },
            new Part13Deviation {
                Id = "S13-UNCERTAIN-AS-GOOD", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "Delta", DataSet = "Historian1", RowTime = "12:01:04.000", ValueText = "0", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 4.2.1.2, 5.4.3.2.1 versus 5.4.3.21, 5.4.3.24, 5.4.3.27, 5.4.3.31, 5.4.3.37-5.4.3.40", Reason = R_S13_UNCERTAIN_AS_GOOD
            },
            new Part13Deviation {
                Id = "S13-UNCERTAIN-AS-GOOD", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "Delta", DataSet = "Historian5", RowTime = "12:01:04.000", ValueText = "40", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 4.2.1.2, 5.4.3.2.1 versus 5.4.3.21, 5.4.3.24, 5.4.3.27, 5.4.3.31, 5.4.3.37-5.4.3.40", Reason = R_S13_UNCERTAIN_AS_GOOD
            },
            new Part13Deviation {
                Id = "S13-UNCERTAIN-AS-GOOD", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "StandardDeviationPopulation", DataSet = "Historian1", RowTime = "12:01:00.000", ValueText = "5", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 4.2.1.2, 5.4.3.2.1 versus 5.4.3.21, 5.4.3.24, 5.4.3.27, 5.4.3.31, 5.4.3.37-5.4.3.40", Reason = R_S13_UNCERTAIN_AS_GOOD
            },
            new Part13Deviation {
                Id = "S13-UNCERTAIN-AS-GOOD", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "StandardDeviationPopulation", DataSet = "Historian5", RowTime = "12:01:00.000", ValueText = "20", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 4.2.1.2, 5.4.3.2.1 versus 5.4.3.21, 5.4.3.24, 5.4.3.27, 5.4.3.31, 5.4.3.37-5.4.3.40", Reason = R_S13_UNCERTAIN_AS_GOOD
            },
            new Part13Deviation {
                Id = "S13-UNCERTAIN-AS-GOOD", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "VariancePopulation", DataSet = "Historian1", RowTime = "12:01:00.000", ValueText = "25", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 4.2.1.2, 5.4.3.2.1 versus 5.4.3.21, 5.4.3.24, 5.4.3.27, 5.4.3.31, 5.4.3.37-5.4.3.40", Reason = R_S13_UNCERTAIN_AS_GOOD
            },
            new Part13Deviation {
                Id = "S13-UNCERTAIN-AS-GOOD", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "VariancePopulation", DataSet = "Historian5", RowTime = "12:01:00.000", ValueText = "400", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 4.2.1.2, 5.4.3.2.1 versus 5.4.3.21, 5.4.3.24, 5.4.3.27, 5.4.3.31, 5.4.3.37-5.4.3.40", Reason = R_S13_UNCERTAIN_AS_GOOD
            },
            new Part13Deviation {
                Id = "S13-UNCERTAIN-AS-GOOD", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "StandardDeviationSample", DataSet = "Historian1", RowTime = "12:01:00.000", ValueText = "7.0711", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 4.2.1.2, 5.4.3.2.1 versus 5.4.3.21, 5.4.3.24, 5.4.3.27, 5.4.3.31, 5.4.3.37-5.4.3.40", Reason = R_S13_UNCERTAIN_AS_GOOD
            },
            new Part13Deviation {
                Id = "S13-UNCERTAIN-AS-GOOD", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "StandardDeviationSample", DataSet = "Historian5", RowTime = "12:01:00.000", ValueText = "28.2843", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 4.2.1.2, 5.4.3.2.1 versus 5.4.3.21, 5.4.3.24, 5.4.3.27, 5.4.3.31, 5.4.3.37-5.4.3.40", Reason = R_S13_UNCERTAIN_AS_GOOD
            },
            new Part13Deviation {
                Id = "S13-UNCERTAIN-AS-GOOD", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "VarianceSample", DataSet = "Historian1", RowTime = "12:01:00.000", ValueText = "50", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 4.2.1.2, 5.4.3.2.1 versus 5.4.3.21, 5.4.3.24, 5.4.3.27, 5.4.3.31, 5.4.3.37-5.4.3.40", Reason = R_S13_UNCERTAIN_AS_GOOD
            },
            new Part13Deviation {
                Id = "S13-UNCERTAIN-AS-GOOD", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "VarianceSample", DataSet = "Historian5", RowTime = "12:01:00.000", ValueText = "800", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 4.2.1.2, 5.4.3.2.1 versus 5.4.3.21, 5.4.3.24, 5.4.3.27, 5.4.3.31, 5.4.3.37-5.4.3.40", Reason = R_S13_UNCERTAIN_AS_GOOD
            },
        };

        /// <summary>
        /// Returns the expectation for every row of the table.
        /// </summary>
        public static List<Part13ExpectedRow> Resolve(AnnexAExampleTable table)
        {
            List<Part13ExpectedRow> rows = new List<Part13ExpectedRow>();

            foreach (AnnexAExpectedValue row in table.Rows)
            {
                Part13Deviation deviation = All.FirstOrDefault(d => d.AppliesTo(table, row));
                rows.Add(new Part13ExpectedRow {
                    AnnexARow = row,
                    Deviation = deviation,
                    Expected = deviation != null ? deviation.Apply(row) : row
                });
            }

            return rows;
        }
    }
}
