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

        private const string R_A13_TOTAL_SLOPED =
            "\"Note that this calculation always uses a SlopedInterpolation between points; Total2 uses a Stepped Attribute or SlopedInterpolation depending on the value of the Stepped Attribute\" (5.4.3.8), and Total = TimeAverage x ProcessingInterval. Annex A calculates Total on the Stepped Historian3 with stepped lines, the same generator error that erratum 3604 corrected for TimeAverage. Historian3 has the same raw data as Historian2, and Total uses neither Stepped nor PercentDataGood/PercentDataBad, so the expectation is the Annex A Historian2 Total row. After the end of data (12:01:30) UseSlopedExtrapolation differs and the Historian3 rows are kept.";

        private const string R_A13_MAX_UNCERTAIN_ABOVE =
            "\"If an Uncertain value is greater than the maximum Good value the Status is Uncertain_DataSubNormal\" (Tables 59, 61; Table 62 for Range). Historian5 (TreatUncertainAsBad = False): the Uncertain value 70 at 12:01:17 lies above the Good maximum 30 at 12:01:12, so the result is the Good maximum with Uncertain_DataSubNormal and the Calculated bit. Annex A keeps Good. Mantis 11427 (~0025875) describes the same rule.";

        private const string R_A13_ACTUALTIME_CALCULATED =
            "MinimumActualTime and MaximumActualTime, Calculated: \"Set Sometimes - If the Status was set to Uncertain_DataSubNormal because of non-Good values in the interval\" (Tables 60, 61). The interval holds a non-Good value (the Bad value at 12:00:42, or the Uncertain value at 12:01:17 with TreatUncertainAsBad = True), so the Uncertain result has the Calculated bit; Annex A keeps the Raw bits. Mantis 11427 (~0025875): \"In both cases the calculated bit is set\".";

        private const string R_S13_TA2_OMITS_UNCERTAIN =
            "TimeAverage2 leaves all non-Good data out of the calculation and shortens the interval by its duration (5.4.3.7), and treats an Uncertain bound like any other Uncertain value (Table 55). Total2 is TimeAverage2 x the duration of the Good data (5.4.3.9), and Table 57 ignores an Uncertain bound like any other non-Good value. The definition names non-Good data, so it overrides TreatUncertainAsBad = False (4.2.1.2; Mantis 11495 decided the same for Count). A sub-interval that ends at a value that is left out uses its Good starting value (5.4.3.7), and an interval without Good data has no value and is Bad. Annex A uses the Uncertain data and the Uncertain bounds of Historian1 and Historian5 (TreatUncertainAsBad = False).";

        private const string R_S13_LAST_REGION =
            "Each region starts with a data point and lasts until the next one or the end of the interval (5.4.3.31, 5.4.3.32; DurationInStateZero and DurationInStateNonZero use the same regions). For TimeAverage2 and Total2 the missing end bound is Bad_NoData (Tables 55, 57), and a sub-interval that ends at a Bad value uses the Good starting value (5.4.3.7). Annex A ends the region that starts at the last raw value (12:01:30) after 1 ms, which the text does not provide; PercentGood then uses 10.001 s instead of the 16 s interval.";

        private const string R_A13_ANNOTATIONCOUNT =
            "AnnotationCount \"returns a count of all Annotations in the interval\" (5.4.3.20). The Annex A histories have no Annotations, so every interval within the data is 0 (Good, Calculated; Table 68). Annex A counts the raw values instead; the WG agreed that these rows are wrong and will correct the CSV (Mantis 11500).";

        private const string R_M11462_BOUND_UNCERTAIN =
            "TreatUncertainAsBad does not apply to bounding values: they are calculated only as 3.1.7 to 3.1.9 describe, from the non-Bad raw values around the timestamp (WG answer to Mantis 11462, ~0025840). An Uncertain raw value can be used and makes the bound Uncertain_DataSubNormal. Historian2/3 (TreatUncertainAsBad = True): the Uncertain value 70 at 12:01:17 is used, so the bound at 12:01:15 is 66 (sloped 60 -> 70) and the bound at 12:01:20 is 70 (sloped 70 -> 70, or stepped 70). TimeAverage and Total use these bounds (Total is always sloped, 5.4.3.8). NumberOfTransitions uses \"a non-Bad value prior to the interval\" as its bound (Table 72), the Uncertain 70, so the Good 70 at 12:01:23 is no transition (Historian4: the Uncertain True at 12:01:17 and the Good True at 12:01:23). Annex A applies TreatUncertainAsBad to the bounds, as the note in 3.1.7 says; the reporter's follow-up on that sentence is open in 11462.";

        /// <summary>
        /// All deviations. Each entry quotes the normative sentence it relies on. The rows were
        /// found with an implementation of the normative text that is independent of the stack and
        /// cross-checked against the Mantis analyses (11422, 11425, 11426, 11427, 11462, 11466).
        /// Kind = SpecificationAmbiguous rows accept the server reading of an unclear clause.
        /// </summary>
        public static readonly IReadOnlyList<Part13Deviation> All = new List<Part13Deviation> {
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
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:00:00.000", ValueText = "31.957", StatusText = "UncertainDataSubNormal, Calculated, Partial",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:00:05.000", ValueText = "61.957", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:00:10.000", ValueText = "72.826", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:00:15.000", ValueText = "83.696", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:00:20.000", ValueText = "94.565", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:00:25.000", ValueText = "118.409", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:00:30.000", ValueText = "135.227", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:00:35.000", ValueText = "146.919", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:00:40.000", ValueText = "169.444", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:00:45.000", ValueText = "200", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:00:50.000", ValueText = "247.250", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:00:55.000", ValueText = "263.750", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:01:00.000", ValueText = "276.250", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:01:05.000", ValueText = "288.750", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:01:10.000", ValueText = "308", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 3.1.8, 5.4.3.8, Table 56", Reason = R_M11462_BOUND_UNCERTAIN + " " + R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:01:15.000", ValueText = "340", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 3.1.8, 5.4.3.8, Table 56", Reason = R_M11462_BOUND_UNCERTAIN + " " + R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "356.667", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 3.1.8, 5.4.3.8, Table 56", Reason = R_M11462_BOUND_UNCERTAIN + " " + R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-TOTAL-SLOPED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total", DataSet = "Historian3", RowTime = "12:01:25.000", ValueText = "418.333", StatusText = "Good, Calculated",
                Clause = "OPC 10000-13 5.4.3.8, Table 56", Reason = R_A13_TOTAL_SLOPED
            },
            new Part13Deviation {
                Id = "A13-MAX-UNCERTAIN-ABOVE", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11427",
                Aggregate = "Maximum", DataSet = "Historian5", RowTime = "12:01:04.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.11, 5.4.3.13, 5.4.3.14, Tables 59, 61, 62", Reason = R_A13_MAX_UNCERTAIN_ABOVE
            },
            new Part13Deviation {
                Id = "A13-MAX-UNCERTAIN-ABOVE", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11427",
                Aggregate = "Range", DataSet = "Historian5", RowTime = "12:01:04.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.11, 5.4.3.13, 5.4.3.14, Tables 59, 61, 62", Reason = R_A13_MAX_UNCERTAIN_ABOVE
            },
            new Part13Deviation {
                Id = "A13-MAX-UNCERTAIN-ABOVE", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11427",
                Aggregate = "MaximumActualTime", DataSet = "Historian5", RowTime = "12:01:12.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 5.4.3.11, 5.4.3.13, 5.4.3.14, Tables 59, 61, 62", Reason = R_A13_MAX_UNCERTAIN_ABOVE
            },
            new Part13Deviation {
                Id = "A13-ACTUALTIME-CALCULATED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11427",
                Aggregate = "MinimumActualTime", DataSet = "Historian2", RowTime = "12:00:39.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 Tables 60, 61", Reason = R_A13_ACTUALTIME_CALCULATED
            },
            new Part13Deviation {
                Id = "A13-ACTUALTIME-CALCULATED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11427",
                Aggregate = "MinimumActualTime", DataSet = "Historian3", RowTime = "12:00:39.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 Tables 60, 61", Reason = R_A13_ACTUALTIME_CALCULATED
            },
            new Part13Deviation {
                Id = "A13-ACTUALTIME-CALCULATED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11427",
                Aggregate = "MinimumActualTime", DataSet = "Historian5", RowTime = "12:00:39.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 Tables 60, 61", Reason = R_A13_ACTUALTIME_CALCULATED
            },
            new Part13Deviation {
                Id = "A13-ACTUALTIME-CALCULATED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11427",
                Aggregate = "MinimumActualTime", DataSet = "Historian2", RowTime = "12:01:12.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 Tables 60, 61", Reason = R_A13_ACTUALTIME_CALCULATED
            },
            new Part13Deviation {
                Id = "A13-ACTUALTIME-CALCULATED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11427",
                Aggregate = "MinimumActualTime", DataSet = "Historian3", RowTime = "12:01:12.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 Tables 60, 61", Reason = R_A13_ACTUALTIME_CALCULATED
            },
            new Part13Deviation {
                Id = "A13-ACTUALTIME-CALCULATED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11427",
                Aggregate = "MaximumActualTime", DataSet = "Historian2", RowTime = "12:00:39.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 Tables 60, 61", Reason = R_A13_ACTUALTIME_CALCULATED
            },
            new Part13Deviation {
                Id = "A13-ACTUALTIME-CALCULATED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11427",
                Aggregate = "MaximumActualTime", DataSet = "Historian3", RowTime = "12:00:39.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 Tables 60, 61", Reason = R_A13_ACTUALTIME_CALCULATED
            },
            new Part13Deviation {
                Id = "A13-ACTUALTIME-CALCULATED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11427",
                Aggregate = "MaximumActualTime", DataSet = "Historian5", RowTime = "12:00:39.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 Tables 60, 61", Reason = R_A13_ACTUALTIME_CALCULATED
            },
            new Part13Deviation {
                Id = "A13-ACTUALTIME-CALCULATED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11427",
                Aggregate = "MaximumActualTime", DataSet = "Historian2", RowTime = "12:01:12.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 Tables 60, 61", Reason = R_A13_ACTUALTIME_CALCULATED
            },
            new Part13Deviation {
                Id = "A13-ACTUALTIME-CALCULATED", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11427",
                Aggregate = "MaximumActualTime", DataSet = "Historian3", RowTime = "12:01:12.000", ValueText = null, StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 Tables 60, 61", Reason = R_A13_ACTUALTIME_CALCULATED
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "TimeAverage2", DataSet = "Historian1", RowTime = "12:00:35.000", ValueText = "", StatusText = "Bad, Calculated",
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "TimeAverage2", DataSet = "Historian1", RowTime = "12:01:00.000", ValueText = "60", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "TimeAverage2", DataSet = "Historian1", RowTime = "12:01:05.000", ValueText = "", StatusText = "Bad, Calculated",
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "TimeAverage2", DataSet = "Historian1", RowTime = "12:01:10.000", ValueText = "", StatusText = "Bad, Calculated",
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "TimeAverage2", DataSet = "Historian1", RowTime = "12:01:15.000", ValueText = "", StatusText = "Bad, Calculated",
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "TimeAverage2", DataSet = "Historian5", RowTime = "12:00:40.000", ValueText = "", StatusText = "Bad, Calculated",
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "TimeAverage2", DataSet = "Historian5", RowTime = "12:01:10.000", ValueText = "30.4", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "TimeAverage2", DataSet = "Historian5", RowTime = "12:01:15.000", ValueText = "", StatusText = "Bad, Calculated",
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "TimeAverage2", DataSet = "Historian5", RowTime = "12:01:20.000", ValueText = "73.3333", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "Total2", DataSet = "Historian1", RowTime = "12:00:35.000", ValueText = "", StatusText = "Bad, Calculated",
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "Total2", DataSet = "Historian1", RowTime = "12:01:00.000", ValueText = "300", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "Total2", DataSet = "Historian1", RowTime = "12:01:05.000", ValueText = "", StatusText = "Bad, Calculated",
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "Total2", DataSet = "Historian1", RowTime = "12:01:10.000", ValueText = "", StatusText = "Bad, Calculated",
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "Total2", DataSet = "Historian1", RowTime = "12:01:15.000", ValueText = "", StatusText = "Bad, Calculated",
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "Total2", DataSet = "Historian5", RowTime = "12:00:40.000", ValueText = "", StatusText = "Bad, Calculated",
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "Total2", DataSet = "Historian5", RowTime = "12:01:10.000", ValueText = "152", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "Total2", DataSet = "Historian5", RowTime = "12:01:15.000", ValueText = "", StatusText = "Bad, Calculated",
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-TA2-OMITS-UNCERTAIN", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11495",
                Aggregate = "Total2", DataSet = "Historian5", RowTime = "12:01:20.000", ValueText = "146.6667", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.7, 5.4.3.9, Tables 55, 57", Reason = R_S13_TA2_OMITS_UNCERTAIN
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total2", DataSet = "Historian1", RowTime = "12:01:30.000", ValueText = "450", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total2", DataSet = "Historian3", RowTime = "12:01:30.000", ValueText = "450", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "Total2", DataSet = "Historian5", RowTime = "12:01:30.000", ValueText = "350", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "DurationInStateNonZero", DataSet = "Historian1", RowTime = "12:01:20.000", ValueText = "16000", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "DurationInStateNonZero", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "13000", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "DurationInStateNonZero", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "13000", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "DurationInStateNonZero", DataSet = "Historian4", RowTime = "12:01:20.000", ValueText = "9000", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "DurationInStateNonZero", DataSet = "Historian5", RowTime = "12:01:20.000", ValueText = "13000", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "DurationGood", DataSet = "Historian1", RowTime = "12:01:20.000", ValueText = "16000", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "DurationGood", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "13000", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "DurationGood", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "13000", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "DurationGood", DataSet = "Historian4", RowTime = "12:01:20.000", ValueText = "13000", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "DurationGood", DataSet = "Historian5", RowTime = "12:01:20.000", ValueText = "13000", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "PercentGood", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "81.25", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "PercentGood", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "81.25", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "PercentGood", DataSet = "Historian4", RowTime = "12:01:20.000", ValueText = "81.25", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "S13-LAST-REGION", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "",
                Aggregate = "PercentGood", DataSet = "Historian5", RowTime = "12:01:20.000", ValueText = "81.25", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.31, 5.4.3.32, 5.4.3.22, 5.4.3.23, 5.4.3.7, Tables 55, 57", Reason = R_S13_LAST_REGION
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "Interpolative", DataSet = "Historian2", RowTime = "12:01:15.000", ValueText = "66", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, Table 52", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "Interpolative", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "70", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, Table 52", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "Interpolative", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "70", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, Table 52", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "TimeAverage", DataSet = "Historian2", RowTime = "12:01:10.000", ValueText = "61.6", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.6", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "TimeAverage", DataSet = "Historian2", RowTime = "12:01:15.000", ValueText = "68", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.6", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "TimeAverage", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "71.333", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.6", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "TimeAverage", DataSet = "Historian3", RowTime = "12:01:10.000", ValueText = "61.6", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.6", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "TimeAverage", DataSet = "Historian3", RowTime = "12:01:15.000", ValueText = "68", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.6", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "TimeAverage", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "71.333", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.6", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "Total", DataSet = "Historian2", RowTime = "12:01:10.000", ValueText = "308", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.8", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "Total", DataSet = "Historian2", RowTime = "12:01:15.000", ValueText = "340", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.8", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "Total", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "356.667", StatusText = null,
                Clause = "OPC 10000-13 3.1.8, 5.4.3.8", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "StartBound", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Interpolated, Partial",
                Clause = "OPC 10000-13 3.1.9, 5.4.3.28, Table 76", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "StartBound", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Interpolated, Partial",
                Clause = "OPC 10000-13 3.1.9, 5.4.3.28, Table 76", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "EndBound", DataSet = "Historian2", RowTime = "12:01:04.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 3.1.9, 5.4.3.29, Table 77", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "EndBound", DataSet = "Historian3", RowTime = "12:01:04.000", ValueText = "70", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 3.1.9, 5.4.3.29, Table 77", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "DeltaBounds", DataSet = "Historian2", RowTime = "12:01:04.000", ValueText = "14", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 3.1.9, 5.4.3.30, Table 78", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "DeltaBounds", DataSet = "Historian3", RowTime = "12:01:04.000", ValueText = "20", StatusText = "UncertainDataSubNormal, Calculated",
                Clause = "OPC 10000-13 3.1.9, 5.4.3.30, Table 78", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "NumberOfTransitions", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "2", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.24, Table 72", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "NumberOfTransitions", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "2", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.24, Table 72", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "M11462-BOUND-UNCERTAIN", Kind = Part13DeviationKind.SpecificationAmbiguous, Mantis = "11462",
                Aggregate = "NumberOfTransitions", DataSet = "Historian4", RowTime = "12:01:20.000", ValueText = "2", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.24, Table 72", Reason = R_M11462_BOUND_UNCERTAIN
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian1", RowTime = "12:00:00.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian1", RowTime = "12:00:16.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian1", RowTime = "12:00:32.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian1", RowTime = "12:00:48.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian1", RowTime = "12:01:04.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian1", RowTime = "12:01:20.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian2", RowTime = "12:00:00.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian2", RowTime = "12:00:16.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian2", RowTime = "12:00:32.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian2", RowTime = "12:00:48.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian2", RowTime = "12:01:04.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian2", RowTime = "12:01:20.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian3", RowTime = "12:00:00.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian3", RowTime = "12:00:16.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian3", RowTime = "12:00:32.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian3", RowTime = "12:00:48.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian3", RowTime = "12:01:04.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian3", RowTime = "12:01:20.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian4", RowTime = "12:00:00.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian4", RowTime = "12:00:16.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian4", RowTime = "12:00:32.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian4", RowTime = "12:00:48.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian4", RowTime = "12:01:04.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian4", RowTime = "12:01:20.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian5", RowTime = "12:00:00.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian5", RowTime = "12:00:16.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian5", RowTime = "12:00:32.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian5", RowTime = "12:00:48.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian5", RowTime = "12:01:04.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
            },
            new Part13Deviation {
                Id = "A13-ANNOTATIONCOUNT", Kind = Part13DeviationKind.AnnexAContradictsNormativeText, Mantis = "11500",
                Aggregate = "AnnotationCount", DataSet = "Historian5", RowTime = "12:01:20.000", ValueText = "0", StatusText = null,
                Clause = "OPC 10000-13 5.4.3.20, Table 68", Reason = R_A13_ANNOTATIONCOUNT
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
