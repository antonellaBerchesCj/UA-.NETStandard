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
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.Part13
{
    /// <summary>
    /// Part 13 §5.4.2.3, Table 50: the valid source data types of each aggregate. "Some Aggregates
    /// are intended for numeric data types - i.e. integers or real/floating point numbers. Dates,
    /// strings, arrays, etc. are not supported. Other Aggregates are intended for digital data
    /// types - i.e. Boolean or enumerations." A value the aggregate cannot use gives
    /// Bad_AggregateInvalidInputs (Table 45: "invalid data inputs, errors attempting to perform
    /// data conversions"); a variable whose DataType is not supported gives Bad_AggregateNotSupported
    /// (Table 45: "not supported by the Server for the specified Node").
    /// </summary>
    [TestFixture]
    [Category("Part13")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class Part13ValidDataTypeTests
    {
        private static readonly DateTime s_t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Table 50: valid data type "Numeric".</summary>
        private static readonly string[] s_numericAggregates = [
            "Interpolative", "Average", "TimeAverage", "TimeAverage2", "Total", "Total2",
            "Minimum", "Maximum", "MinimumActualTime", "MaximumActualTime", "Range",
            "Minimum2", "Maximum2", "MinimumActualTime2", "MaximumActualTime2", "Range2",
            "Delta", "DeltaBounds", "StandardDeviationSample", "VarianceSample",
            "StandardDeviationPopulation", "VariancePopulation"
        ];

        /// <summary>Table 50: valid data type "Numeric or Boolean".</summary>
        private static readonly string[] s_numericOrBooleanAggregates = [
            "DurationInStateZero", "DurationInStateNonZero", "NumberOfTransitions"
        ];

        /// <summary>Table 50: valid data type "All".</summary>
        private static readonly string[] s_allTypeAggregates = [
            "AnnotationCount", "Count", "Start", "End", "StartBound", "EndBound",
            "DurationGood", "DurationBad", "PercentGood", "PercentBad", "WorstQuality", "WorstQuality2"
        ];

        /// <summary>The non-numeric samples of each kind.</summary>
        private static readonly string[] s_nonNumericKinds = ["Boolean", "String", "NumericString", "DateTime", "DoubleArray"];

        public static IEnumerable<TestCaseData> NumericAggregatesWithNonNumericData()
        {
            foreach (string aggregateName in s_numericAggregates)
            {
                foreach (string kind in s_nonNumericKinds)
                {
                    yield return new TestCaseData(aggregateName, kind).SetName("Table50.Numeric." + aggregateName + "." + kind);
                }
            }
        }

        /// <summary>
        /// A numeric aggregate does not use Boolean, String (also a String that holds a number),
        /// DateTime or array values; every interval that holds them is Bad_AggregateInvalidInputs.
        /// </summary>
        [TestCaseSource(nameof(NumericAggregatesWithNonNumericData))]
        public void NumericAggregateRejectsNonNumericData(string aggregateName, string kind)
        {
            List<DataValue> results = Run(aggregateName, Samples(kind), 5, 25, 20000);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadAggregateInvalidInputs),
                aggregateName + " on " + kind + ": " + AggregateResultComparer.FormatStatus(results[0].StatusCode));
            Assert.That(results[0].Value, Is.Null);
        }

        /// <summary>
        /// DurationInStateZero, DurationInStateNonZero and NumberOfTransitions accept Boolean values,
        /// but not Strings.
        /// </summary>
        [Test]
        public void NumericOrBooleanAggregatesAcceptBooleanButNotString()
        {
            foreach (string aggregateName in s_numericOrBooleanAggregates)
            {
                DataValue boolean = Run(aggregateName, Samples("Boolean"), 5, 25, 20000)[0];
                Assert.That(StatusCode.IsGood(boolean.StatusCode), Is.True,
                    aggregateName + " on Boolean: " + AggregateResultComparer.FormatStatus(boolean.StatusCode));
                Assert.That(boolean.Value, Is.Not.Null, aggregateName + " on Boolean has a value");

                DataValue text = Run(aggregateName, Samples("String"), 5, 25, 20000)[0];
                Assert.That(text.StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadAggregateInvalidInputs), aggregateName + " on String");
            }
        }

        /// <summary>
        /// The aggregates valid for all data types are calculated on String values.
        /// </summary>
        [Test]
        public void AllTypeAggregatesAcceptStrings()
        {
            foreach (string aggregateName in s_allTypeAggregates)
            {
                DataValue result = Run(aggregateName, Samples("String"), 5, 25, 20000)[0];
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                    aggregateName + " on String: " + AggregateResultComparer.FormatStatus(result.StatusCode));
            }
        }

        /// <summary>
        /// A numeric variable with one String sample at 30 s (Double values 0, 10, 20, ... every
        /// 10 s): only the intervals that use the String are Bad_AggregateInvalidInputs. Average uses
        /// the raw values of [30 s, 40 s); TimeAverage also uses it as the end bound of
        /// [20 s, 30 s). The other intervals keep the sloped interpolation of the variable:
        /// TimeAverage of [0 s, 10 s) is 5 (stepped would give 0).
        /// </summary>
        [Test]
        public void NonNumericSampleInvalidatesOnlyTheIntervalsThatUseIt()
        {
            List<DataValue> raw = new List<DataValue>();

            for (int seconds = 0; seconds <= 70; seconds += 10)
            {
                Variant value = seconds == 30 ? new Variant("text") : new Variant((double)seconds);
                raw.Add(new DataValue(value, StatusCodes.Good, At(seconds), At(seconds)));
            }

            List<DataValue> average = Run("Average", raw, 0, 60, 10000);
            List<DataValue> timeAverage = Run("TimeAverage", raw, 0, 60, 10000);

            Assert.That(Codes(average), Is.EqualTo(new uint[] {
                StatusCodes.Good, StatusCodes.Good, StatusCodes.Good,
                StatusCodes.BadAggregateInvalidInputs, StatusCodes.Good, StatusCodes.Good }), "Average");
            Assert.That(Codes(timeAverage), Is.EqualTo(new uint[] {
                StatusCodes.Good, StatusCodes.Good, StatusCodes.BadAggregateInvalidInputs,
                StatusCodes.BadAggregateInvalidInputs, StatusCodes.Good, StatusCodes.Good }), "TimeAverage");
            Assert.That(Convert.ToDouble(timeAverage[0].Value, CultureInfo.InvariantCulture), Is.EqualTo(5).Within(1e-9),
                "sloped interpolation is kept for the numeric intervals");
        }

        /// <summary>
        /// A Bad value is not used, so its data type does not matter: a Bad String sample in a numeric
        /// variable is treated like any other Bad value.
        /// </summary>
        [Test]
        public void BadNonNumericSampleIsIgnored()
        {
            List<DataValue> raw = new List<DataValue> {
                new DataValue(new Variant(10.0), StatusCodes.Good, At(0), At(0)),
                new DataValue(new Variant("text"), StatusCodes.Bad, At(5), At(5)),
                new DataValue(new Variant(20.0), StatusCodes.Good, At(8), At(8)),
                new DataValue(new Variant(30.0), StatusCodes.Good, At(20), At(20))
            };

            DataValue result = Run("Average", raw, 0, 10, 10000)[0];

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal), "one Bad value of three");
            Assert.That(Convert.ToDouble(result.Value, CultureInfo.InvariantCulture), Is.EqualTo(15).Within(1e-9), "(10 + 20) / 2");
        }

        /// <summary>
        /// The node-level check (Table 50) used for MonitoredItem filters and processed HistoryRead.
        /// </summary>
        [TestCase("Average", "Double", ValueRanks.Scalar, true)]
        [TestCase("Average", "Int32", ValueRanks.Scalar, true)]
        [TestCase("Average", "Number", ValueRanks.Scalar, true)]
        [TestCase("Average", "Duration", ValueRanks.Scalar, true)]
        [TestCase("Average", "Boolean", ValueRanks.Scalar, false)]
        [TestCase("Average", "ServerState", ValueRanks.Scalar, false)]
        [TestCase("Average", "String", ValueRanks.Scalar, false)]
        [TestCase("Average", "UtcTime", ValueRanks.Scalar, false)]
        [TestCase("Average", "BaseDataType", ValueRanks.Scalar, true)]
        [TestCase("Average", "Double", ValueRanks.OneDimension, false)]
        [TestCase("Average", "Double", ValueRanks.ScalarOrOneDimension, true)]
        [TestCase("Interpolative", "Boolean", ValueRanks.Scalar, false)]
        [TestCase("DurationInStateZero", "Boolean", ValueRanks.Scalar, true)]
        [TestCase("DurationInStateZero", "ServerState", ValueRanks.Scalar, true)]
        [TestCase("NumberOfTransitions", "String", ValueRanks.Scalar, false)]
        [TestCase("Count", "String", ValueRanks.Scalar, true)]
        [TestCase("WorstQuality2", "ByteString", ValueRanks.OneDimension, true)]
        public void IsSupportedForDataTypeFollowsTable50(string aggregateName, string dataType, int valueRank, bool expected)
        {
            NodeId aggregateId = Aggregators.GetIdForStandardAggregate(new QualifiedName(aggregateName));

            bool supported = AggregateManager.IsSupportedForDataType(aggregateId, DataTypeId(dataType), valueRank, TypeTree());

            Assert.That(supported, Is.EqualTo(expected), aggregateName + " for " + dataType);
        }

        /// <summary>
        /// Aggregates that are not standard aggregates are not checked.
        /// </summary>
        [Test]
        public void IsSupportedForDataTypeAcceptsOtherAggregates()
        {
            Assert.That(AggregateManager.IsSupportedForDataType(new NodeId("Custom", 2), DataTypeIds.String, ValueRanks.Scalar, TypeTree()), Is.True);
        }

        private static List<DataValue> Samples(string kind)
        {
            var raw = new List<DataValue>();

            for (int ii = 0; ii < 4; ii++)
            {
                Variant value = kind switch {
                    "Boolean" => new Variant(ii % 2 == 1),
                    "String" => new Variant(((char)('A' + ii)).ToString()),
                    "NumericString" => new Variant(((ii + 1) * 10).ToString(CultureInfo.InvariantCulture)),
                    "DateTime" => new Variant(s_t0.AddDays(ii)),
                    "DoubleArray" => new Variant(new double[] { ii, ii + 1 }),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };
                raw.Add(new DataValue(value, StatusCodes.Good, At(ii * 10), At(ii * 10)));
            }

            return raw;
        }

        private static List<DataValue> Run(string aggregateName, List<DataValue> raw, double startSeconds, double endSeconds, double interval)
        {
            NodeId aggregateId = Aggregators.GetIdForStandardAggregate(new QualifiedName(aggregateName));
            var configuration = new AggregateConfiguration {
                UseServerCapabilitiesDefaults = false,
                TreatUncertainAsBad = true,
                PercentDataBad = 100,
                PercentDataGood = 100
            };

            return AggregateCalculatorRunner.Run(aggregateId, raw, At(startSeconds), At(endSeconds), interval, false, configuration);
        }

        private static uint[] Codes(List<DataValue> results)
        {
            var codes = new uint[results.Count];

            for (int ii = 0; ii < results.Count; ii++)
            {
                codes[ii] = results[ii].StatusCode.CodeBits;
            }

            return codes;
        }

        private static DateTime At(double seconds)
        {
            return s_t0.AddMilliseconds(seconds * 1000.0);
        }

        private static NodeId DataTypeId(string name)
        {
            return name switch {
                "Double" => DataTypeIds.Double,
                "Int32" => DataTypeIds.Int32,
                "Number" => DataTypeIds.Number,
                "Duration" => DataTypeIds.Duration,
                "Boolean" => DataTypeIds.Boolean,
                "ServerState" => DataTypeIds.ServerState,
                "String" => DataTypeIds.String,
                "UtcTime" => DataTypeIds.UtcTime,
                "BaseDataType" => DataTypeIds.BaseDataType,
                "ByteString" => DataTypeIds.ByteString,
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
        }

        /// <summary>
        /// The standard subtypes the cases use: Duration (Double), UtcTime (DateTime) and the
        /// ServerState enumeration.
        /// </summary>
        private static TypeTable TypeTree()
        {
            var typeTree = new TypeTable(new NamespaceTable());
            typeTree.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
            typeTree.AddSubtype(DataTypeIds.Number, DataTypeIds.BaseDataType);
            typeTree.AddSubtype(DataTypeIds.Double, DataTypeIds.Number);
            typeTree.AddSubtype(DataTypeIds.Duration, DataTypeIds.Double);
            typeTree.AddSubtype(DataTypeIds.DateTime, DataTypeIds.BaseDataType);
            typeTree.AddSubtype(DataTypeIds.UtcTime, DataTypeIds.DateTime);
            typeTree.AddSubtype(DataTypeIds.Enumeration, DataTypeIds.BaseDataType);
            typeTree.AddSubtype(DataTypeIds.ServerState, DataTypeIds.Enumeration);
            return typeTree;
        }
    }
}
