/* ========================================================================
 * OPC UA Part 13 (OPC 10000-13 v1.05.07) aggregate conformance tests.
 *
 * Hand-computed cases for the rules behind the disputed CTT 1.05.513 results.
 * Every expected value is derived from the quoted normative sentence, not from the
 * stack implementation, the CTT oracle or the informative Annex A.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.Part13
{
    /// <summary>
    /// Normative Part 13 rules referenced by the Mantis issues on the CTT aggregate oracle.
    /// </summary>
    [TestFixture]
    [Category("Part13")]
    [Category("Part13Normative")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class Part13NormativeTests
    {
        private static readonly DateTime s_t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        #region WorstQuality2 (§5.4.3.36, Mantis 11422)
        /// <summary>
        /// §5.4.3.36: "The start bound calculated using Simple Bounding Values (see 3.1.9) is always
        /// included when determining the worst quality." Only the start bound is named.
        /// The interval [5 s, 15 s) has a Good start bound (sloped between 1@0 and 2@10) and a Good
        /// raw value; the end bound at 15 s is Uncertain_DataSubNormal (§3.1.9: "If the value after
        /// the timestamp is Bad or Uncertain the StatusCode is Uncertain_DataSubNormal") and is not
        /// part of the interval [Start, End) (§5.4.2.2, Table 49).
        /// </summary>
        [Test]
        public void WorstQuality2IgnoresTheEndBound()
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(0, 1, StatusCodes.Good),
                Raw(10, 2, StatusCodes.Good),
                Raw(20, null, StatusCodes.Bad),
                Raw(30, 3, StatusCodes.Good)
            };

            DataValue result = RunSingle(ObjectIds.AggregateFunction_WorstQuality2, raw, 5, 15, Config(false));

            AssertTimestamp(result, 5);
            Assert.That(result.Value, Is.InstanceOf<StatusCode>(), "WorstQuality2 returns a StatusCode value (Table 50)");
            Assert.That(((StatusCode)result.Value).CodeBits, Is.EqualTo(StatusCodes.Good),
                "§5.4.3.36 names only the start bound; the Uncertain end bound at 15 s must not be used");
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.Good), "Table 84: Custom Always Good");
            AssertLocation(result, AggregateBits.Calculated, "Table 84: Calculated Set Always");
        }

        /// <summary>
        /// §5.4.3.36: the start bound is always included. The raw value before 5 s is Bad, so the
        /// simple start bound is Bad_NoData (§3.1.9: "If the value before the timestamp is Bad the
        /// StatusCode is Bad_NoData") and is the worst quality of the interval; WorstQuality (no bounds,
        /// §5.4.3.35) only sees the Good raw value at 10 s.
        /// </summary>
        [Test]
        public void WorstQuality2IncludesTheStartBound()
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(0, 1, StatusCodes.Good),
                Raw(4, null, StatusCodes.Bad),
                Raw(10, 2, StatusCodes.Good),
                Raw(20, 3, StatusCodes.Good)
            };

            DataValue worst2 = RunSingle(ObjectIds.AggregateFunction_WorstQuality2, raw, 5, 15, Config(false));
            DataValue worst = RunSingle(ObjectIds.AggregateFunction_WorstQuality, raw, 5, 15, Config(false));

            Assert.That(((StatusCode)worst2.Value).CodeBits, Is.EqualTo(StatusCodes.BadNoData), "WorstQuality2 includes the Bad_NoData start bound");
            Assert.That(((StatusCode)worst.Value).CodeBits, Is.EqualTo(StatusCodes.Good), "WorstQuality uses no bounds (Table 83)");
            Assert.That(worst2.StatusCode.CodeBits, Is.EqualTo(StatusCodes.Good), "Table 84: Custom Always Good");
        }

        /// <summary>
        /// §5.4.3.36: "The server shall ignore the TreatUncertainAsBad for this aggregate." The raw value before
        /// 5 s is Uncertain, so the simple start bound is Uncertain_DataSubNormal (§3.1.9: "If the value before
        /// the timestamp is Uncertain the StatusCode is Uncertain_DataSubNormal"). Applying
        /// TreatUncertainAsBad = True to the bound would turn it into Bad_NoData.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void WorstQuality2IgnoresTreatUncertainAsBadForTheStartBound(bool treatUncertainAsBad)
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(0, 1, StatusCodes.Good),
                Raw(4, 2, StatusCodes.Uncertain),
                Raw(10, 3, StatusCodes.Good),
                Raw(20, 4, StatusCodes.Good)
            };

            DataValue worst2 = RunSingle(ObjectIds.AggregateFunction_WorstQuality2, raw, 5, 15, Config(treatUncertainAsBad));

            Assert.That(
                AggregateResultComparer.FormatCode((StatusCode)worst2.Value),
                Is.EqualTo(AggregateResultComparer.FormatCode(StatusCodes.UncertainDataSubNormal)),
                "the start bound after an Uncertain raw value is Uncertain_DataSubNormal whatever TreatUncertainAsBad says");
        }
        #endregion

        #region Minimum, Maximum, ActualTime, Range (Tables 58-62, Mantis 11426)
        /// <summary>
        /// Mantis 11426 answer (~0025852, OPC Classic semantics): Minimum, MinimumActualTime and Range
        /// look at Uncertain values as if they were Good, whatever TreatUncertainAsBad is; an Uncertain
        /// extremum makes the result Uncertain_DataSubNormal. Tables 58-61 set Calculated "if the Status
        /// was set to Uncertain_DataSubNormal because of non-Good values in the interval", the ActualTime
        /// aggregates included (CTT 1.05.513 on Historian1, 005_06 MinimumActualTime at 12:08:00).
        /// Data in [0 s, 10 s): Good 5@0, Uncertain 1@2, Good 3@4, so the minimum is the Uncertain 1@2.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UncertainValueBelowGoodMinimumIsTheUncertainMinimum(bool treatUncertainAsBad)
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(-10, 9, StatusCodes.Good),
                Raw(0, 5, StatusCodes.Good),
                Raw(2, 1, StatusCodes.Uncertain),
                Raw(4, 3, StatusCodes.Good),
                Raw(10, 9, StatusCodes.Good)
            };

            AggregateConfiguration configuration = Config(treatUncertainAsBad);

            DataValue minimum = RunSingle(ObjectIds.AggregateFunction_Minimum, raw, 0, 10, configuration);
            AssertValue(minimum, 1, "the Uncertain value is a candidate (Mantis 11426)");
            AssertTimestamp(minimum, 0);
            AssertCode(minimum, StatusCodes.UncertainDataSubNormal, "Table 58");
            AssertLocation(minimum, AggregateBits.Calculated, "Table 58");

            DataValue minimumActualTime = RunSingle(ObjectIds.AggregateFunction_MinimumActualTime, raw, 0, 10, configuration);
            AssertValue(minimumActualTime, 1, "the Uncertain value is a candidate (Mantis 11426)");
            AssertTimestamp(minimumActualTime, 2);
            AssertCode(minimumActualTime, StatusCodes.UncertainDataSubNormal, "Table 60");
            AssertLocation(minimumActualTime, AggregateBits.Calculated, "Table 60: Uncertain because of the Uncertain minimum (CTT)");

            DataValue range = RunSingle(ObjectIds.AggregateFunction_Range, raw, 0, 10, configuration);
            AssertValue(range, 4, "maximum minus minimum including the Uncertain value (Mantis 11426)");
            AssertCode(range, StatusCodes.UncertainDataSubNormal, "Table 62");
            AssertLocation(range, AggregateBits.Calculated, "Table 62: Calculated Set Always");
        }

        /// <summary>
        /// Maximum, MaximumActualTime and Range with an Uncertain value above the Good maximum
        /// (Mantis 11426 ~0025852): the Uncertain value is the maximum and the result is
        /// Uncertain_DataSubNormal, Calculated, whatever TreatUncertainAsBad is.
        /// Data in [0 s, 10 s): Good 3@0, Uncertain 8@2, Good 5@4.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UncertainValueAboveGoodMaximumIsTheUncertainMaximum(bool treatUncertainAsBad)
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(-10, 1, StatusCodes.Good),
                Raw(0, 3, StatusCodes.Good),
                Raw(2, 8, StatusCodes.Uncertain),
                Raw(4, 5, StatusCodes.Good),
                Raw(10, 1, StatusCodes.Good)
            };

            AggregateConfiguration configuration = Config(treatUncertainAsBad);

            DataValue maximum = RunSingle(ObjectIds.AggregateFunction_Maximum, raw, 0, 10, configuration);
            AssertValue(maximum, 8, "the Uncertain value is a candidate (Mantis 11426)");
            AssertTimestamp(maximum, 0);
            AssertCode(maximum, StatusCodes.UncertainDataSubNormal, "Table 59");
            AssertLocation(maximum, AggregateBits.Calculated, "Table 59");

            DataValue maximumActualTime = RunSingle(ObjectIds.AggregateFunction_MaximumActualTime, raw, 0, 10, configuration);
            AssertValue(maximumActualTime, 8, "the Uncertain value is a candidate (Mantis 11426)");
            AssertTimestamp(maximumActualTime, 2);
            AssertCode(maximumActualTime, StatusCodes.UncertainDataSubNormal, "Table 61");
            AssertLocation(maximumActualTime, AggregateBits.Calculated, "Table 61: Uncertain because of the Uncertain maximum (CTT)");

            DataValue range = RunSingle(ObjectIds.AggregateFunction_Range, raw, 0, 10, configuration);
            AssertValue(range, 5, "maximum minus minimum including the Uncertain value (Mantis 11426)");
            AssertCode(range, StatusCodes.UncertainDataSubNormal, "Table 62");
        }

        /// <summary>
        /// Tables 58, 60, 61: "If Bad values exist then the Status is Uncertain_DataSubNormal" and the
        /// Calculated bit is set "if the Status was set to Uncertain_DataSubNormal because of non-Good
        /// values in the interval" (the ActualTime aggregates keep the Raw bit for a Bad sample without a value, as in Annex A and the CTT).
        /// Data in [0 s, 10 s): Good 5@0, Bad@2, Good 3@4.
        /// </summary>
        [Test]
        public void BadValueInIntervalSetsUncertainAndCalculated()
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(-10, 9, StatusCodes.Good),
                Raw(0, 5, StatusCodes.Good),
                Raw(2, null, StatusCodes.Bad),
                Raw(4, 3, StatusCodes.Good),
                Raw(10, 9, StatusCodes.Good)
            };

            DataValue minimum = RunSingle(ObjectIds.AggregateFunction_Minimum, raw, 0, 10, Config(false));
            AssertValue(minimum, 3, "§5.4.3.10");
            AssertCode(minimum, StatusCodes.UncertainDataSubNormal, "Table 58");
            AssertLocation(minimum, AggregateBits.Calculated, "Table 58");

            DataValue minimumActualTime = RunSingle(ObjectIds.AggregateFunction_MinimumActualTime, raw, 0, 10, Config(false));
            AssertValue(minimumActualTime, 3, "§5.4.3.12");
            AssertTimestamp(minimumActualTime, 4);
            AssertCode(minimumActualTime, StatusCodes.UncertainDataSubNormal, "Table 60");
            AssertLocation(minimumActualTime, AggregateBits.Raw, "Table 60: the ActualTime result is a raw value and keeps Raw (Annex A, CTT)");

            DataValue maximumActualTime = RunSingle(ObjectIds.AggregateFunction_MaximumActualTime, raw, 0, 10, Config(false));
            AssertValue(maximumActualTime, 5, "§5.4.3.13");
            AssertTimestamp(maximumActualTime, 0);
            AssertCode(maximumActualTime, StatusCodes.UncertainDataSubNormal, "Table 61");
            AssertLocation(maximumActualTime, AggregateBits.Raw, "Table 61: the ActualTime result is a raw value and keeps Raw (Annex A, CTT)");
        }

        /// <summary>
        /// Tables 60, 61: "Calculated: Set Sometimes If the Status was set to Uncertain_DataSubNormal
        /// because of non-Good values in the interval". A Bad sample that carries a value is rejected
        /// from the selection, so the ActualTime result is Calculated (CTT 1.05.513 on Historian1 and
        /// HistorianInteger1, whose Bad samples have values); a Bad sample without a value keeps Raw
        /// (Annex A, see BadValueInIntervalSetsUncertainAndCalculated) (Mantis 11427).
        /// Data in [0 s, 10 s): Good 5@0, Bad 1@2, Bad 9@3, Good 3@4.
        /// </summary>
        [Test]
        public void BadValueWithDataInIntervalMakesActualTimeCalculated()
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(-10, 9, StatusCodes.Good),
                Raw(0, 5, StatusCodes.Good),
                Raw(2, 1, StatusCodes.Bad),
                Raw(3, 9, StatusCodes.Bad),
                Raw(4, 3, StatusCodes.Good),
                Raw(10, 9, StatusCodes.Good)
            };

            DataValue minimumActualTime = RunSingle(ObjectIds.AggregateFunction_MinimumActualTime, raw, 0, 10, Config(false));
            AssertValue(minimumActualTime, 3, "§5.4.3.12: the Bad value 1 is not a candidate");
            AssertTimestamp(minimumActualTime, 4);
            AssertCode(minimumActualTime, StatusCodes.UncertainDataSubNormal, "Table 60");
            AssertLocation(minimumActualTime, AggregateBits.Calculated, "Table 60: Uncertain because of non-Good values (CTT)");

            DataValue maximumActualTime = RunSingle(ObjectIds.AggregateFunction_MaximumActualTime, raw, 0, 10, Config(false));
            AssertValue(maximumActualTime, 5, "§5.4.3.13: the Bad value 9 is not a candidate");
            AssertTimestamp(maximumActualTime, 0);
            AssertCode(maximumActualTime, StatusCodes.UncertainDataSubNormal, "Table 61");
            AssertLocation(maximumActualTime, AggregateBits.Calculated, "Table 61: Uncertain because of non-Good values (CTT)");
        }

        /// <summary>
        /// Baseline without non-Good values: Table 58 "If the minimum value is on the start time the
        /// status code will be Good, Raw", otherwise Good, Calculated; Table 60 "Raw: Set Sometimes If a
        /// Good minimum value is returned".
        /// </summary>
        [Test]
        public void AllGoodDataUsesRawAndCalculatedBitsAsDefined()
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(-10, 9, StatusCodes.Good),
                Raw(0, 5, StatusCodes.Good),
                Raw(4, 3, StatusCodes.Good),
                Raw(10, 9, StatusCodes.Good)
            };

            DataValue minimum = RunSingle(ObjectIds.AggregateFunction_Minimum, raw, 0, 10, Config(false));
            AssertValue(minimum, 3, "§5.4.3.10");
            AssertCode(minimum, StatusCodes.Good, "Table 58");
            AssertLocation(minimum, AggregateBits.Calculated, "Table 58: minimum not on the start time");

            DataValue maximum = RunSingle(ObjectIds.AggregateFunction_Maximum, raw, 0, 10, Config(false));
            AssertValue(maximum, 5, "§5.4.3.11");
            AssertCode(maximum, StatusCodes.Good, "Table 59");
            AssertLocation(maximum, AggregateBits.Raw, "§5.4.3.11: maximum on the interval start time is Good, Raw");

            DataValue minimumActualTime = RunSingle(ObjectIds.AggregateFunction_MinimumActualTime, raw, 0, 10, Config(false));
            AssertTimestamp(minimumActualTime, 4);
            AssertCode(minimumActualTime, StatusCodes.Good, "Table 60");
            AssertLocation(minimumActualTime, AggregateBits.Raw, "Table 60: a Good minimum value is returned");
        }
        #endregion

        #region PercentDataBad / PercentDataGood (§5.4.3.2.1)
        /// <summary>
        /// §5.4.3.2.1: "The StatusCode for the interval is Bad if the ratio is greater than or equal to
        /// the PercentDataBad parameter ... The StatusCode for the interval is Good if the ratio is greater
        /// than or equal to the PercentDataGood parameter. If for an interval neither ratio applies then
        /// that interval is Uncertain_DataSubNormal."; Minimum2/Maximum2/Range2 use PercentTime
        /// (Tables 63, 64) and Range2 follows them (Table 67).
        /// Interval [0 s, 10 s), sloped: Good 5@0, Bad@1, Good 6@9, Good 7@20 (end bound 6.0909, Good).
        /// Regions (§5.4.3.2.2): [0,1) ends in a Bad value, so it is Uncertain; [1,9) is Bad; [9,10) is Good.
        /// TreatUncertainAsBad = false: 80 % Bad, 20 % Good. TreatUncertainAsBad = true: 90 % Bad, 10 % Good.
        /// With PercentDataGood + PercentDataBad = 100 (50/50) Annex A and the CTT never return Bad, so the
        /// interval is Uncertain.
        /// </summary>
        [TestCase(false, 100, 100, StatusCodes.UncertainDataSubNormal)]
        [TestCase(false, 80, 100, StatusCodes.Bad)]
        [TestCase(false, 81, 100, StatusCodes.UncertainDataSubNormal)]
        [TestCase(false, 50, 50, StatusCodes.UncertainDataSubNormal)]
        [TestCase(true, 100, 100, StatusCodes.UncertainDataSubNormal)]
        [TestCase(true, 90, 100, StatusCodes.Bad)]
        [TestCase(true, 91, 100, StatusCodes.UncertainDataSubNormal)]
        [TestCase(true, 50, 50, StatusCodes.UncertainDataSubNormal)]
        public void Minimum2Maximum2Range2UsePercentDataBadAndGood(bool treatUncertainAsBad, int percentBad, int percentGood, uint expectedCode)
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(-10, 5, StatusCodes.Good),
                Raw(0, 5, StatusCodes.Good),
                Raw(1, null, StatusCodes.Bad),
                Raw(9, 6, StatusCodes.Good),
                Raw(20, 7, StatusCodes.Good)
            };

            AggregateConfiguration configuration = Config(treatUncertainAsBad, (byte)percentBad, (byte)percentGood);
            DataValue minimum2 = RunSingle(ObjectIds.AggregateFunction_Minimum2, raw, 0, 10, configuration);
            DataValue maximum2 = RunSingle(ObjectIds.AggregateFunction_Maximum2, raw, 0, 10, configuration);
            DataValue range2 = RunSingle(ObjectIds.AggregateFunction_Range2, raw, 0, 10, configuration);

            foreach (DataValue result in new[] { minimum2, maximum2, range2 })
            {
                AssertTimestamp(result, 0);

                if (StatusCode.IsBad(expectedCode))
                {
                    Assert.That(StatusCode.IsBad(result.StatusCode), Is.True,
                        "§5.4.3.2.1: Bad ratio >= PercentDataBad gives Bad, got " + AggregateResultComparer.FormatStatus(result.StatusCode));
                    Assert.That(result.Value, Is.Null, "a Bad result has no value");
                }
                else
                {
                    AssertCode(result, expectedCode, "§5.4.3.2.1");
                }
            }

            if (!StatusCode.IsBad(expectedCode))
            {
                AssertValue(minimum2, 5, "Minimum2: the start bound 5 (raw value at 0 s) is the minimum (§5.4.3.15)");
                AssertValue(maximum2, 6 + 1.0 / 11.0, "Maximum2: the sloped end bound 6.0909 is the maximum (§5.4.3.16)");
                AssertValue(range2, 1 + 1.0 / 11.0, "Range2 = Maximum2 - Minimum2 (§5.4.3.19)");
            }
        }

        /// <summary>
        /// §4.2.1.2: "PercentDataGood ≥ (100 - PercentDataBad). If they are equal the result of the
        /// PercentDataGood calculation is used." With PercentDataBad = PercentDataGood = 50 both thresholds
        /// of §5.4.3.2.1 are met only when the Bad ratio is exactly 50 %, and the interval is then Good.
        /// Stepped, [0 s, 5 s): Good 10@0 and Bad@2.5 give 2.5 s Good and 2.5 s Bad; the next value (Good
        /// 20@6) is after the interval. With the Bad value at 2 s (60 % Bad) the interval is Uncertain:
        /// Annex A and the CTT never return Bad when PercentDataGood + PercentDataBad = 100.
        /// The backward read (StartTime 5 s, EndTime 0 s, CTT 004-03) covers the same interval and gives
        /// the same result (§5.4.2.2).
        /// </summary>
        [TestCase("TimeAverage2", 10.0, false)]
        [TestCase("Total2", 25.0, false)]
        [TestCase("Minimum2", 10.0, false)]
        [TestCase("Maximum2", 10.0, false)]
        [TestCase("MinimumActualTime2", 10.0, false)]
        [TestCase("MaximumActualTime2", 10.0, false)]
        [TestCase("Range2", 0.0, false)]
        [TestCase("TimeAverage2", 10.0, true)]
        [TestCase("Total2", 25.0, true)]
        [TestCase("Minimum2", 10.0, true)]
        [TestCase("Maximum2", 10.0, true)]
        [TestCase("MinimumActualTime2", 10.0, true)]
        [TestCase("MaximumActualTime2", 10.0, true)]
        [TestCase("Range2", 0.0, true)]
        public void PercentTimeTieUsesThePercentDataGoodResult(string aggregateName, double expectedValue, bool reverse)
        {
            NodeId aggregateId = Aggregators.GetIdForStandardAggregate(new QualifiedName(aggregateName));
            AggregateConfiguration configuration = Config(false, 50, 50);

            DataValue tie = RunSingle(aggregateId, GoodThenBad(2.5), 0, 5, configuration, true, reverse);
            AssertCode(tie, StatusCodes.Good, "§4.2.1.2: at the tie the PercentDataGood result is used");
            AssertValue(tie, expectedValue, aggregateName);

            DataValue bad = RunSingle(aggregateId, GoodThenBad(2), 0, 5, configuration, true, reverse);
            Assert.That(StatusCode.IsUncertain(bad.StatusCode), Is.True,
                "Annex A / CTT: with PercentDataGood + PercentDataBad = 100, 60 % Bad gives Uncertain, got " +
                AggregateResultComparer.FormatStatus(bad.StatusCode));
        }

        /// <summary>
        /// The count based status (PercentValues) gives the same tie result: Average over one Good and
        /// one Bad value with PercentDataBad = PercentDataGood = 50 is Good, in both time directions
        /// (CTT 003-03 forward, 004-03 backward). Both raw values lie strictly inside the interval: a
        /// forward interval holds its start time and a backward interval its later time (the request's
        /// StartTime side), so a raw value on the boundary at 0 s would only count in the forward read.
        /// Raw: Good 10@1, Bad@2.5, Good 20@6.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void PercentValuesTieUsesThePercentDataGoodResult(bool reverse)
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(-10, 10, StatusCodes.Good),
                Raw(1, 10, StatusCodes.Good),
                Raw(2.5, null, StatusCodes.Bad),
                Raw(6, 20, StatusCodes.Good)
            };

            DataValue average = RunSingle(ObjectIds.AggregateFunction_Average, raw, 0, 5, Config(false, 50, 50), true, reverse);
            AssertCode(average, StatusCodes.Good, "§4.2.1.2: at the tie the PercentDataGood result is used");
            AssertValue(average, 10, "§5.4.3.5: average of the Good values");
        }
        #endregion

        #region Interpolative on Stepped variables (§3.1.8, Table 52)
        /// <summary>
        /// §3.1.8, Interpolated Bounding Values using SteppedInterpolation: "The StatusCode is
        /// Uncertain_DataSubNormal if any Bad values exist between the before value and the timestamp";
        /// Table 52: "Uncertain if Bad values skipped". Data: Good 1@0, Bad@10, Good 3@20.
        /// At 15 s the Bad value lies between 0 s and the timestamp, so the stepped bound is Uncertain. At
        /// 10 s the Bad value is on the timestamp, not between, and at 9.999 s no Bad value lies between 0 s
        /// and the timestamp, so the stepped bound is Good (CTT 1.05.513 Interpolative 005-05/005-06 on the
        /// Stepped Historian3). Sloped at 10 s for comparison: 2, Uncertain (the line crosses the Bad value).
        /// </summary>
        [TestCase(true, 10.0, 1.0, StatusCodes.Good)]
        [TestCase(true, 15.0, 1.0, StatusCodes.UncertainDataSubNormal)]
        [TestCase(true, 9.999, 1.0, StatusCodes.Good)]
        [TestCase(false, 10.0, 2.0, StatusCodes.UncertainDataSubNormal)]
        public void InterpolativeBoundIsUncertainWhenABadValueIsSkipped(bool stepped, double seconds, double expectedValue, uint expectedCode)
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(0, 1, StatusCodes.Good),
                Raw(10, null, StatusCodes.Bad),
                Raw(20, 3, StatusCodes.Good),
                Raw(30, 4, StatusCodes.Good)
            };

            DataValue result = RunSingle(ObjectIds.AggregateFunction_Interpolative, raw, seconds, seconds + 1, Config(false), stepped);

            AssertTimestamp(result, seconds);
            AssertValue(result, expectedValue, "§3.1.8");
            AssertCode(result, expectedCode, "§3.1.8, Table 52");
            AssertLocation(result, AggregateBits.Interpolated, "Table 52: Interpolated unless a raw value is on the start time");
        }
        #endregion

        #region Integer data (Table 50, §3.1.8)
        /// <summary>
        /// TimeAverage/TimeAverage2/Total/Total2 return Double (Table 50) and the bound formula of §3.1.8
        /// gives a real number: Int32 raw 0@0 and 7@10 give the end bound 3.5 at 5 s, so the time average
        /// over [0 s, 5 s) is 1.75 and the total 8.75 (§5.4.3.8: Total = TimeAverage x ProcessingInterval in
        /// seconds). Truncating the bound to the raw data type would give 1.5 and 7.5.
        /// </summary>
        [Test]
        public void TimeAverageAndTotalUseRealBoundsForIntegerData()
        {
            List<DataValue> raw = new List<DataValue> {
                RawInt32(-10, 0, StatusCodes.Good),
                RawInt32(0, 0, StatusCodes.Good),
                RawInt32(10, 7, StatusCodes.Good),
                RawInt32(20, 7, StatusCodes.Good)
            };

            Assert.Multiple(() => {
                AssertValue(RunSingle(ObjectIds.AggregateFunction_TimeAverage, raw, 0, 5, Config(false)), 1.75, "TimeAverage");
                AssertValue(RunSingle(ObjectIds.AggregateFunction_TimeAverage2, raw, 0, 5, Config(false)), 1.75, "TimeAverage2");
                AssertValue(RunSingle(ObjectIds.AggregateFunction_Total, raw, 0, 5, Config(false)), 8.75, "Total");
                AssertValue(RunSingle(ObjectIds.AggregateFunction_Total2, raw, 0, 5, Config(false)), 8.75, "Total2");
            });
        }

        /// <summary>
        /// §5.4.3.6: TimeAverage "always uses a sloped line between points; TimeAverage2 uses a stepped or
        /// sloped line depending on the value of the Stepped Property". Total follows the Stepped Property, as
        /// Annex A (Historian3) and the CTT expect. Variable with Stepped = True, Good raw 0@0 and 10@10,
        /// interval [0 s, 10 s): TimeAverage 5 (sloped), Total 0 (stepped).
        /// </summary>
        [Test]
        public void TimeAverageIsSlopedAndTotalFollowsSteppedOnSteppedVariables()
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(-10, 0, StatusCodes.Good),
                Raw(0, 0, StatusCodes.Good),
                Raw(10, 10, StatusCodes.Good),
                Raw(20, 10, StatusCodes.Good)
            };

            Assert.Multiple(() => {
                AssertValue(RunSingle(ObjectIds.AggregateFunction_TimeAverage, raw, 0, 10, Config(false), true), 5, "TimeAverage (sloped)");
                AssertValue(
                    RunSingle(ObjectIds.AggregateFunction_Total, raw, 0, 10, Config(false), true),
                    0,
                    "Total (stepped, as Annex A Historian3 and the CTT calculate it)");
                AssertValue(RunSingle(ObjectIds.AggregateFunction_TimeAverage2, raw, 0, 10, Config(false), true), 0, "TimeAverage2 (stepped)");
                AssertValue(RunSingle(ObjectIds.AggregateFunction_Total2, raw, 0, 10, Config(false), true), 0, "Total2 (stepped)");
            });
        }

        /// <summary>
        /// Maximum2 returns the raw data type (Table 50). With sloped interpolation the End bound is used
        /// when it is the maximum (§5.4.3.16). Int32 raw 0@0 and 36@10 give the end bound 3.6 at 1 s.
        /// OPC 10000-4 §7.7.3 (Table 121): "Floating point values are rounded by adding 0.5 and truncating
        /// when they are converted to integer values", so the result is 4 (truncation would give 3).
        /// </summary>
        [Test]
        public void Maximum2RoundsAnInterpolatedBoundToTheRawDataType()
        {
            List<DataValue> raw = new List<DataValue> {
                RawInt32(-10, 0, StatusCodes.Good),
                RawInt32(0, 0, StatusCodes.Good),
                RawInt32(10, 36, StatusCodes.Good)
            };

            DataValue maximum2 = RunSingle(ObjectIds.AggregateFunction_Maximum2, raw, 0, 1, Config(false));
            Assert.That(maximum2.Value, Is.InstanceOf<int>(), "Table 50: Maximum2 returns the raw data type");
            Assert.That((int)maximum2.Value, Is.EqualTo(4), "3.6 rounded (OPC 10000-4 §7.7.3)");
            AssertCode(maximum2, StatusCodes.Good, "all regions Good");
        }

        /// <summary>
        /// Interpretation: Part 13 does not define how a real bound is converted to an integer raw data
        /// type; the only OPC UA rule is OPC 10000-4 §7.7.3 ("adding 0.5 and truncating"), which turns
        /// 2.5 into 3. .NET Convert.ToInt32 rounds half to even (2.5 becomes 2).
        /// </summary>
        [Test]
        [Category("Part13Interpretation")]
        public void Maximum2RoundsHalfUpLikeOpc10000Part4()
        {
            List<DataValue> raw = new List<DataValue> {
                RawInt32(-10, 0, StatusCodes.Good),
                RawInt32(0, 0, StatusCodes.Good),
                RawInt32(10, 25, StatusCodes.Good)
            };

            DataValue maximum2 = RunSingle(ObjectIds.AggregateFunction_Maximum2, raw, 0, 1, Config(false));
            Assert.That(maximum2.Value, Is.InstanceOf<int>(), "Table 50: Maximum2 returns the raw data type");
            Assert.That((int)maximum2.Value, Is.EqualTo(3), "2.5 + 0.5 truncated (OPC 10000-4 §7.7.3)");
        }

        /// <summary>
        /// Minimum2/Maximum2/Range2 return the raw data type also for an interval that holds only the
        /// interpolated bounds (no raw value): Int32 raw 0@0 and 7@10 give the bounds 0.7 at 1 s and 1.4
        /// at 2 s, both rounded half up to 1 (OPC 10000-4 §7.7.3) before the extremes are selected, so
        /// the two bounds are equal (MultipleValues) and Range2 is 0 (CTT 1.05.513 on HistorianInt1,
        /// Mantis 11494).
        /// </summary>
        [Test]
        public void Minimum2Maximum2RoundIntegerBoundsBeforeSelecting()
        {
            List<DataValue> raw = new List<DataValue> {
                RawInt32(-10, 0, StatusCodes.Good),
                RawInt32(0, 0, StatusCodes.Good),
                RawInt32(10, 7, StatusCodes.Good),
                RawInt32(20, 7, StatusCodes.Good)
            };

            DataValue minimum2 = RunSingle(ObjectIds.AggregateFunction_Minimum2, raw, 1, 2, Config(false));
            Assert.That(minimum2.Value, Is.InstanceOf<int>(), "Table 63: Minimum2 returns the raw data type");
            Assert.That((int)minimum2.Value, Is.EqualTo(1), "0.7 rounded half up");
            Assert.That(minimum2.StatusCode.AggregateBits & AggregateBits.MultipleValues, Is.EqualTo(AggregateBits.MultipleValues), "both bounds round to 1");

            DataValue maximum2 = RunSingle(ObjectIds.AggregateFunction_Maximum2, raw, 1, 2, Config(false));
            Assert.That(maximum2.Value, Is.InstanceOf<int>(), "Table 64: Maximum2 returns the raw data type");
            Assert.That((int)maximum2.Value, Is.EqualTo(1), "1.4 rounded half up");

            DataValue range2 = RunSingle(ObjectIds.AggregateFunction_Range2, raw, 1, 2, Config(false));
            Assert.That(Convert.ToDouble(range2.Value, CultureInfo.InvariantCulture), Is.EqualTo(0), "Range2 of the rounded bounds");
        }

        /// <summary>
        /// StartBound/EndBound/DeltaBounds return the raw data type (Tables 76-78): an interpolated bound
        /// of an Int32 variable is rounded half up (OPC 10000-4 §7.7.3), not truncated. Int32 raw 0@0 and
        /// 7@10 give the bounds 3.5 at 5 s and 4.2 at 6 s: StartBound 4 (truncation: 3), EndBound 4,
        /// DeltaBounds 0 (truncation: 1) (CTT 1.05.513 on HistorianInt1, Mantis 11494).
        /// </summary>
        [Test]
        public void BoundAggregatesRoundIntegerBoundsHalfUp()
        {
            List<DataValue> raw = new List<DataValue> {
                RawInt32(-10, 0, StatusCodes.Good),
                RawInt32(0, 0, StatusCodes.Good),
                RawInt32(10, 7, StatusCodes.Good),
                RawInt32(20, 7, StatusCodes.Good)
            };

            DataValue startBound = RunSingle(ObjectIds.AggregateFunction_StartBound, raw, 5, 6, Config(false));
            Assert.That(startBound.Value, Is.InstanceOf<int>(), "Table 76: StartBound returns the raw data type");
            Assert.That((int)startBound.Value, Is.EqualTo(4), "3.5 rounded half up");

            DataValue endBound = RunSingle(ObjectIds.AggregateFunction_EndBound, raw, 5, 6, Config(false));
            Assert.That((int)endBound.Value, Is.EqualTo(4), "4.2 rounded half up");

            DataValue deltaBounds = RunSingle(ObjectIds.AggregateFunction_DeltaBounds, raw, 5, 6, Config(false));
            Assert.That(Convert.ToDouble(deltaBounds.Value, CultureInfo.InvariantCulture), Is.EqualTo(0), "4 - 4");
        }

        /// <summary>
        /// A raw entry with StatusCode Bad_NoData marks the absence of data ("First archive entry, Point
        /// created" in every Annex A data set); it is neither a Bad value nor the start of data (Annex A:
        /// the first MaximumActualTime interval of Historian2/3/5 is "12:00:02, 10, Good, Partial";
        /// Mantis 11503). Raw: Bad_NoData@0, Good 10@2, Good 20@25.
        /// - Count [0 s, 1.8 s) holds only the Bad_NoData entry: Bad_NoData (the CTT expects Bad).
        /// - Count [1.8 s, 3.6 s) holds the first real value: Good, Calculated, Partial.
        /// - MaximumActualTime [0 s, 16 s): 10 at 2 s, Good and Partial, not Uncertain_DataSubNormal.
        /// </summary>
        [Test]
        public void BadNoDataRawEntryIsNeitherABadValueNorTheStartOfData()
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(0, null, StatusCodes.BadNoData),
                Raw(2, 10, StatusCodes.Good),
                Raw(25, 20, StatusCodes.Good)
            };

            List<DataValue> counts = AggregateCalculatorRunner.Run(
                ObjectIds.AggregateFunction_Count, raw, At(0), At(3.6), 1800, false, Config(false));

            Assert.That(counts.Count, Is.EqualTo(2), "two intervals");
            Assert.That(counts[0].StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadNoData), "only the Bad_NoData entry: no data");
            AssertValue(counts[1], 1, "the first real value is counted");
            AssertCode(counts[1], StatusCodes.Good, "Count [1.8 s, 3.6 s)");
            Assert.That(counts[1].StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated | AggregateBits.Partial),
                "the Partial bit is on the interval that holds the first real value");

            DataValue maximumActualTime = RunSingle(ObjectIds.AggregateFunction_MaximumActualTime, raw, 0, 16, Config(false));
            AssertValue(maximumActualTime, 10, "Annex A: 12:00:02, 10");
            AssertTimestamp(maximumActualTime, 2);
            AssertCode(maximumActualTime, StatusCodes.Good, "the Bad_NoData entry is not a Bad value");
            Assert.That(maximumActualTime.StatusCode.AggregateBits & AggregateBits.Partial, Is.EqualTo(AggregateBits.Partial),
                "data starts at 2 s");
        }
        #endregion

        #region Helpers
        private static DateTime At(double seconds)
        {
            return s_t0.AddMilliseconds(seconds * 1000.0);
        }

        private static DataValue Raw(double seconds, double? value, uint status)
        {
            Variant variant = value.HasValue ? new Variant(value.Value) : Variant.Null;
            return new DataValue(variant, status, At(seconds), At(seconds));
        }

        private static DataValue RawInt32(double seconds, int value, uint status)
        {
            return new DataValue(new Variant(value), status, At(seconds), At(seconds));
        }

        private static List<DataValue> GoodThenBad(double badSeconds)
        {
            return new List<DataValue> {
                Raw(-10, 10, StatusCodes.Good),
                Raw(0, 10, StatusCodes.Good),
                Raw(badSeconds, null, StatusCodes.Bad),
                Raw(6, 20, StatusCodes.Good),
                Raw(20, 20, StatusCodes.Good)
            };
        }

        private static AggregateConfiguration Config(bool treatUncertainAsBad, byte percentDataBad = 100, byte percentDataGood = 100)
        {
            return new AggregateConfiguration {
                UseServerCapabilitiesDefaults = false,
                TreatUncertainAsBad = treatUncertainAsBad,
                PercentDataBad = percentDataBad,
                PercentDataGood = percentDataGood,
                UseSlopedExtrapolation = false
            };
        }

        private static DataValue RunSingle(
            NodeId aggregateId,
            List<DataValue> raw,
            double startSeconds,
            double endSeconds,
            AggregateConfiguration configuration,
            bool stepped = false,
            bool reverse = false)
        {
            // A backward read swaps StartTime and EndTime; the processing interval stays positive.
            List<DataValue> results = AggregateCalculatorRunner.Run(
                aggregateId,
                raw,
                At(reverse ? endSeconds : startSeconds),
                At(reverse ? startSeconds : endSeconds),
                (endSeconds - startSeconds) * 1000.0,
                stepped,
                configuration);

            Assert.That(results.Count, Is.EqualTo(1), "one interval expected for " + aggregateId);
            TestContext.Out.WriteLine("{0}: {1}", Aggregators.GetNameForStandardAggregate(aggregateId), AggregateResultComparer.FormatActual(results[0], s_t0));
            return results[0];
        }

        private static void AssertValue(DataValue result, double expected, string because)
        {
            Assert.That(result.Value, Is.Not.Null, because + ": value expected, status " + AggregateResultComparer.FormatStatus(result.StatusCode));
            Assert.That(Convert.ToDouble(result.Value, CultureInfo.InvariantCulture), Is.EqualTo(expected).Within(1e-6), because);
        }

        private static void AssertTimestamp(DataValue result, double seconds)
        {
            Assert.That(result.SourceTimestamp, Is.EqualTo(At(seconds)), "timestamp");
        }

        private static void AssertCode(DataValue result, uint expected, string because)
        {
            Assert.That(
                AggregateResultComparer.FormatCode(result.StatusCode),
                Is.EqualTo(AggregateResultComparer.FormatCode(new StatusCode(expected))),
                because + " (status " + AggregateResultComparer.FormatStatus(result.StatusCode) + ")");
        }

        private static void AssertLocation(DataValue result, AggregateBits expected, string because)
        {
            Assert.That(
                result.StatusCode.AggregateBits & AggregateBits.DataSourceMask,
                Is.EqualTo(expected),
                because + " (status " + AggregateResultComparer.FormatStatus(result.StatusCode) + ")");
        }
        #endregion
    }
}
