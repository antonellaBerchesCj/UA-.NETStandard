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
using System.Linq;
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
        private static readonly bool[] s_onlyLastIsPartial = [false, false, true];

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

        #region Minimum, Maximum, ActualTime, Range (Tables 58-62, Mantis 11427)
        /// <summary>
        /// Tables 58, 60, 62: Minimum, MinimumActualTime and Range use the Good values; "If an
        /// Uncertain value is less than the minimum Good value the Status is Uncertain_DataSubNormal"
        /// (Mantis 11427 ~0025875). With TreatUncertainAsBad = True the Uncertain value counts as a Bad
        /// value, which gives the same status. Calculated is set because the status was set to
        /// Uncertain_DataSubNormal by a non-Good value.
        /// Data in [0 s, 10 s): Good 5@0, Uncertain 1@2, Good 3@4. Good minimum 3@4, range 5 - 3 = 2.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UncertainValueBelowGoodMinimumMakesTheGoodMinimumUncertain(bool treatUncertainAsBad)
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
            AssertValue(minimum, 3, "§5.4.3.10: the minimum Good value");
            AssertTimestamp(minimum, 0);
            AssertCode(minimum, StatusCodes.UncertainDataSubNormal, "Table 58");
            AssertLocation(minimum, AggregateBits.Calculated, "Table 58");

            DataValue minimumActualTime = RunSingle(ObjectIds.AggregateFunction_MinimumActualTime, raw, 0, 10, configuration);
            AssertValue(minimumActualTime, 3, "§5.4.3.12: the minimum Good value");
            AssertTimestamp(minimumActualTime, 4);
            AssertCode(minimumActualTime, StatusCodes.UncertainDataSubNormal, "Table 60");
            AssertLocation(minimumActualTime, AggregateBits.Calculated, "Table 60");

            DataValue range = RunSingle(ObjectIds.AggregateFunction_Range, raw, 0, 10, configuration);
            AssertValue(range, 2, "§5.4.3.14: maximum minus minimum of the Good values");
            AssertCode(range, StatusCodes.UncertainDataSubNormal, "Table 62");
            AssertLocation(range, AggregateBits.Calculated, "Table 62: Calculated Set Always");
        }

        /// <summary>
        /// Tables 59, 61, 62: "If an Uncertain value is greater than the maximum Good value the Status
        /// is Uncertain_DataSubNormal"; the result is the Good maximum (Mantis 11427 ~0025875).
        /// Data in [0 s, 10 s): Good 3@0, Uncertain 8@2, Good 5@4. Good maximum 5@4, range 2.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UncertainValueAboveGoodMaximumMakesTheGoodMaximumUncertain(bool treatUncertainAsBad)
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
            AssertValue(maximum, 5, "§5.4.3.11: the maximum Good value");
            AssertTimestamp(maximum, 0);
            AssertCode(maximum, StatusCodes.UncertainDataSubNormal, "Table 59");
            AssertLocation(maximum, AggregateBits.Calculated, "Table 59");

            DataValue maximumActualTime = RunSingle(ObjectIds.AggregateFunction_MaximumActualTime, raw, 0, 10, configuration);
            AssertValue(maximumActualTime, 5, "§5.4.3.13: the maximum Good value");
            AssertTimestamp(maximumActualTime, 4);
            AssertCode(maximumActualTime, StatusCodes.UncertainDataSubNormal, "Table 61");
            AssertLocation(maximumActualTime, AggregateBits.Calculated, "Table 61");

            DataValue range = RunSingle(ObjectIds.AggregateFunction_Range, raw, 0, 10, configuration);
            AssertValue(range, 2, "§5.4.3.14: maximum minus minimum of the Good values");
            AssertCode(range, StatusCodes.UncertainDataSubNormal, "Table 62");
        }

        /// <summary>
        /// Tables 58, 60, 61: "If Bad values exist then the Status is Uncertain_DataSubNormal" and the
        /// Calculated bit is set "if the Status was set to Uncertain_DataSubNormal because of non-Good
        /// values in the interval", for the ActualTime aggregates too (Mantis 11427 ~0025875).
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
            AssertLocation(minimumActualTime, AggregateBits.Calculated, "Table 60");

            DataValue maximumActualTime = RunSingle(ObjectIds.AggregateFunction_MaximumActualTime, raw, 0, 10, Config(false));
            AssertValue(maximumActualTime, 5, "§5.4.3.13");
            AssertTimestamp(maximumActualTime, 0);
            AssertCode(maximumActualTime, StatusCodes.UncertainDataSubNormal, "Table 61");
            AssertLocation(maximumActualTime, AggregateBits.Calculated, "Table 61");
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
        /// Table 52: "Uncertain if Bad values skipped", and "Returned Uncertain_DataSubNormal if any Bad
        /// value(s) was/were skipped to calculate the bounding value". Data: Good 1@0, Bad@10, Good 3@20.
        /// Stepped: at 15 s the Bad value lies between 0 s and the timestamp, and at 10 s the Bad value on
        /// the timestamp is skipped, so both bounds are Uncertain; at 9.999 s no Bad value was skipped, so
        /// the bound is Good. Sloped at 10 s: 2, Uncertain (the line crosses the Bad value).
        /// </summary>
        [TestCase(true, 10.0, 1.0, StatusCodes.UncertainDataSubNormal)]
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
        /// §5.4.3.6 and §5.4.3.8: TimeAverage and Total always use a sloped line between points (Total =
        /// TimeAverage x ProcessingInterval); TimeAverage2 and Total2 follow the Stepped Property.
        /// Variable with Stepped = True, Good raw 0@0 and 10@10, interval [0 s, 10 s): TimeAverage 5 and
        /// Total 5 x 10 s = 50 (sloped), TimeAverage2 and Total2 0 (stepped).
        /// </summary>
        [Test]
        public void TimeAverageAndTotalAreSlopedOnSteppedVariables()
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(-10, 0, StatusCodes.Good),
                Raw(0, 0, StatusCodes.Good),
                Raw(10, 10, StatusCodes.Good),
                Raw(20, 10, StatusCodes.Good)
            };

            Assert.Multiple(() => {
                AssertValue(RunSingle(ObjectIds.AggregateFunction_TimeAverage, raw, 0, 10, Config(false), true), 5, "TimeAverage (sloped)");
                AssertValue(RunSingle(ObjectIds.AggregateFunction_Total, raw, 0, 10, Config(false), true), 50, "Total (sloped, §5.4.3.8)");
                AssertValue(RunSingle(ObjectIds.AggregateFunction_TimeAverage2, raw, 0, 10, Config(false), true), 0, "TimeAverage2 (stepped)");
                AssertValue(RunSingle(ObjectIds.AggregateFunction_Total2, raw, 0, 10, Config(false), true), 0, "Total2 (stepped)");
            });
        }

        /// <summary>
        /// Maximum2 returns the raw data type (Table 50). With sloped interpolation the End bound is used
        /// when it is the maximum (§5.4.3.16). Int32 raw 0@0 and 36@10 give the end bound 3.6 at 1 s,
        /// which TypeInfo.Cast rounds to 4 (as in 1.5.378; truncation would give 3).
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
            Assert.That((int)maximum2.Value, Is.EqualTo(4), "3.6 rounded");
            AssertCode(maximum2, StatusCodes.Good, "all regions Good");
        }

        /// <summary>
        /// Interpretation: Part 13 will say that a real bound is rounded to an integer raw data type,
        /// without an algorithm (Mantis 11499); 11518 asks for the rule at .5 and is unanswered. The
        /// calculators keep the 1.5.378 conversion, TypeInfo.Cast, which rounds to the nearest value
        /// and ties to even: the bound 2.5 becomes 2.
        /// </summary>
        [Test]
        [Category("Part13Interpretation")]
        public void Maximum2ConvertsTheBoundWithTypeInfoCast()
        {
            List<DataValue> raw = new List<DataValue> {
                RawInt32(-10, 0, StatusCodes.Good),
                RawInt32(0, 0, StatusCodes.Good),
                RawInt32(10, 25, StatusCodes.Good)
            };

            DataValue maximum2 = RunSingle(ObjectIds.AggregateFunction_Maximum2, raw, 0, 1, Config(false));
            Assert.That(maximum2.Value, Is.InstanceOf<int>(), "Table 50: Maximum2 returns the raw data type");
            Assert.That((int)maximum2.Value, Is.EqualTo(2), "2.5 rounded to the even neighbour, as TypeInfo.Cast does");
        }

        /// <summary>
        /// Minimum2/Maximum2/Range2 return the raw data type also for an interval that holds only the
        /// interpolated bounds (no raw value): Int32 raw 0@0 and 7@10 give the bounds 0.7 at 1 s and 1.4
        /// at 2 s, both rounded to 1 (TypeInfo.Cast, as in 1.5.378) before the extremes are selected, so
        /// the two bounds are equal (MultipleValues) and Range2 is 0 (Mantis 11494).
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
            Assert.That((int)minimum2.Value, Is.EqualTo(1), "0.7 rounded");
            Assert.That(minimum2.StatusCode.AggregateBits & AggregateBits.MultipleValues, Is.EqualTo(AggregateBits.MultipleValues), "both bounds round to 1");

            DataValue maximum2 = RunSingle(ObjectIds.AggregateFunction_Maximum2, raw, 1, 2, Config(false));
            Assert.That(maximum2.Value, Is.InstanceOf<int>(), "Table 64: Maximum2 returns the raw data type");
            Assert.That((int)maximum2.Value, Is.EqualTo(1), "1.4 rounded");

            DataValue range2 = RunSingle(ObjectIds.AggregateFunction_Range2, raw, 1, 2, Config(false));
            Assert.That(Convert.ToDouble(range2.Value, CultureInfo.InvariantCulture), Is.EqualTo(0), "Range2 of the rounded bounds");
        }
        /// <summary>
        /// StartBound/EndBound/DeltaBounds return the raw data type (Tables 76-78): an interpolated bound
        /// of an Int32 variable is rounded to the nearest value with TypeInfo.Cast (as in 1.5.378), not
        /// truncated. Int32 raw 0@0 and 8@10 give the bounds 4.8 at 6 s and 5.6 at 7 s: StartBound 5
        /// (truncation: 4), EndBound 6 (truncation: 5), DeltaBounds 6 - 5 = 1 (Mantis 11494). Values away
        /// from .5 are used because Part 13 does not fix the rule at .5 (Mantis 11518).
        /// </summary>
        [Test]
        public void BoundAggregatesRoundIntegerBoundsInsteadOfTruncating()
        {
            List<DataValue> raw = new List<DataValue> {
                RawInt32(-10, 0, StatusCodes.Good),
                RawInt32(0, 0, StatusCodes.Good),
                RawInt32(10, 8, StatusCodes.Good),
                RawInt32(20, 8, StatusCodes.Good)
            };

            DataValue startBound = RunSingle(ObjectIds.AggregateFunction_StartBound, raw, 6, 7, Config(false));
            Assert.That(startBound.Value, Is.InstanceOf<int>(), "Table 76: StartBound returns the raw data type");
            Assert.That((int)startBound.Value, Is.EqualTo(5), "4.8 rounded");

            DataValue endBound = RunSingle(ObjectIds.AggregateFunction_EndBound, raw, 6, 7, Config(false));
            Assert.That((int)endBound.Value, Is.EqualTo(6), "5.6 rounded");

            DataValue deltaBounds = RunSingle(ObjectIds.AggregateFunction_DeltaBounds, raw, 6, 7, Config(false));
            Assert.That(deltaBounds.Value, Is.InstanceOf<int>(), "Table 78: DeltaBounds returns the raw data type");
            Assert.That((int)deltaBounds.Value, Is.EqualTo(1), "6 - 5");
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

        #region Configuration, Partial and Good-only aggregates (§4.2.1.2, §5.3.3.2, §5.4.3.27, §5.4.3.37-40)
        /// <summary>
        /// §4.2.1.2: "PercentDataGood ≥ (100 - PercentDataBad)"; "If the values entered for
        /// PercentDataGood and PercentDataBad do not result in a valid calculation (e.g. Bad = 80;
        /// Good = 0) the result will have a StatusCode of Bad_AggregateInvalidInputs", which is also
        /// returned "if the value of PercentDataGood or PercentDataBad exceed 100".
        /// </summary>
        [TestCase(80, 0)]
        [TestCase(30, 60)]
        [TestCase(101, 100)]
        [TestCase(100, 101)]
        public void InvalidPercentDataGivesBadAggregateInvalidInputs(int percentDataBad, int percentDataGood)
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(0, 10, StatusCodes.Good),
                Raw(5, 20, StatusCodes.Good),
                Raw(20, 30, StatusCodes.Good)
            };
            AggregateConfiguration configuration = Config(false, (byte)percentDataBad, (byte)percentDataGood);

            foreach (NodeId aggregateId in new[] {
                ObjectIds.AggregateFunction_Average,
                ObjectIds.AggregateFunction_TimeAverage2,
                ObjectIds.AggregateFunction_Count,
                ObjectIds.AggregateFunction_Minimum })
            {
                DataValue result = RunSingle(aggregateId, raw, 0, 10, configuration);
                AssertCode(result, StatusCodes.BadAggregateInvalidInputs, "§4.2.1.2");
                Assert.That(result.Value, Is.Null, "a Bad result has no value");
            }
        }

        /// <summary>
        /// Valid settings, including PercentDataGood = 100 - PercentDataBad, are calculated (§4.2.1.2).
        /// </summary>
        [TestCase(50, 50)]
        [TestCase(20, 90)]
        [TestCase(100, 100)]
        public void ValidPercentDataIsCalculated(int percentDataBad, int percentDataGood)
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(0, 10, StatusCodes.Good),
                Raw(5, 20, StatusCodes.Good),
                Raw(20, 30, StatusCodes.Good)
            };

            DataValue result = RunSingle(ObjectIds.AggregateFunction_Average, raw, 0, 10, Config(false, (byte)percentDataBad, (byte)percentDataGood));

            AssertCode(result, StatusCodes.Good, "two Good values");
            AssertValue(result, 15, "(10 + 20) / 2");
        }

        /// <summary>
        /// §5.3.3.2: "If the start/end time does not result in an even interval and there is
        /// additional data beyond the end time then the last interval will have a Partial bit."
        /// Good raw values every 5 s from -20 s to 40 s; request 0 s to 25 s (or 25 s to 0 s) with a
        /// 10 s interval: the last interval is 5 s long, data exists beyond it, and only it is Partial.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ShortenedLastIntervalWithDataBeyondTheEndIsPartial(bool reverse)
        {
            List<DataValue> raw = new List<DataValue>();

            for (int seconds = -20; seconds <= 40; seconds += 5)
            {
                raw.Add(Raw(seconds, seconds, StatusCodes.Good));
            }

            List<DataValue> results = AggregateCalculatorRunner.Run(
                ObjectIds.AggregateFunction_Count,
                raw,
                At(reverse ? 25 : 0),
                At(reverse ? 0 : 25),
                10000,
                false,
                Config(false));

            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results.Select(r => (r.StatusCode.AggregateBits & AggregateBits.Partial) != 0),
                Is.EqualTo(s_onlyLastIsPartial), "only the shortened last interval is Partial");
        }

        /// <summary>
        /// Average uses Good raw values only (§5.4.3.5; upstream a07f4eb1a). The value based status
        /// (§5.4.3.2.1) counts a Good value as Good and a Bad value as Bad; an Uncertain value counts as
        /// Bad only with TreatUncertainAsBad = True (§4.2.1.2: "Uncertain is treated as defined above")
        /// and is never Good. Raw: Good 10 at 0 s, Uncertain 70 at 5 s; interval [0 s, 10 s).
        /// </summary>
        [TestCase(false, 100, StatusCodes.UncertainDataSubNormal)]
        [TestCase(false, 50, StatusCodes.UncertainDataSubNormal)]
        [TestCase(true, 100, StatusCodes.UncertainDataSubNormal)]
        [TestCase(true, 50, StatusCodes.Bad)]
        public void AverageUsesGoodValuesAndCountsUncertainAsBadOnlyWithTreatUncertainAsBad(
            bool treatUncertainAsBad,
            int percentDataBad,
            uint expectedCode)
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(0, 10, StatusCodes.Good),
                Raw(5, 70, StatusCodes.Uncertain),
                Raw(20, 10, StatusCodes.Good)
            };

            DataValue result = RunSingle(ObjectIds.AggregateFunction_Average, raw, 0, 10, Config(treatUncertainAsBad, (byte)percentDataBad));

            AssertCode(result, expectedCode, "one Good and one Uncertain value");

            if (expectedCode == StatusCodes.Bad)
            {
                Assert.That(result.Value, Is.Null, "a Bad result has no value");
            }
            else
            {
                AssertValue(result, 10, "only the Good value");
            }
        }

        /// <summary>
        /// §5.4.3.27: Delta is the difference between the earliest and latest Good raw values; "The
        /// status is Uncertain_DataSubNormal if non-Good values are skipped while looking for the first
        /// or last values." TreatUncertainAsBad does not change this (Mantis 11495). Interval
        /// [0 s, 10 s): Good 10 at 0 s, Good 20, Uncertain 50; the Uncertain value lies between the
        /// Good values (not skipped) or after the last Good value (skipped).
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void DeltaUsesGoodValuesOnly(bool treatUncertainAsBad, bool uncertainIsLast)
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(0, 10, StatusCodes.Good),
                uncertainIsLast ? Raw(4, 20, StatusCodes.Good) : Raw(4, 50, StatusCodes.Uncertain),
                uncertainIsLast ? Raw(8, 50, StatusCodes.Uncertain) : Raw(8, 20, StatusCodes.Good),
                Raw(20, 30, StatusCodes.Good)
            };

            DataValue result = RunSingle(ObjectIds.AggregateFunction_Delta, raw, 0, 10, Config(treatUncertainAsBad));

            AssertValue(result, 10, "20 - 10, the Uncertain value is not used");
            AssertCode(result, uncertainIsLast ? StatusCodes.UncertainDataSubNormal : StatusCodes.Good,
                uncertainIsLast ? "the Uncertain value was skipped to find the last value" : "nothing was skipped");
        }

        /// <summary>
        /// §5.4.3.37-40: "X is each Good raw value in the interval"; "If any non-Good values were
        /// ignored, the Aggregate quality is uncertain/subnormal". Interval [0 s, 10 s): Good 10,
        /// Uncertain 50, Good 20: the Good values {10, 20} give population variance 25 and sample
        /// variance 50, with or without TreatUncertainAsBad (Mantis 3301, 11495).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void StandardDeviationAndVarianceUseGoodValuesOnly(bool treatUncertainAsBad)
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(0, 10, StatusCodes.Good),
                Raw(4, 50, StatusCodes.Uncertain),
                Raw(8, 20, StatusCodes.Good),
                Raw(20, 30, StatusCodes.Good)
            };

            var expected = new Dictionary<NodeId, double> {
                [ObjectIds.AggregateFunction_VariancePopulation] = 25,
                [ObjectIds.AggregateFunction_VarianceSample] = 50,
                [ObjectIds.AggregateFunction_StandardDeviationPopulation] = 5,
                [ObjectIds.AggregateFunction_StandardDeviationSample] = Math.Sqrt(50)
            };

            foreach (KeyValuePair<NodeId, double> item in expected)
            {
                DataValue result = RunSingle(item.Key, raw, 0, 10, Config(treatUncertainAsBad));
                AssertValue(result, item.Value, "Good values 10 and 20 only");
                AssertCode(result, StatusCodes.UncertainDataSubNormal, "the Uncertain value was ignored");
            }
        }
        #endregion

        #region TimeAverage2/Total2 non-Good data and the region at the end of data (§5.4.3.7, §5.4.3.9, §5.4.3.31)
        /// <summary>
        /// §5.4.3.7: TimeAverage2 leaves all non-Good data out and shortens the interval by it; a
        /// sub-interval that ends at a value that is left out uses its Good starting value. Total2 is
        /// TimeAverage2 x the duration of the Good data (§5.4.3.9). The definition names non-Good
        /// data, so TreatUncertainAsBad does not change the value (as for Count, Mantis 11495).
        /// Sloped raw data: Good 10 at 0 s, Good 20 at 10 s, Uncertain 100 at 20 s, Good 30 at 30 s.
        /// [0 s, 20 s): 10 to 20 sloped (150) plus 20 held to the Uncertain value (200): 350 / 20 s =
        /// 17.5 (the sloped line to 100 would give 37.5). The status follows §5.4.3.2.1: Good with
        /// TreatUncertainAsBad = False, Uncertain_DataSubNormal with True (10 s of 20 s Bad).
        /// </summary>
        [TestCase(false, StatusCodes.Good)]
        [TestCase(true, StatusCodes.UncertainDataSubNormal)]
        public void TimeAverage2AndTotal2LeaveUncertainDataOut(bool treatUncertainAsBad, uint expectedCode)
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(0, 10, StatusCodes.Good),
                Raw(10, 20, StatusCodes.Good),
                Raw(20, 100, StatusCodes.Uncertain),
                Raw(30, 30, StatusCodes.Good)
            };

            DataValue timeAverage2 = RunSingle(ObjectIds.AggregateFunction_TimeAverage2, raw, 0, 20, Config(treatUncertainAsBad));
            DataValue total2 = RunSingle(ObjectIds.AggregateFunction_Total2, raw, 0, 20, Config(treatUncertainAsBad));

            AssertValue(timeAverage2, 17.5, "the Uncertain value is left out");
            AssertCode(timeAverage2, expectedCode, "§5.4.3.2.1");
            AssertValue(total2, 350, "17.5 x 20 s of Good data");
            AssertCode(total2, expectedCode, "§5.4.3.2.1");
        }

        /// <summary>
        /// An interval whose only data is Uncertain has no Good data to average, so TimeAverage2 and
        /// Total2 have no value and are Bad (§5.4.3.7), whatever TreatUncertainAsBad is. Same data,
        /// interval [20 s, 30 s): the Uncertain value at the start and nothing else.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TimeAverage2AndTotal2WithoutGoodDataAreBad(bool treatUncertainAsBad)
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(0, 10, StatusCodes.Good),
                Raw(10, 20, StatusCodes.Good),
                Raw(20, 100, StatusCodes.Uncertain),
                Raw(30, 30, StatusCodes.Good)
            };

            foreach (NodeId aggregateId in new[] { ObjectIds.AggregateFunction_TimeAverage2, ObjectIds.AggregateFunction_Total2 })
            {
                DataValue result = RunSingle(aggregateId, raw, 20, 30, Config(treatUncertainAsBad));
                AssertCode(result, StatusCodes.Bad, "no Good data");
                Assert.That(result.Value, Is.Null, "a Bad result has no value");
            }
        }

        /// <summary>
        /// The region that starts at the last raw value lasts until the end of the interval like any
        /// other region (§5.4.3.31, §5.4.3.32; DurationInState uses the same regions). For
        /// TimeAverage2/Total2 the missing end bound is Bad_NoData (Tables 55, 57), and a
        /// sub-interval that ends at a Bad value uses the Good starting value (§5.4.3.7). Raw: Good 0
        /// at 0 s, Good 5 at 10 s (the last value); interval [0 s, 20 s), Partial (§5.3.3.2).
        /// </summary>
        [Test]
        public void TheRegionAfterTheLastValueRunsToTheEndOfTheInterval()
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(0, 0, StatusCodes.Good),
                Raw(10, 5, StatusCodes.Good)
            };

            AssertValue(RunSingle(ObjectIds.AggregateFunction_DurationGood, raw, 0, 20, Config(false)), 20000, "DurationGood: 0 s to 20 s");
            AssertValue(RunSingle(ObjectIds.AggregateFunction_PercentGood, raw, 0, 20, Config(false)), 100, "PercentGood");
            AssertValue(RunSingle(ObjectIds.AggregateFunction_DurationInStateNonZero, raw, 0, 20, Config(false)), 10000, "the value 5 from 10 s to 20 s");
            AssertValue(RunSingle(ObjectIds.AggregateFunction_DurationInStateZero, raw, 0, 20, Config(false)), 10000, "the value 0 from 0 s to 10 s");

            DataValue total2 = RunSingle(ObjectIds.AggregateFunction_Total2, raw, 0, 20, Config(false));
            AssertValue(total2, 75, "0 to 5 sloped (25) plus 5 held for 10 s (50)");
            Assert.That(total2.StatusCode.AggregateBits & AggregateBits.Partial, Is.EqualTo(AggregateBits.Partial), "§5.3.3.2");
            AssertValue(RunSingle(ObjectIds.AggregateFunction_TimeAverage2, raw, 0, 20, Config(false)), 3.75, "75 / 20 s");
        }
        #endregion

        #region Duration rounding (Mantis 11499)
        /// <summary>
        /// Mantis 11499 (UA call 2026-10-20): Part 13 will say that Duration values are rounded,
        /// without fixing the algorithm. DurationGood and DurationBad are rounded to whole
        /// milliseconds, half to even; PercentGood and PercentBad use the unrounded durations.
        /// Data: Good at 0, Bad at 2.5 ms, Good at 500 ms and 2 s; interval [0, 1 s). Good 2.5 + 500 =
        /// 502.5 ms -> 502, Bad 497.5 ms -> 498; 50.25 % and 49.75 %.
        /// </summary>
        [Test]
        public void DurationGoodAndBadAreRoundedToWholeMilliseconds()
        {
            List<DataValue> raw = new List<DataValue> {
                AtTicks(0, 1, StatusCodes.Good),
                AtTicks(25000, null, StatusCodes.Bad),
                AtTicks(5000000, 1, StatusCodes.Good),
                AtTicks(20000000, 1, StatusCodes.Good)
            };

            AssertValue(RunSingle(ObjectIds.AggregateFunction_DurationGood, raw, 0, 1, Config(false)), 502, "502.5 ms, half to even");
            AssertValue(RunSingle(ObjectIds.AggregateFunction_DurationBad, raw, 0, 1, Config(false)), 498, "497.5 ms, half to even");
            AssertValue(RunSingle(ObjectIds.AggregateFunction_PercentGood, raw, 0, 1, Config(false)), 50.25, "from 502.5 ms");
            AssertValue(RunSingle(ObjectIds.AggregateFunction_PercentBad, raw, 0, 1, Config(false)), 49.75, "from 497.5 ms");
        }

        /// <summary>
        /// DurationInStateZero and DurationInStateNonZero are Duration values too (Mantis 11499).
        /// Data: Good 1 at 0, Good 0 at 3.2345 ms, Good 1 at 600 ms and 2 s; interval [0, 1 s).
        /// NonZero 3.2345 + 400 = 403.2345 ms -> 403, Zero 596.7655 ms -> 597.
        /// </summary>
        [Test]
        public void DurationInStateIsRoundedToWholeMilliseconds()
        {
            List<DataValue> raw = new List<DataValue> {
                AtTicks(0, 1, StatusCodes.Good),
                AtTicks(32345, 0, StatusCodes.Good),
                AtTicks(6000000, 1, StatusCodes.Good),
                AtTicks(20000000, 1, StatusCodes.Good)
            };

            AssertValue(RunSingle(ObjectIds.AggregateFunction_DurationInStateNonZero, raw, 0, 1, Config(false), stepped: true), 403, "403.2345 ms");
            AssertValue(RunSingle(ObjectIds.AggregateFunction_DurationInStateZero, raw, 0, 1, Config(false), stepped: true), 597, "596.7655 ms");
        }

        /// <summary>A raw value at a time in ticks (100 ns) after the start: exact below 1 ms.</summary>
        private static DataValue AtTicks(long ticks, double? value, uint status)
        {
            Variant variant = value.HasValue ? new Variant(value.Value) : Variant.Null;
            DateTime time = s_t0.AddTicks(ticks);
            return new DataValue(variant, status, time, time);
        }
        #endregion

        #region AnnotationCount (§5.4.3.20, Table 68)
        /// <summary>
        /// §5.4.3.20: AnnotationCount "returns a count of all Annotations in the interval"; Table 68:
        /// Good, Calculated, "Good unless the interval is before the start of data or after the end of
        /// data" (Bad_NoData). Raw values every 5 s from 0 to 30 s (one Bad); Annotations at 2, 3, 10,
        /// 12 and 33 s, queued out of order. 10 s intervals forward: [0, 10) 2, [10, 20) 2 (the
        /// Annotation at 10 s starts it), [20, 30) 0, [30, 40) 1 (end of data, Partial), [40, 50) after
        /// the end of data.
        /// </summary>
        [Test]
        public void AnnotationCountCountsTheAnnotationsInTheInterval()
        {
            List<DataValue> results = AggregateCalculatorRunner.Run(
                ObjectIds.AggregateFunction_AnnotationCount,
                AnnotatedRaw(),
                At(0),
                At(50),
                10000,
                false,
                Config(false),
                AnnotationTimes());

            Assert.That(results.Count, Is.EqualTo(5));
            AssertAnnotationCount(results[0], 2, AggregateBits.Calculated, "[0, 10)");
            AssertAnnotationCount(results[1], 2, AggregateBits.Calculated, "[10, 20)");
            AssertAnnotationCount(results[2], 0, AggregateBits.Calculated, "[20, 30): raw values are not counted");
            AssertAnnotationCount(results[3], 1, AggregateBits.Calculated | AggregateBits.Partial, "[30, 40) holds the end of data");
            AssertCode(results[4], StatusCodes.BadNoData, "[40, 50) is after the end of data");
        }

        /// <summary>
        /// A backward read uses the intervals (StartTime, EndTime] of the raw values (§5.4.2.2), so the
        /// Annotation at 10 s belongs to (0, 10]. (30, 40] and (40, 50] are after the end of data (30 s):
        /// Bad_NoData, also for the Annotation at 33 s.
        /// </summary>
        [Test]
        public void AnnotationCountUsesTheBackwardIntervals()
        {
            List<DataValue> results = AggregateCalculatorRunner.Run(
                ObjectIds.AggregateFunction_AnnotationCount,
                AnnotatedRaw(),
                At(50),
                At(0),
                10000,
                false,
                Config(false),
                AnnotationTimes());

            Assert.That(results.Count, Is.EqualTo(5));
            AssertCode(results[0], StatusCodes.BadNoData, "(40, 50]");
            AssertCode(results[1], StatusCodes.BadNoData, "(30, 40]");
            AssertValue(results[2], 0, "(20, 30]");
            AssertValue(results[3], 1, "(10, 20]");
            AssertValue(results[4], 3, "(0, 10]");
        }

        /// <summary>
        /// Table 68: an interval before the start of data is Bad_NoData, also when it holds an
        /// Annotation. Without Annotations an interval within the data is 0; 1.5.378 counted the raw
        /// values (3).
        /// </summary>
        [Test]
        public void AnnotationCountIsBadNoDataBeforeTheStartOfDataAndZeroWithoutAnnotations()
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(20, 1, StatusCodes.Good),
                Raw(25, 2, StatusCodes.Good),
                Raw(30, 3, StatusCodes.Good)
            };

            List<DataValue> results = AggregateCalculatorRunner.Run(
                ObjectIds.AggregateFunction_AnnotationCount,
                raw,
                At(0),
                At(40),
                20000,
                false,
                Config(false),
                new[] { At(5) });

            Assert.That(results.Count, Is.EqualTo(2));
            AssertCode(results[0], StatusCodes.BadNoData, "[0, 20) is before the start of data");
            AssertAnnotationCount(results[1], 0, AggregateBits.Calculated | AggregateBits.Partial, "[20, 40) holds no Annotation");
        }

        private static List<DataValue> AnnotatedRaw()
        {
            return new List<DataValue> {
                Raw(0, 1, StatusCodes.Good),
                Raw(5, 2, StatusCodes.Good),
                Raw(10, 3, StatusCodes.Good),
                Raw(15, null, StatusCodes.Bad),
                Raw(20, 5, StatusCodes.Good),
                Raw(25, 6, StatusCodes.Good),
                Raw(30, 7, StatusCodes.Good)
            };
        }

        private static List<DateTime> AnnotationTimes()
        {
            return new List<DateTime> { At(12), At(2), At(33), At(10), At(3) };
        }

        private static void AssertAnnotationCount(DataValue result, int expected, AggregateBits bits, string because)
        {
            AssertCode(result, StatusCodes.Good, because);
            Assert.That(result.Value, Is.EqualTo(expected), because);
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(bits), because);
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
