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
using System.Diagnostics;
using System.Globalization;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// The time a calculator needs must grow linearly with the number of raw values, both in one
    /// interval and over the life of a monitored item (an AggregateFilter). A step that scans the
    /// interval once per region or per value (as DurationInState did with values.Find for every
    /// region) is quadratic: 8 times the values then take about 64 times as long, linear work about
    /// 8 times. The values arrive one by one and the calculator is drained after each one, as a
    /// monitored item does. The raw values are created before the clock starts, each size is
    /// measured as the best of three runs after a warm-up, and the fixture does not run in parallel
    /// with other tests, to keep allocation and timing noise out of the ratio.
    /// </summary>
    [TestFixture]
    [Category("Aggregates")]
    [NonParallelizable]
    public class AggregateCalculatorScalingTests
    {
        private const int kSmall = 10000;
        private const int kLarge = 80000;
        private const int kRuns = 3;

        // linear work gives a ratio of about 8 for 8 times the values, quadratic work about 64.
        private const double kIntervalRatioLimit = 24;

        // linear work gives a ratio of about 1 between the last and the first quarter of a long run;
        // work that grows with the age of the monitored item gives about 7.
        private const double kAgeRatioLimit = 3;

        // runs below this many milliseconds are compared as this long, so that timer noise in very
        // fast runs cannot produce a large ratio; a quadratic step is far slower than this.
        private const double kFloorMilliseconds = 5;

        private static readonly DateTime s_t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// One interval holds all values (sloped, every tenth value Bad, so Good regions end at a
        /// Bad value): 80,000 values may take at most 24 times as long as 10,000.
        /// </summary>
        [TestCase("DurationInStateZero")]
        [TestCase("DurationInStateNonZero")]
        [TestCase("DurationGood")]
        [TestCase("Average")]
        [TestCase("TimeAverage")]
        [TestCase("TimeAverage2")]
        [TestCase("Minimum2")]
        [TestCase("Count")]
        [TestCase("NumberOfTransitions")]
        [TestCase("Delta")]
        [TestCase("DeltaBounds")]
        [TestCase("StandardDeviationSample")]
        [TestCase("WorstQuality2")]
        [TestCase("Interpolative")]
        public void TimeGrowsLinearlyWithTheValuesInAnInterval(string aggregateName)
        {
            NodeId aggregateId = Aggregators.GetIdForStandardAggregate(new QualifiedName(aggregateName));
            RunOneInterval(aggregateId, 1000);

            double small = double.MaxValue;
            double large = double.MaxValue;

            for (int run = 0; run < kRuns; run++)
            {
                small = Math.Min(small, RunOneInterval(aggregateId, kSmall));
                large = Math.Min(large, RunOneInterval(aggregateId, kLarge));
            }

            TestContext.Out.WriteLine("{0}: {1:F1} ms for {2} values, {3:F1} ms for {4}", aggregateName, small, kSmall, large, kLarge);
            Assert.That(
                large / Math.Max(small, kFloorMilliseconds),
                Is.LessThan(kIntervalRatioLimit),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:F1} ms for {1} values, {2:F1} ms for {3}: the time per interval grows faster than the number of values",
                    small,
                    kSmall,
                    large,
                    kLarge));
        }

        /// <summary>
        /// 80,000 values one second apart into 10 s intervals: the last 20,000 may take at most 3
        /// times as long as the first 20,000.
        /// </summary>
        [TestCase("DurationInStateNonZero")]
        [TestCase("DurationGood")]
        [TestCase("Average")]
        [TestCase("TimeAverage")]
        [TestCase("TimeAverage2")]
        [TestCase("Minimum2")]
        [TestCase("Count")]
        [TestCase("Delta")]
        [TestCase("DeltaBounds")]
        [TestCase("StandardDeviationSample")]
        [TestCase("WorstQuality2")]
        [TestCase("Interpolative")]
        public void TimePerValueDoesNotGrowWithTheAgeOfTheMonitoredItem(string aggregateName)
        {
            NodeId aggregateId = Aggregators.GetIdForStandardAggregate(new QualifiedName(aggregateName));
            RunLongLived(aggregateId, 1000, out _, out _);

            double first = double.MaxValue;
            double last = double.MaxValue;

            for (int run = 0; run < kRuns; run++)
            {
                RunLongLived(aggregateId, kLarge, out double firstQuarter, out double lastQuarter);
                first = Math.Min(first, firstQuarter);
                last = Math.Min(last, lastQuarter);
            }

            TestContext.Out.WriteLine("{0}: first quarter {1:F1} ms, last quarter {2:F1} ms", aggregateName, first, last);
            Assert.That(
                last / Math.Max(first, kFloorMilliseconds),
                Is.LessThan(kAgeRatioLimit),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "first quarter {0:F1} ms, last quarter {1:F1} ms: the time per value grows with the age of the monitored item",
                    first,
                    last));
        }

        /// <summary>
        /// Runs one interval holding all values and returns the elapsed milliseconds.
        /// </summary>
        private static double RunOneInterval(NodeId aggregateId, int count)
        {
            IAggregateCalculator calculator = CreateCalculator(aggregateId, count, count * 1000.0);
            DataValue[] values = CreateValues(count);
            Collect();

            var stopwatch = Stopwatch.StartNew();
            Feed(calculator, values, 0, count);

            while (calculator.GetProcessedValue(true) != null)
            {
            }

            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Runs 10 s intervals over all values and returns the elapsed milliseconds of the first and
        /// of the last quarter of the values.
        /// </summary>
        private static void RunLongLived(NodeId aggregateId, int count, out double firstQuarter, out double lastQuarter)
        {
            IAggregateCalculator calculator = CreateCalculator(aggregateId, count, 10000);
            DataValue[] values = CreateValues(count);
            int quarter = count / 4;
            Collect();

            var stopwatch = Stopwatch.StartNew();
            Feed(calculator, values, 0, quarter);
            firstQuarter = stopwatch.Elapsed.TotalMilliseconds;

            Feed(calculator, values, quarter, count - quarter);
            Collect();

            stopwatch.Restart();
            Feed(calculator, values, count - quarter, count);
            lastQuarter = stopwatch.Elapsed.TotalMilliseconds;
        }

        private static IAggregateCalculator CreateCalculator(NodeId aggregateId, int count, double processingInterval)
        {
            var configuration = new AggregateConfiguration {
                UseServerCapabilitiesDefaults = false,
                TreatUncertainAsBad = true,
                PercentDataBad = 100,
                PercentDataGood = 100
            };

            return Aggregators.CreateStandardCalculator(
                aggregateId,
                s_t0,
                s_t0.AddSeconds(count),
                processingInterval,
                false,
                configuration,
                null);
        }

        /// <summary>
        /// Raw values one second apart, alternating 0 and 1, every tenth one Bad.
        /// </summary>
        private static DataValue[] CreateValues(int count)
        {
            var values = new DataValue[count];

            for (int ii = 0; ii < count; ii++)
            {
                DateTime timestamp = s_t0.AddSeconds(ii);
                uint status = ii % 10 == 7 ? StatusCodes.Bad : StatusCodes.Good;
                values[ii] = new DataValue(new Variant((double)(ii % 2)), status, timestamp, timestamp);
            }

            return values;
        }

        /// <summary>
        /// Queues the values first to last - 1 and drains the calculator after each one.
        /// </summary>
        private static void Feed(IAggregateCalculator calculator, DataValue[] values, int first, int last)
        {
            for (int ii = first; ii < last; ii++)
            {
                calculator.QueueRawValue(values[ii]);

                while (calculator.GetProcessedValue(false) != null)
                {
                }
            }
        }

        private static void Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
