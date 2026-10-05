/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for the OPC UA Part 13 aggregate fixes backported from the
    /// master/master378 branches (CTT 1.05.513). The calculators are driven directly,
    /// the same way the server history read does.
    /// </summary>
    [TestFixture, Category("Aggregators")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class AggregateCttTests
    {
        private static readonly DateTime s_baseTime = new DateTime(2025, 1, 1, 0, 0, 0);

        #region Status calculation (Part 13 §4.2.1.2, §5.4.3.2.1)
        /// <summary>
        /// The value-based status counts Uncertain values as Good when TreatUncertainAsBad is
        /// false and as Bad otherwise.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CountValueBasedStatusHonorsTreatUncertainAsBad(bool treatUncertainAsBad)
        {
            // Two Good values and one Uncertain value: 100% Good without TreatUncertainAsBad,
            // otherwise 67% Good and 33% Bad, which meets neither threshold.
            uint expectedCodeBits = treatUncertainAsBad
                ? StatusCodes.UncertainDataSubNormal
                : StatusCodes.Good;
            List<DataValue> rawValues = new List<DataValue> {
                CreateValue(1, StatusCodes.Good, 0),
                CreateValue(2, StatusCodes.UncertainSubstituteValue, 5),
                CreateValue(3, StatusCodes.Good, 10),
                CreateValue(4, StatusCodes.Good, 20)
            };

            List<DataValue> results = RunDirect(
                ObjectIds.AggregateFunction_Count,
                rawValues,
                s_baseTime,
                AtSeconds(15),
                15000,
                CreateConfiguration(treatUncertainAsBad));

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(ToInt32(results[0]), Is.EqualTo(2));
            Assert.That(results[0].StatusCode.CodeBits, Is.EqualTo(expectedCodeBits));
            Assert.That(results[0].StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
        }

        /// <summary>
        /// Time-based status calculation treats Uncertain regions as Bad when
        /// TreatUncertainAsBad is true; the Bad result keeps the Calculated bit.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void Minimum2TreatsUncertainRegionsAsBadWhenConfigured(bool treatUncertainAsBad)
        {
            uint expectedCodeBits = treatUncertainAsBad
                ? StatusCodes.Bad
                : StatusCodes.UncertainDataSubNormal;

            // Interval [15 s, 35 s): the start bound is Bad (the raw value at 10 s is Bad), the
            // region 20-30 s ends on an Uncertain value and the region 30-35 s is Uncertain.
            // With TreatUncertainAsBad every region is Bad (100% >= PercentDataBad); otherwise
            // 25% is Bad and 75% Good, which meets neither threshold.
            List<DataValue> rawValues = new List<DataValue> {
                CreateValue(0, StatusCodes.Good, 0),
                CreateValue(1, StatusCodes.BadDataUnavailable, 10),
                CreateValue(2, StatusCodes.Good, 20),
                CreateValue(3, StatusCodes.UncertainSubstituteValue, 30),
                CreateValue(4, StatusCodes.Good, 40),
                CreateValue(5, StatusCodes.Good, 50)
            };

            List<DataValue> results = RunDirect(
                ObjectIds.AggregateFunction_Minimum2,
                rawValues,
                AtSeconds(15),
                AtSeconds(35),
                20000,
                CreateConfiguration(treatUncertainAsBad));

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].StatusCode.CodeBits, Is.EqualTo(expectedCodeBits));
            Assert.That(
                results[0].StatusCode.AggregateBits & AggregateBits.Calculated,
                Is.EqualTo(AggregateBits.Calculated));
        }

        /// <summary>
        /// The duration-in-state status takes the region ending at an Uncertain simple end bound
        /// into account (Part 13 §5.4.3.2.2), while the duration still includes that region
        /// because it starts at a Good raw value.
        /// </summary>
        [TestCase(true, true, 15000.0)]
        [TestCase(false, true, 5000.0)]
        [TestCase(true, false, 15000.0)]
        [TestCase(false, false, 5000.0)]
        public void DurationInStateUncertainEndBoundMakesLastRegionUncertain(
            bool zero,
            bool treatUncertainAsBad,
            double expected)
        {
            // Interval [5 s, 25 s): the simple end bound at 25 s is Uncertain_DataSubNormal
            // because the raw value after it (30 s) is Bad.
            List<DataValue> rawValues = new List<DataValue> {
                CreateValue(0, StatusCodes.Good, 0),
                CreateValue(0, StatusCodes.Good, 10),
                CreateValue(1, StatusCodes.Good, 20),
                CreateValue(1, StatusCodes.BadDataUnavailable, 30),
                CreateValue(1, StatusCodes.Good, 40)
            };
            uint expectedCodeBits = treatUncertainAsBad
                ? StatusCodes.UncertainDataSubNormal
                : StatusCodes.Good;

            List<DataValue> results = RunDirect(
                zero ? ObjectIds.AggregateFunction_DurationInStateZero : ObjectIds.AggregateFunction_DurationInStateNonZero,
                rawValues,
                AtSeconds(5),
                AtSeconds(25),
                20000,
                CreateConfiguration(treatUncertainAsBad));

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(ToDouble(results[0]), Is.EqualTo(expected).Within(0.000001));
            Assert.That(results[0].StatusCode.CodeBits, Is.EqualTo(expectedCodeBits));
            Assert.That(results[0].StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
        }
        #endregion

        #region Partial bit (Part 13 §5.3.3.2)
        /// <summary>
        /// The interval overlapping the start or end of data carries the Partial bit in both
        /// time directions.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void PartialBitMarksIntervalOverlappingDataEdge(bool reverse, bool dataEndsInsideRange)
        {
            List<DataValue> rawValues = new List<DataValue> {
                CreateValue(1, StatusCodes.Good, 5),
                CreateValue(2, StatusCodes.Good, 10),
                CreateValue(3, StatusCodes.Good, dataEndsInsideRange ? 15 : 20)
            };
            DateTime startTime = reverse ? AtSeconds(20) : s_baseTime;
            DateTime endTime = reverse ? s_baseTime : AtSeconds(20);

            List<DataValue> results = RunDirect(
                ObjectIds.AggregateFunction_Count,
                rawValues,
                startTime,
                endTime,
                10000,
                CreateConfiguration(false));

            // The early interval overlaps the start of data; the late interval overlaps the end
            // of data only when the data ends inside the range. Backward reads return the late
            // interval first.
            AssertPartialBits(results, new[] { true, dataEndsInsideRange }, reverse);
        }

        /// <summary>
        /// A raw value rejected because it arrives out of order does not move the tracked start
        /// of data, so the interval overlapping the real start keeps its Partial bit.
        /// </summary>
        [Test]
        public void RejectedOutOfOrderValueDoesNotMoveStartOfData()
        {
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Count,
                s_baseTime,
                AtSeconds(20),
                10000,
                false,
                CreateConfiguration(false));

            Assert.That(calculator.QueueRawValue(CreateValue(1, StatusCodes.Good, 5)), Is.True);
            Assert.That(calculator.QueueRawValue(CreateValue(2, StatusCodes.Good, 10)), Is.True);
            Assert.That(calculator.QueueRawValue(CreateValue(0, StatusCodes.Good, 0)), Is.False,
                "an earlier value after later ones must be rejected");
            Assert.That(calculator.QueueRawValue(CreateValue(3, StatusCodes.Good, 15)), Is.True);
            Assert.That(calculator.QueueRawValue(CreateValue(4, StatusCodes.Good, 20)), Is.True);

            AssertPartialBits(ReadAll(calculator), new[] { true, false }, false);
        }
        #endregion

        #region Count aggregates
        /// <summary>
        /// Transition counts include uncertain values only when TreatUncertainAsBad is false
        /// (Part 13 §4.2.1.2, §5.4.3.24).
        /// </summary>
        [TestCase(false, 22)]
        [TestCase(true, 20)]
        public void NumberOfTransitionsCountUncertainValuesOnlyWhenNotTreatedAsBad(
            bool treatUncertainAsBad,
            int expectedTransitions)
        {
            List<DataValue> rawValues = new List<DataValue>(25);
            for (int index = 0; index <= 24; index++)
            {
                StatusCode status = StatusCodes.Good;

                if (index % 10 == 7)
                {
                    status = StatusCodes.BadDataUnavailable;
                }
                else if (index % 10 == 9)
                {
                    status = StatusCodes.UncertainSubstituteValue;
                }

                rawValues.Add(CreateValue(index, status, index));
            }

            List<DataValue> results = RunDirect(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                rawValues,
                s_baseTime,
                AtSeconds(24),
                24000,
                CreateConfiguration(treatUncertainAsBad));

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(ToInt32(results[0]), Is.EqualTo(expectedTransitions));
            Assert.That(results[0].SourceTimestamp, Is.EqualTo(s_baseTime));
            Assert.That(results[0].StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(results[0].StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
        }
        #endregion

        #region Duration and status aggregates
        /// <summary>
        /// The first DurationGood/DurationBad region takes the status of the raw value at or
        /// before the interval start rather than of the simple bound (Part 13 §5.4.3.31-.32).
        /// </summary>
        [TestCase("DurationBad", true, 15000.0)]
        [TestCase("DurationGood", true, 15000.0)]
        [TestCase("DurationBad", false, 10000.0)]
        [TestCase("DurationGood", false, 20000.0)]
        [TestCase("PercentBad", true, 50.0)]
        [TestCase("PercentGood", true, 50.0)]
        [TestCase("PercentBad", false, 100.0 / 3.0)]
        [TestCase("PercentGood", false, 200.0 / 3.0)]
        public void DurationFirstRegionUsesRawStatusBeforeInterval(
            string aggregateName,
            bool treatUncertainAsBad,
            double expected)
        {
            // Interval [5 s, 35 s): the simple start bound is Uncertain because Bad data follows
            // the Good value at 0 s, but the first region (5-10 s) is Good. 10-20 s is Bad,
            // 20-30 s Good, and 30-35 s Uncertain (Bad only with TreatUncertainAsBad).
            List<DataValue> rawValues = new List<DataValue> {
                CreateValue(0, StatusCodes.Good, 0),
                CreateValue(1, StatusCodes.BadDataUnavailable, 10),
                CreateValue(2, StatusCodes.Good, 20),
                CreateValue(3, StatusCodes.UncertainSubstituteValue, 30),
                CreateValue(4, StatusCodes.Good, 40)
            };
            DateTime startTime = AtSeconds(5);

            List<DataValue> results = RunDirect(
                GetAggregateId(aggregateName),
                rawValues,
                startTime,
                AtSeconds(35),
                30000,
                CreateConfiguration(treatUncertainAsBad));

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(ToDouble(results[0]), Is.EqualTo(expected).Within(0.000001));
            Assert.That(results[0].SourceTimestamp, Is.EqualTo(startTime));
            Assert.That(results[0].StatusCode.CodeBits, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
        }

        /// <summary>
        /// A backward WorstQuality2 read returns the forward results for the same intervals,
        /// with the start bound taken at the early time (Part 13 §5.4.2.2, §5.4.3.36).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void WorstQuality2BackwardMatchesForwardIntervals(bool reverse)
        {
            // No raw value sits on an interval boundary, so the forward [5,15) and [15,25) and
            // backward (5,15] and (15,25] intervals contain the same raw values.
            List<DataValue> rawValues = new List<DataValue> {
                CreateValue(0, StatusCodes.Good, 1),
                CreateValue(1, StatusCodes.UncertainSubstituteValue, 9),
                CreateValue(2, StatusCodes.Good, 12),
                CreateValue(3, StatusCodes.BadDataUnavailable, 14),
                CreateValue(4, StatusCodes.Good, 18),
                CreateValue(5, StatusCodes.Good, 21),
                CreateValue(6, StatusCodes.Good, 30)
            };

            List<DataValue> results = RunDirect(
                ObjectIds.AggregateFunction_WorstQuality2,
                rawValues,
                reverse ? AtSeconds(25) : AtSeconds(5),
                reverse ? AtSeconds(5) : AtSeconds(25),
                10000,
                CreateConfiguration(false));

            // [5,15) has the Uncertain start bound, Uncertain, Good and Bad values, so the worst
            // is the single BadDataUnavailable; [15,25) starts on a Bad_NoData bound.
            uint[] expectedChronological = new uint[] { StatusCodes.BadDataUnavailable, StatusCodes.BadNoData };
            Assert.That(results.Count, Is.EqualTo(expectedChronological.Length));

            for (int index = 0; index < results.Count; index++)
            {
                int interval = reverse ? results.Count - 1 - index : index;
                Assert.That(
                    ToStatusCode(results[index]).Code,
                    Is.EqualTo(expectedChronological[interval]),
                    "worst quality at result " + index);
                Assert.That(
                    results[index].StatusCode.AggregateBits,
                    Is.EqualTo(AggregateBits.Calculated),
                    "aggregate bits at result " + index);
            }
        }

        /// <summary>
        /// A backward WorstQuality read reports the chronologically first of two equally severe
        /// statuses, as the forward calculation over the same interval does (Part 13 §5.4.2.2).
        /// </summary>
        [Test]
        public void WorstQualityBackwardSelectsChronologicallyFirstStatus()
        {
            List<DataValue> rawValues = new List<DataValue> {
                CreateValue(0, StatusCodes.Good, 0),
                CreateValue(1, StatusCodes.BadOutOfRange, 2),
                CreateValue(2, StatusCodes.BadSensorFailure, 8),
                CreateValue(3, StatusCodes.Good, 12)
            };

            List<DataValue> results = RunDirect(
                ObjectIds.AggregateFunction_WorstQuality,
                rawValues,
                AtSeconds(10),
                s_baseTime,
                10000,
                CreateConfiguration(false));

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(ToStatusCode(results[0]).Code, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(
                results[0].StatusCode.AggregateBits,
                Is.EqualTo(AggregateBits.Calculated | AggregateBits.MultipleValues));
        }
        #endregion

        #region Minimum, Maximum and Range (Part 13 §5.4.3.10-§5.4.3.19)
        /// <summary>
        /// An uncertain sample outside the good range lowers quality without replacing the good
        /// extrema.
        /// </summary>
        [TestCase(Objects.AggregateFunction_Minimum, 1.0, 5.0)]
        [TestCase(Objects.AggregateFunction_Maximum, 9.0, 5.0)]
        [TestCase(Objects.AggregateFunction_Range, 1.0, 0.0)]
        [TestCase(Objects.AggregateFunction_Range, 9.0, 0.0)]
        [TestCase(Objects.AggregateFunction_MinimumActualTime, 1.0, 5.0)]
        [TestCase(Objects.AggregateFunction_MaximumActualTime, 9.0, 5.0)]
        public void MinMaxPreservesGoodExtremaAndReportsUncertainInputs(
            uint aggregateTypeId,
            double uncertainValue,
            double expectedValue)
        {
            DateTime start = s_baseTime;
            DateTime middle = start.AddMilliseconds(5000);
            DateTime end = start.AddMilliseconds(10000);
            List<DataValue> rawValues = new List<DataValue> {
                new DataValue(new Variant(5.0), StatusCodes.Good, start, start),
                new DataValue(new Variant(uncertainValue), StatusCodes.Uncertain, middle, middle),
                new DataValue(new Variant(7.0), StatusCodes.Good, end, end)
            };

            List<DataValue> results = RunDirect(
                new NodeId(aggregateTypeId), rawValues, start, end, 10000, CreateConfiguration(false));

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(ToDouble(results[0]), Is.EqualTo(expectedValue));
            Assert.That(results[0].StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(results[0].StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
            Assert.That(results[0].SourceTimestamp, Is.EqualTo(start));
        }

        /// <summary>
        /// Uncertain samples within the good range preserve the good result and its timestamp.
        /// </summary>
        [TestCase(Objects.AggregateFunction_Minimum, 5.0, 0, AggregateBits.Raw)]
        [TestCase(Objects.AggregateFunction_Maximum, 10.0, 0, AggregateBits.Calculated)]
        [TestCase(Objects.AggregateFunction_Range, 5.0, 0, AggregateBits.Calculated)]
        [TestCase(Objects.AggregateFunction_MinimumActualTime, 5.0, 0, AggregateBits.Raw)]
        [TestCase(Objects.AggregateFunction_MaximumActualTime, 10.0, 2000, AggregateBits.Raw)]
        public void UncertainInsideGoodRangePreservesGoodQuality(
            uint aggregateTypeId,
            double expectedValue,
            int timestampOffset,
            AggregateBits expectedBits)
        {
            DateTime start = s_baseTime;
            DateTime maximum = start.AddMilliseconds(2000);
            DateTime middle = start.AddMilliseconds(5000);
            DateTime end = start.AddMilliseconds(10000);
            List<DataValue> rawValues = new List<DataValue> {
                new DataValue(new Variant(5.0), StatusCodes.Good, start, start),
                new DataValue(new Variant(10.0), StatusCodes.Good, maximum, maximum),
                new DataValue(new Variant(7.0), StatusCodes.Uncertain, middle, middle),
                new DataValue(new Variant(8.0), StatusCodes.Good, end, end)
            };

            List<DataValue> results = RunDirect(
                new NodeId(aggregateTypeId), rawValues, start, end, 10000, CreateConfiguration(false));

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(ToDouble(results[0]), Is.EqualTo(expectedValue));
            Assert.That(results[0].StatusCode.CodeBits, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].StatusCode.AggregateBits, Is.EqualTo(expectedBits));
            Assert.That(results[0].SourceTimestamp, Is.EqualTo(start.AddMilliseconds(timestampOffset)));
        }

        /// <summary>
        /// A Bad sample makes the ActualTime results Uncertain and Calculated (Part 13 §5.4.3.12,
        /// §5.4.3.13).
        /// </summary>
        [TestCase(Objects.AggregateFunction_MinimumActualTime, 5.0)]
        [TestCase(Objects.AggregateFunction_MaximumActualTime, 10.0)]
        public void BadSampleMakesActualTimeResultCalculated(uint aggregateTypeId, double expectedValue)
        {
            DateTime start = s_baseTime;
            DateTime minimum = start.AddMilliseconds(1000);
            DateTime maximum = start.AddMilliseconds(3000);
            DateTime bad = start.AddMilliseconds(6000);
            DateTime end = start.AddMilliseconds(10000);
            List<DataValue> rawValues = new List<DataValue> {
                new DataValue(new Variant(5.0), StatusCodes.Good, minimum, minimum),
                new DataValue(new Variant(10.0), StatusCodes.Good, maximum, maximum),
                new DataValue(new Variant(0.0), StatusCodes.BadDataUnavailable, bad, bad),
                new DataValue(new Variant(8.0), StatusCodes.Good, end, end)
            };

            List<DataValue> results = RunDirect(
                new NodeId(aggregateTypeId), rawValues, start, end, 10000, CreateConfiguration(true));

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(ToDouble(results[0]), Is.EqualTo(expectedValue));
            Assert.That(results[0].StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(
                results[0].StatusCode.AggregateBits & AggregateBits.Calculated,
                Is.EqualTo(AggregateBits.Calculated));
        }

        /// <summary>
        /// An interval without good samples reports no data rather than a fabricated extremum.
        /// </summary>
        [TestCase(Objects.AggregateFunction_Minimum, false)]
        [TestCase(Objects.AggregateFunction_Maximum, false)]
        [TestCase(Objects.AggregateFunction_Range, false)]
        [TestCase(Objects.AggregateFunction_MinimumActualTime, false)]
        [TestCase(Objects.AggregateFunction_MaximumActualTime, false)]
        [TestCase(Objects.AggregateFunction_Minimum, true)]
        public void AllUncertainIntervalDoesNotInventAGoodExtremum(
            uint aggregateTypeId,
            bool treatUncertainAsBad)
        {
            DateTime start = s_baseTime;
            DateTime end = start.AddMilliseconds(10000);
            List<DataValue> rawValues = new List<DataValue> {
                new DataValue(new Variant(5.0), StatusCodes.Uncertain, start, start),
                new DataValue(new Variant(7.0), StatusCodes.Good, end, end)
            };

            List<DataValue> results = RunDirect(
                new NodeId(aggregateTypeId), rawValues, start, end, 10000, CreateConfiguration(treatUncertainAsBad));

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(results[0].Value, Is.Null);
        }
        #endregion

        #region Standard deviation and variance (Part 13 §5.4.3.37-§5.4.3.40)
        /// <summary>
        /// The population aggregates divide by n and the sample aggregates by n - 1, both over
        /// the Good raw values in the interval (no bounds).
        /// </summary>
        [TestCase("StandardDeviationPopulation", 2.0)]
        [TestCase("VariancePopulation", 4.0)]
        [TestCase("StandardDeviationSample", 2.1380899352993950)]
        [TestCase("VarianceSample", 32.0 / 7.0)]
        public void StdDevAndVarianceUseSampleOrPopulationDivisor(string aggregateName, double expected)
        {
            // 2, 4, 4, 4, 5, 5, 7, 9: mean 5, sum of squared deviations 32.
            double[] samples = new double[] { 2, 4, 4, 4, 5, 5, 7, 9 };
            List<DataValue> rawValues = new List<DataValue>();

            for (int ii = 0; ii < samples.Length; ii++)
            {
                rawValues.Add(CreateValue(samples[ii], StatusCodes.Good, ii));
            }

            rawValues.Add(CreateValue(0, StatusCodes.Good, samples.Length));

            List<DataValue> results = RunDirect(
                GetAggregateId(aggregateName),
                rawValues,
                s_baseTime,
                AtSeconds(samples.Length),
                samples.Length * 1000,
                CreateConfiguration(false));

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(ToDouble(results[0]), Is.EqualTo(expected).Within(0.000001));
            Assert.That(results[0].StatusCode.CodeBits, Is.EqualTo(StatusCodes.Good));
        }
        #endregion

        #region Helpers
        private static List<DataValue> RunDirect(
            NodeId aggregateId,
            List<DataValue> rawValues,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            AggregateConfiguration configuration)
        {
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                aggregateId,
                startTime,
                endTime,
                processingInterval,
                false,
                configuration);

            Assert.That(calculator, Is.Not.Null, "calculator for " + aggregateId);

            if (endTime < startTime)
            {
                for (int index = rawValues.Count - 1; index >= 0; index--)
                {
                    Assert.That(calculator.QueueRawValue(rawValues[index]), Is.True, "raw value " + index);
                }
            }
            else
            {
                for (int index = 0; index < rawValues.Count; index++)
                {
                    Assert.That(calculator.QueueRawValue(rawValues[index]), Is.True, "raw value " + index);
                }
            }

            return ReadAll(calculator);
        }

        private static List<DataValue> ReadAll(IAggregateCalculator calculator)
        {
            List<DataValue> results = new List<DataValue>();
            DataValue value = calculator.GetProcessedValue(true);

            while (value != null)
            {
                results.Add(value);
                value = calculator.GetProcessedValue(true);
            }

            return results;
        }

        private static void AssertPartialBits(
            List<DataValue> results,
            bool[] expectedPartialChronological,
            bool reverse)
        {
            Assert.That(results.Count, Is.EqualTo(expectedPartialChronological.Length));

            for (int index = 0; index < results.Count; index++)
            {
                int interval = reverse ? results.Count - 1 - index : index;
                bool isPartial = (results[index].StatusCode.AggregateBits & AggregateBits.Partial) != 0;
                Assert.That(
                    isPartial,
                    Is.EqualTo(expectedPartialChronological[interval]),
                    "Partial bit at result " + index + " (chronological interval " + interval + ")");
            }
        }

        private static NodeId GetAggregateId(string aggregateName)
        {
            switch (aggregateName)
            {
                case "DurationGood": return ObjectIds.AggregateFunction_DurationGood;
                case "DurationBad": return ObjectIds.AggregateFunction_DurationBad;
                case "PercentGood": return ObjectIds.AggregateFunction_PercentGood;
                case "PercentBad": return ObjectIds.AggregateFunction_PercentBad;
                case "StandardDeviationPopulation": return ObjectIds.AggregateFunction_StandardDeviationPopulation;
                case "StandardDeviationSample": return ObjectIds.AggregateFunction_StandardDeviationSample;
                case "VariancePopulation": return ObjectIds.AggregateFunction_VariancePopulation;
                case "VarianceSample": return ObjectIds.AggregateFunction_VarianceSample;
                default: throw new ArgumentOutOfRangeException(nameof(aggregateName), aggregateName, null);
            }
        }

        private static AggregateConfiguration CreateConfiguration(bool treatUncertainAsBad)
        {
            AggregateConfiguration configuration = new AggregateConfiguration();
            configuration.UseServerCapabilitiesDefaults = false;
            configuration.TreatUncertainAsBad = treatUncertainAsBad;
            configuration.PercentDataBad = 100;
            configuration.PercentDataGood = 100;
            configuration.UseSlopedExtrapolation = false;
            return configuration;
        }

        private static DateTime AtSeconds(int seconds)
        {
            return s_baseTime.AddMilliseconds(seconds * 1000);
        }

        private static DataValue CreateValue(double value, StatusCode statusCode, int timestampSeconds)
        {
            DateTime timestamp = AtSeconds(timestampSeconds);
            return new DataValue(new Variant(value), statusCode, timestamp, timestamp);
        }

        private static double ToDouble(DataValue value)
        {
            Assert.That(value.Value, Is.Not.Null, "value");
            return Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
        }

        private static int ToInt32(DataValue value)
        {
            Assert.That(value.Value, Is.Not.Null, "value");
            return Convert.ToInt32(value.Value, CultureInfo.InvariantCulture);
        }

        private static StatusCode ToStatusCode(DataValue value)
        {
            Assert.That(value.Value, Is.InstanceOf<StatusCode>(), "value");
            return (StatusCode)value.Value;
        }
        #endregion
    }
}
