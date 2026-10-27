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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for the shared helpers on the <see cref="AggregateCalculator"/> base class,
    /// in particular the public stepped/sloped interpolation routines and the reverse-time
    /// and sloped-extrapolation processing branches.
    /// </summary>
    [TestFixture]
    [Category("Aggregators")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AggregateCalculatorCoverageTests
    {
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        private static DataValue Good(double value, DateTime timestamp)
        {
            return new DataValue(new Variant(value), StatusCodes.Good, timestamp, timestamp);
        }

        [Test]
        public void SteppedInterpolateWithGoodEarlyBoundReturnsInterpolatedGood()
        {
            var timestamp = new DateTime(2024, 1, 1, 0, 0, 5);
            DataValue earlyBound = Good(42.0, new DateTime(2024, 1, 1, 0, 0, 0));

            DataValue result = AggregateCalculator.SteppedInterpolate(timestamp, earlyBound);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(42.0));
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Interpolated), Is.True);
        }

        [Test]
        public void SteppedInterpolateWithBadEarlyBoundReturnsBadNoData()
        {
            var timestamp = new DateTime(2024, 1, 1, 0, 0, 5);
            var earlyBound = new DataValue(
                new Variant(1.0), StatusCodes.Bad, timestamp, timestamp);

            DataValue result = AggregateCalculator.SteppedInterpolate(timestamp, earlyBound);

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadNoData));
        }

        [Test]
        public void SteppedInterpolateWithUncertainEarlyBoundReturnsUncertain()
        {
            var timestamp = new DateTime(2024, 1, 1, 0, 0, 5);
            var earlyBound = new DataValue(
                new Variant(1.0), StatusCodes.UncertainDataSubNormal, timestamp, timestamp);

            DataValue result = AggregateCalculator.SteppedInterpolate(timestamp, earlyBound);

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Interpolated), Is.True);
        }

        [Test]
        public void SlopedInterpolateWithBadEarlyBoundReturnsBadNoData()
        {
            var timestamp = new DateTime(2024, 1, 1, 0, 0, 5);
            var earlyBound = new DataValue(
                new Variant(1.0), StatusCodes.Bad,
                new DateTime(2024, 1, 1, 0, 0, 0), new DateTime(2024, 1, 1, 0, 0, 0));
            DataValue lateBound = Good(10.0, new DateTime(2024, 1, 1, 0, 0, 10));

            DataValue result = AggregateCalculator.SlopedInterpolate(timestamp, earlyBound, lateBound);

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadNoData));
        }

        [Test]
        public void SlopedInterpolateWithBadLateBoundFallsBackToStepped()
        {
            var timestamp = new DateTime(2024, 1, 1, 0, 0, 5);
            DataValue earlyBound = Good(7.0, new DateTime(2024, 1, 1, 0, 0, 0));
            var lateBound = new DataValue(
                new Variant(10.0), StatusCodes.Bad,
                new DateTime(2024, 1, 1, 0, 0, 10), new DateTime(2024, 1, 1, 0, 0, 10));

            DataValue result = AggregateCalculator.SlopedInterpolate(timestamp, earlyBound, lateBound);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(7.0));
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
        }

        [Test]
        public void SlopedInterpolateWithGoodBoundsInterpolatesLinearly()
        {
            var early = new DateTime(2024, 1, 1, 0, 0, 0);
            var late = new DateTime(2024, 1, 1, 0, 0, 10);
            var timestamp = new DateTime(2024, 1, 1, 0, 0, 5);
            DataValue earlyBound = Good(0.0, early);
            DataValue lateBound = Good(100.0, late);

            DataValue result = AggregateCalculator.SlopedInterpolate(timestamp, earlyBound, lateBound);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(50.0).Within(0.0001));
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Interpolated), Is.True);
        }

        [Test]
        public void SlopedInterpolateWithUncertainBoundMarksUncertain()
        {
            var early = new DateTime(2024, 1, 1, 0, 0, 0);
            var late = new DateTime(2024, 1, 1, 0, 0, 10);
            var timestamp = new DateTime(2024, 1, 1, 0, 0, 5);
            var earlyBound = new DataValue(
                new Variant(0.0), StatusCodes.UncertainDataSubNormal, early, early);
            DataValue lateBound = Good(100.0, late);

            DataValue result = AggregateCalculator.SlopedInterpolate(timestamp, earlyBound, lateBound);

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
        }

        [Test]
        public void SlopedInterpolateWithNonNumericValuesReturnsBadTypeMismatch()
        {
            var early = new DateTime(2024, 1, 1, 0, 0, 0);
            var late = new DateTime(2024, 1, 1, 0, 0, 10);
            var timestamp = new DateTime(2024, 1, 1, 0, 0, 5);
            var earlyBound = new DataValue(new Variant("not-a-number"), StatusCodes.Good, early, early);
            var lateBound = new DataValue(new Variant("also-not"), StatusCodes.Good, late, late);

            DataValue result = AggregateCalculator.SlopedInterpolate(timestamp, earlyBound, lateBound);

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void SlopedInterpolateWithNullBoundReturnsBadTypeMismatch()
        {
            var timestamp = new DateTime(2024, 1, 1, 0, 0, 5);
            DataValue bound = Good(10.0, new DateTime(2024, 1, 1, 0, 0, 10));

            Assert.That(AggregateCalculator.SlopedInterpolate(timestamp, null, bound).StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(AggregateCalculator.SlopedInterpolate(timestamp, bound, null).StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void SlopedInterpolateConvertsIntegersWithTypeInfoCast()
        {
            var early = new DateTime(2024, 1, 1, 0, 0, 0);
            var late = new DateTime(2024, 1, 1, 0, 0, 10);
            var earlyBound = new DataValue(new Variant(2), StatusCodes.Good, early, early);
            var lateBound = new DataValue(new Variant(3), StatusCodes.Good, late, late);

            // 2.5 is converted like TypeInfo.Cast (Convert.ToInt32): to the even neighbour.
            DataValue result = AggregateCalculator.SlopedInterpolate(new DateTime(2024, 1, 1, 0, 0, 5), earlyBound, lateBound);

            Assert.That(result.Value, Is.EqualTo(2));
            Assert.That(result.WrappedValue.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
        }

        /// <summary>
        /// A Good sample without a value cannot be converted to a number. The region it starts is
        /// Bad_TypeMismatch and left out, so the result is Uncertain over the remaining 20 s, as in
        /// 1.5.378. The first version of the Part 13 change used 0 for the failed conversion and kept
        /// the Good status (11.67 Good for TimeAverage). Data: 10 at 0 s, a Good sample without a
        /// value at 10 s, 20 at 20 s and 30 s; interval [0, 30 s).
        /// </summary>
        [TestCase("TimeAverage", 12.5)]
        [TestCase("TimeAverage2", 12.5)]
        [TestCase("Total", 250.0)]
        [TestCase("Total2", 250.0)]
        public void GoodSampleWithoutAValueIsNotCalculatedAsZero(string aggregateName, double expected)
        {
            var start = new DateTime(2024, 1, 1, 0, 0, 0);
            DataValue[] values =
            [
                Good(10.0, start),
                new DataValue(Variant.Null, StatusCodes.Good, start.AddSeconds(10), start.AddSeconds(10)),
                Good(20.0, start.AddSeconds(20)),
                Good(20.0, start.AddSeconds(30))
            ];

            DataValue result = ComputeSingleInterval(aggregateName, values, start, start.AddSeconds(30), false);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(expected).Within(1e-9));
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
        }

        /// <summary>
        /// All-Good data in one interval (ProcessingInterval 0) that starts 1 s before the first raw
        /// value stays Good in both directions, as in 1.5.378. The first version of the Part 13
        /// change lowered PercentGood of a forward single interval by the part before the first
        /// value, which made these results Uncertain_DataSubNormal. Data: 1, 2, 3, 4 at 1, 11, 21 and
        /// 31 s; interval [0, 41 s).
        /// </summary>
        [TestCase("Average", 2.5, false)]
        [TestCase("Average", 2.5, true)]
        [TestCase("Count", 4.0, false)]
        [TestCase("Count", 4.0, true)]
        [TestCase("NumberOfTransitions", 4.0, false)]
        [TestCase("NumberOfTransitions", 4.0, true)]
        public void AllGoodDataStaysGoodWhenTheIntervalStartsBeforeTheFirstValue(string aggregateName, double expected, bool reverse)
        {
            var start = new DateTime(2024, 1, 1, 0, 0, 0);
            DataValue[] values =
            [
                Good(1.0, start.AddSeconds(1)),
                Good(2.0, start.AddSeconds(11)),
                Good(3.0, start.AddSeconds(21)),
                Good(4.0, start.AddSeconds(31))
            ];

            DataValue result = ComputeSingleInterval(aggregateName, values, start, start.AddSeconds(41), reverse);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(expected).Within(1e-9));
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void HasEndTimePassedReflectsProcessingWindow()
        {
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0);
            DateTime endTime = startTime.AddMilliseconds(10000);
            var configuration = new AggregateConfiguration
            {
                TreatUncertainAsBad = false,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = false
            };

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Average, startTime, endTime, 10000, false, configuration, m_telemetry);

            calculator.QueueRawValue(new DataValue(new Variant(1.0), StatusCodes.Good, startTime, startTime));
            calculator.TryGetProcessedValue(false, out _);

            Assert.That(calculator.HasEndTimePassed(startTime.AddMilliseconds(5000)), Is.False);
            Assert.That(calculator.HasEndTimePassed(endTime.AddMilliseconds(5000)), Is.True);
        }

        [Test]
        public void ReverseTimeAverageComputesOverInterval()
        {
            // endTime < startTime exercises the TimeFlowsBackward branches in the base class.
            var startTime = new DateTime(2024, 1, 1, 0, 0, 12);
            var endTime = new DateTime(2024, 1, 1, 0, 0, 0);
            var configuration = new AggregateConfiguration
            {
                TreatUncertainAsBad = false,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = false
            };

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Average, startTime, endTime, 12000, false, configuration, m_telemetry);

            for (int i = 0; i < 6; i++)
            {
                DateTime ts = endTime.AddMilliseconds(500 + (i * 2000));
                calculator.QueueRawValue(Good(10.0 + i, ts));
            }

            DataValue result = default;
            bool any = false;
            while (calculator.TryGetProcessedValue(true, out DataValue value))
            {
                if (!any)
                {
                    result = value;
                    any = true;
                }
            }

            Assert.That(any, Is.True);
            Assert.That(result.WrappedValue.IsNull, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InterpolativeExactGoodBoundaryPreservesRawValue(bool reverse)
        {
            DataValue result = ComputeInterpolativeBoundary(StatusCodes.GoodClamped, reverse);
            DateTime expectedTimestamp = new(2024, 1, 1, 0, 0, 10);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(123.0));
            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.GoodClamped));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
            Assert.That(result.SourceTimestamp, Is.EqualTo(expectedTimestamp));
            Assert.That(result.ServerTimestamp, Is.EqualTo(expectedTimestamp));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InterpolativeExactUncertainBoundaryPreservesUncertainRawValue(bool reverse)
        {
            DataValue result = ComputeInterpolativeBoundary(
                StatusCodes.UncertainLastUsableValue,
                reverse);
            DateTime expectedTimestamp = new(2024, 1, 1, 0, 0, 10);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(123.0));
            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Raw));
            Assert.That(result.SourceTimestamp, Is.EqualTo(expectedTimestamp));
            Assert.That(result.ServerTimestamp, Is.EqualTo(expectedTimestamp));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InterpolativeExactBadBoundaryUsesSurroundingGoodValues(bool reverse)
        {
            DataValue result = ComputeInterpolativeBoundary(StatusCodes.BadSensorFailure, reverse);
            DateTime expectedTimestamp = new(2024, 1, 1, 0, 0, 10);

            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.EqualTo(100.0));
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(result.StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Interpolated));
            Assert.That(result.SourceTimestamp, Is.EqualTo(expectedTimestamp));
            Assert.That(result.ServerTimestamp, Is.EqualTo(expectedTimestamp));
        }

        [Test]
        public void SlopedExtrapolationConfigurationProducesInterpolatedResult()
        {
            var startTime = new DateTime(2024, 1, 1, 0, 0, 0);
            DateTime endTime = startTime.AddMilliseconds(20000);
            var configuration = new AggregateConfiguration
            {
                TreatUncertainAsBad = false,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = true
            };

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Interpolative, startTime, endTime, 5000, false, configuration, m_telemetry);

            var values = new List<DataValue>
            {
                Good(0.0, startTime.AddMilliseconds(1000)),
                Good(40.0, startTime.AddMilliseconds(9000))
            };
            foreach (DataValue value in values)
            {
                calculator.QueueRawValue(value);
            }

            bool any = false;
            while (calculator.TryGetProcessedValue(true, out DataValue _))
            {
                any = true;
            }

            Assert.That(any, Is.True);
        }

        /// <summary>
        /// Runs one interval (ProcessingInterval 0) from the early to the late time, or backward,
        /// with TreatUncertainAsBad = False, and returns its result.
        /// </summary>
        private DataValue ComputeSingleInterval(
            string aggregateName,
            DataValue[] chronological,
            DateTime earlyTime,
            DateTime lateTime,
            bool reverse)
        {
            var configuration = new AggregateConfiguration
            {
                TreatUncertainAsBad = false,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = false
            };
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                Aggregators.GetIdForStandardAggregate(new QualifiedName(aggregateName)),
                reverse ? lateTime : earlyTime,
                reverse ? earlyTime : lateTime,
                0,
                false,
                configuration,
                m_telemetry);

            for (int index = 0; index < chronological.Length; index++)
            {
                DataValue value = chronological[reverse ? chronological.Length - 1 - index : index];
                Assert.That(calculator.QueueRawValue(value), Is.True);
            }

            Assert.That(calculator.TryGetProcessedValue(true, out DataValue result), Is.True);
            Assert.That(calculator.TryGetProcessedValue(true, out _), Is.False);
            return result;
        }

        private DataValue ComputeInterpolativeBoundary(
            StatusCode exactStatus,
            bool reverse)
        {
            var exactTime = new DateTime(2024, 1, 1, 0, 0, 10);
            DateTime startTime = exactTime;
            DateTime endTime = reverse
                ? new DateTime(2024, 1, 1, 0, 0, 5)
                : new DateTime(2024, 1, 1, 0, 0, 15);
            var configuration = new AggregateConfiguration
            {
                TreatUncertainAsBad = true,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = false
            };
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Interpolative,
                startTime,
                endTime,
                5000,
                false,
                configuration,
                m_telemetry);

            DataValue[] values =
            [
                Good(0.0, new DateTime(2024, 1, 1, 0, 0, 0)),
                Good(50.0, new DateTime(2024, 1, 1, 0, 0, 5)),
                new DataValue(new Variant(123.0), exactStatus, exactTime, exactTime),
                Good(150.0, new DateTime(2024, 1, 1, 0, 0, 15)),
                Good(200.0, new DateTime(2024, 1, 1, 0, 0, 20))
            ];
            if (reverse)
            {
                for (int index = values.Length - 1; index >= 0; index--)
                {
                    Assert.That(calculator.QueueRawValue(values[index]), Is.True);
                }
            }
            else
            {
                foreach (DataValue value in values)
                {
                    Assert.That(calculator.QueueRawValue(value), Is.True);
                }
            }

            Assert.That(calculator.TryGetProcessedValue(true, out DataValue result), Is.True);
            Assert.That(calculator.TryGetProcessedValue(true, out _), Is.False);
            return result;
        }
    }
}
