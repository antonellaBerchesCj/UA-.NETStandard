/* ========================================================================
 * OPC UA Part 13 (OPC 10000-13 v1.05.07) aggregate conformance tests.
 *
 * Result data types and the status of intervals without usable data: Delta/DeltaBounds and
 * Range/Range2 return the data type of the raw values (Tables 50, 62, 67, 74, 78), integer
 * values are converted as OPC 10000-4 §7.7.3 describes, and TimeAverage2/Total2 tell an
 * interval whose data was omitted apart from an interval without data (§5.4.3.7, §5.4.3.9).
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.Part13
{
    /// <summary>
    /// Result data types of the aggregates that return the raw data type, and TimeAverage2/Total2
    /// for intervals without usable data.
    /// </summary>
    [TestFixture]
    [Category("Part13")]
    [Category("Part13DataType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class Part13DataTypeTests
    {
        private static readonly DateTime s_t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        #region Delta and DeltaBounds (Table 50)
        /// <summary>
        /// Delta and DeltaBounds return the data type of the raw values (Table 50). Raw: start value
        /// at 0 s, end value at 5 s, 10 s and 20 s; interval [0 s, 10 s). Delta uses the first and
        /// last raw values in the interval (0 s, 5 s); DeltaBounds uses the bounds (0 s, 10 s).
        /// </summary>
        [TestCase("Delta", BuiltInType.Byte, 10.0, 20.0, 10.0)]
        [TestCase("Delta", BuiltInType.Int32, 10.0, 25.0, 15.0)]
        [TestCase("Delta", BuiltInType.Int16, 20.0, 10.0, -10.0)]
        [TestCase("Delta", BuiltInType.Double, 10.5, 5.25, -5.25)]
        [TestCase("DeltaBounds", BuiltInType.Byte, 10.0, 20.0, 10.0)]
        [TestCase("DeltaBounds", BuiltInType.Int32, 10.0, 25.0, 15.0)]
        public void DeltaKeepsTheRawDataType(
            string aggregateName,
            BuiltInType sourceType,
            double startValue,
            double endValue,
            double expectedValue)
        {
            List<DataValue> raw = new List<DataValue> {
                Typed(0, sourceType, startValue),
                Typed(5, sourceType, endValue),
                Typed(10, sourceType, endValue),
                Typed(20, sourceType, endValue)
            };

            DataValue result = RunSingle(aggregateName, raw, 0, 10, false, Config(false));

            AssertType(result, sourceType, aggregateName);
            AssertValue(result, expectedValue, aggregateName);
        }

        /// <summary>
        /// A delta that does not fit into the data type of the raw values (a decrease of an unsigned
        /// value, or an overflow) cannot be converted to the result data type (Table 50), which is
        /// Bad_AggregateInvalidInputs (Table 45: errors attempting to perform data conversions).
        /// </summary>
        [TestCase("Delta", BuiltInType.Byte, 20.0, 10.0)]
        [TestCase("Delta", BuiltInType.SByte, -100.0, 100.0)]
        [TestCase("Delta", BuiltInType.UInt16, 20.0, 10.0)]
        [TestCase("Delta", BuiltInType.UInt32, 10.0, 5.0)]
        [TestCase("Delta", BuiltInType.Int32, -2000000000.0, 2000000000.0)]
        [TestCase("Delta", BuiltInType.UInt64, 10.0, 5.0)]
        [TestCase("DeltaBounds", BuiltInType.Byte, 20.0, 10.0)]
        [TestCase("DeltaBounds", BuiltInType.UInt16, 20.0, 10.0)]
        [TestCase("DeltaBounds", BuiltInType.UInt32, 10.0, 5.0)]
        [TestCase("DeltaBounds", BuiltInType.Int32, -2000000000.0, 2000000000.0)]
        [TestCase("DeltaBounds", BuiltInType.UInt64, 10.0, 5.0)]
        public void DeltaThatDoesNotFitTheRawDataTypeIsAnInvalidInput(
            string aggregateName,
            BuiltInType sourceType,
            double startValue,
            double endValue)
        {
            List<DataValue> raw = new List<DataValue> {
                Typed(0, sourceType, startValue),
                Typed(5, sourceType, endValue),
                Typed(10, sourceType, endValue),
                Typed(20, sourceType, endValue)
            };

            DataValue result = RunSingle(aggregateName, raw, 0, 10, false, Config(false));

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadAggregateInvalidInputs), aggregateName);
            Assert.That(result.Value, Is.Null, aggregateName);
        }
        #endregion

        #region Range and Range2 (Double, as in 1.5.378)
        /// <summary>
        /// Range and Range2 return a Double, as in 1.5.378. Table 50 gives the raw data type; the
        /// result type is kept because changing it would break clients. Stepped raw values: minimum
        /// or maximum at 0 s, the other one at 5 s, the same value again at 20 s, so the bounds of
        /// Range2 equal the raw values. A Double also holds ranges that do not fit the raw type
        /// (SByte -100 to 100: 200).
        /// </summary>
        [TestCase("Range", BuiltInType.Int32, 10.0, 30.0, 20.0)]
        [TestCase("Range", BuiltInType.UInt16, 5.0, 60000.0, 59995.0)]
        [TestCase("Range", BuiltInType.SByte, -100.0, 100.0, 200.0)]
        [TestCase("Range2", BuiltInType.Int32, 10.0, 30.0, 20.0)]
        [TestCase("Range2", BuiltInType.UInt16, 5.0, 60000.0, 59995.0)]
        [TestCase("Range2", BuiltInType.SByte, -100.0, 100.0, 200.0)]
        public void RangeReturnsDoubleAsIn1_5_378(
            string aggregateName,
            BuiltInType sourceType,
            double firstValue,
            double secondValue,
            double expectedValue)
        {
            List<DataValue> raw = new List<DataValue> {
                Typed(0, sourceType, firstValue),
                Typed(5, sourceType, secondValue),
                Typed(20, sourceType, secondValue)
            };

            DataValue result = RunSingle(aggregateName, raw, 0, 10, true, Config(false));

            AssertType(result, BuiltInType.Double, aggregateName);
            AssertValue(result, expectedValue, aggregateName);
        }
        #endregion

        #region Integer conversion of interpolated values (Mantis 11494, 11499)
        /// <summary>
        /// Interpolative returns the raw data type (Table 50) and rounds an interpolated value of an
        /// integer variable instead of truncating it (Mantis 11494, 11499). Int32 raw 0@0 and 8@10
        /// give 4.8 at 6 s, which is 5 (truncation would give 4). A value away from .5 is used because
        /// Part 13 does not fix the rule at .5 (Mantis 11518).
        /// </summary>
        [Test]
        public void InterpolativeRoundsInsteadOfTruncating()
        {
            List<DataValue> raw = new List<DataValue> {
                Typed(0, BuiltInType.Int32, 0),
                Typed(10, BuiltInType.Int32, 8),
                Typed(20, BuiltInType.Int32, 8)
            };

            DataValue result = RunSingle("Interpolative", raw, 6, 7, false, Config(false));

            AssertType(result, BuiltInType.Int32, "Interpolative");
            AssertValue(result, 5, "4.8 rounded, not truncated");
        }
        /// <summary>
        /// Part 13 does not define how an interpolated value of an integer variable is rounded at .5
        /// (Mantis 11518, no answer; Mantis 11499: "values should be rounded (without specifying the
        /// algorithm)"), so the 1.5.378 conversion is kept: TypeInfo.Cast rounds to the nearest value
        /// and a tie to the even value. Raw 0 at 0 ms and the given value at 1024 ms give exactly half
        /// of it at 512 ms (a power-of-two range keeps the tie exact): 2.5 gives 2 (round half up: 3),
        /// 1.5 gives 2 (truncation: 1).
        /// </summary>
        [TestCase(5.0, 2.0)]
        [TestCase(-5.0, -2.0)]
        [TestCase(3.0, 2.0)]
        [TestCase(7.0, 4.0)]
        public void StartBoundKeepsTheTypeInfoCastRuleAtHalf(double valueAt1024Milliseconds, double expected)
        {
            List<DataValue> raw = new List<DataValue> {
                Typed(0, BuiltInType.Int32, 0),
                Typed(1.024, BuiltInType.Int32, valueAt1024Milliseconds),
                Typed(2.048, BuiltInType.Int32, valueAt1024Milliseconds)
            };

            // ProcessingInterval 0: one interval for the whole range [512 ms, 768 ms).
            List<DataValue> results = AggregateCalculatorRunner.Run(
                ObjectIds.AggregateFunction_StartBound, raw, At(0.512), At(0.768), 0, false, Config(false));

            Assert.That(results.Count, Is.EqualTo(1));
            AssertType(results[0], BuiltInType.Int32, "StartBound");
            AssertValue(results[0], expected, "ties to even, as in 1.5.378");
        }
       #endregion

        #region TimeAverage2 and Total2 without usable data (§5.4.3.7, §5.4.3.9)
        /// <summary>
        /// With Simple Bounding Values the non-Good data is omitted. An interval whose data is all
        /// omitted (Uncertain values with TreatUncertainAsBad = True) holds data, so the result is
        /// not Bad_NoData but the PercentTime status: Bad, Calculated, without a value.
        /// Raw: Good 1@0, Uncertain 2@10, Uncertain 3@15, Good 4@20, Good 5@30; interval [10 s, 20 s).
        /// </summary>
        [TestCase("TimeAverage2")]
        [TestCase("Total2")]
        public void OmittedUncertainDataGivesBadNotBadNoData(string aggregateName)
        {
            List<DataValue> raw = new List<DataValue> {
                Typed(0, BuiltInType.Double, 1),
                Typed(10, BuiltInType.Double, 2, StatusCodes.UncertainSubstituteValue),
                Typed(15, BuiltInType.Double, 3, StatusCodes.UncertainSubstituteValue),
                Typed(20, BuiltInType.Double, 4),
                Typed(30, BuiltInType.Double, 5)
            };

            DataValue result = RunSingle(aggregateName, raw, 10, 20, false, Config(true));

            Assert.That(
                result.StatusCode.CodeBits,
                Is.EqualTo(StatusCodes.Bad),
                aggregateName + ": " + AggregateResultComparer.FormatStatus(result.StatusCode));
            Assert.That(
                result.StatusCode.AggregateBits & AggregateBits.Calculated,
                Is.EqualTo(AggregateBits.Calculated),
                aggregateName + ": Calculated");
            Assert.That(result.Value, Is.Null, aggregateName + ": no value with a Bad status");
        }

        /// <summary>
        /// An interval that holds only Bad raw values has no usable data: Bad_NoData.
        /// Raw: Bad 2@10, Bad 3@15, Good 4@20, Good 5@30 (no data before 10 s); interval [10 s, 20 s).
        /// </summary>
        [TestCase("TimeAverage2")]
        [TestCase("Total2")]
        public void OnlyBadDataGivesBadNoData(string aggregateName)
        {
            List<DataValue> raw = new List<DataValue> {
                Typed(10, BuiltInType.Double, 2, StatusCodes.BadDataUnavailable),
                Typed(15, BuiltInType.Double, 3, StatusCodes.BadDataUnavailable),
                Typed(20, BuiltInType.Double, 4),
                Typed(30, BuiltInType.Double, 5)
            };

            DataValue result = RunSingle(aggregateName, raw, 10, 20, false, Config(false));

            Assert.That(
                result.StatusCode.CodeBits,
                Is.EqualTo(StatusCodes.BadNoData),
                aggregateName + ": " + AggregateResultComparer.FormatStatus(result.StatusCode));
        }
        #endregion

        #region Helpers
        private static DateTime At(double seconds)
        {
            return s_t0.AddMilliseconds(seconds * 1000.0);
        }

        private static DataValue Typed(double seconds, BuiltInType type, double value)
        {
            return Typed(seconds, type, value, StatusCodes.Good);
        }

        private static DataValue Typed(double seconds, BuiltInType type, double value, uint status)
        {
            object typed = TypeInfo.Cast(value, TypeInfo.Scalars.Double, type);
            Variant variant = new Variant(typed, new TypeInfo(type, ValueRanks.Scalar));
            return new DataValue(variant, status, At(seconds), At(seconds));
        }

        private static AggregateConfiguration Config(bool treatUncertainAsBad)
        {
            return new AggregateConfiguration {
                UseServerCapabilitiesDefaults = false,
                TreatUncertainAsBad = treatUncertainAsBad,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = false
            };
        }

        private static DataValue RunSingle(
            string aggregateName,
            List<DataValue> raw,
            double startSeconds,
            double endSeconds,
            bool stepped,
            AggregateConfiguration configuration)
        {
            NodeId aggregateId = Aggregators.GetIdForStandardAggregate(new QualifiedName(aggregateName));
            List<DataValue> results = AggregateCalculatorRunner.Run(
                aggregateId,
                raw,
                At(startSeconds),
                At(endSeconds),
                (endSeconds - startSeconds) * 1000.0,
                stepped,
                configuration);

            Assert.That(results.Count, Is.EqualTo(1), "one interval expected for " + aggregateName);
            TestContext.Out.WriteLine("{0}: {1}", aggregateName, AggregateResultComparer.FormatActual(results[0], s_t0));
            return results[0];
        }

        private static void AssertType(DataValue result, BuiltInType expected, string because)
        {
            Assert.That(result.Value, Is.Not.Null, because + ": value expected, status " +
                AggregateResultComparer.FormatStatus(result.StatusCode));
            Assert.That(result.WrappedValue.TypeInfo.BuiltInType, Is.EqualTo(expected), because + ": data type");
        }

        private static void AssertValue(DataValue result, double expected, string because)
        {
            Assert.That(Convert.ToDouble(result.Value, CultureInfo.InvariantCulture), Is.EqualTo(expected).Within(1e-9), because);
        }
        #endregion
    }
}
