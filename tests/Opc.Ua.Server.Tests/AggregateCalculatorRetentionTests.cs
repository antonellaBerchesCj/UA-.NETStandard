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
using System.Collections;
using System.Reflection;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// A calculator that serves a long-lived monitored item (an AggregateFilter) gets its raw values
    /// one by one and is drained after each one. It must release the values of finished intervals,
    /// or the memory and the time per value grow with the age of the monitored item.
    /// </summary>
    [TestFixture]
    [Category("Aggregates")]
    [Parallelizable]
    public class AggregateCalculatorRetentionTests
    {
        private static readonly DateTime s_t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Queues 20,000 raw values one second apart into 10 s intervals and drains the calculator
        /// after each value. Every collection the calculator holds stays at the size of a few
        /// intervals.
        /// </summary>
        [TestCase("Average")]
        [TestCase("TimeAverage")]
        [TestCase("TimeAverage2")]
        [TestCase("Minimum2")]
        [TestCase("Count")]
        [TestCase("DurationInStateNonZero")]
        [TestCase("DurationGood")]
        [TestCase("Delta")]
        [TestCase("DeltaBounds")]
        [TestCase("StandardDeviationSample")]
        [TestCase("WorstQuality2")]
        [TestCase("Interpolative")]
        public void FinishedIntervalsAreReleased(string aggregateName)
        {
            const int kValues = 20000;
            NodeId aggregateId = Aggregators.GetIdForStandardAggregate(new QualifiedName(aggregateName));
            var configuration = new AggregateConfiguration {
                UseServerCapabilitiesDefaults = false,
                TreatUncertainAsBad = true,
                PercentDataBad = 100,
                PercentDataGood = 100
            };
            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                aggregateId,
                s_t0,
                s_t0.AddSeconds(kValues + 10),
                10000,
                false,
                configuration,
                null);
            int processed = 0;
            int largest = 0;

            for (int ii = 0; ii < kValues; ii++)
            {
                DateTime timestamp = s_t0.AddSeconds(ii);
                uint status = ii % 10 == 7 ? StatusCodes.Bad : StatusCodes.Good;
                calculator.QueueRawValue(new DataValue(new Variant((double)(ii % 3)), status, timestamp, timestamp));

                while (calculator.GetProcessedValue(false) != null)
                {
                    processed++;
                }

                if (ii % 1000 == 999)
                {
                    largest = Math.Max(largest, CountHeldItems(calculator));
                }
            }

            Assert.That(processed, Is.GreaterThanOrEqualTo((kValues / 10) - 2), "the finished intervals are returned");
            Assert.That(largest, Is.LessThanOrEqualTo(50), "values of finished intervals must be released");
        }

        /// <summary>
        /// Sums the items of every collection held in an instance field of the calculator.
        /// </summary>
        private static int CountHeldItems(object calculator)
        {
            int total = 0;

            for (Type type = calculator.GetType(); type != null; type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (field.GetValue(calculator) is ICollection collection)
                    {
                        total += collection.Count;
                    }
                }
            }

            return total;
        }
    }
}
