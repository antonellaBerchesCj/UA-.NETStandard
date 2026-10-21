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

        #region Delta and DeltaBounds (Tables 74, 78)
        /// <summary>
        /// Delta and DeltaBounds return the data type of the raw values. A delta that does not fit
        /// into that type (a decrease of an unsigned value, or a signed overflow) is returned in the
        /// next larger signed type; beyond Int64/UInt64 it is returned as Double. Raw: start value
        /// at 0 s, end value at 5 s, 10 s and 20 s; interval [0 s, 10 s). Delta uses the first and
        /// last raw values in the interval (0 s, 5 s); DeltaBounds uses the bounds (0 s, 10 s).
        /// </summary>
        [TestCase("Delta", BuiltInType.Byte, 10.0, 20.0, BuiltInType.Byte, 10.0)]
        [TestCase("Delta", BuiltInType.Byte, 20.0, 10.0, BuiltInType.Int16, -10.0)]
        [TestCase("Delta", BuiltInType.SByte, -100.0, 100.0, BuiltInType.Int16, 200.0)]
        [TestCase("Delta", BuiltInType.UInt16, 20.0, 10.0, BuiltInType.Int32, -10.0)]
        [TestCase("Delta", BuiltInType.Int32, 10.0, 25.0, BuiltInType.Int32, 15.0)]
        [TestCase("Delta", BuiltInType.UInt32, 10.0, 5.0, BuiltInType.Int64, -5.0)]
        [TestCase("Delta", BuiltInType.Int32, -2000000000.0, 2000000000.0, BuiltInType.Int64, 4000000000.0)]
        [TestCase("Delta", BuiltInType.UInt64, 10.0, 5.0, BuiltInType.Double, -5.0)]
        [TestCase("Delta", BuiltInType.Double, 10.5, 5.25, BuiltInType.Double, -5.25)]
        [TestCase("DeltaBounds", BuiltInType.Byte, 10.0, 20.0, BuiltInType.Byte, 10.0)]
        [TestCase("DeltaBounds", BuiltInType.Byte, 20.0, 10.0, BuiltInType.Int16, -10.0)]
        [TestCase("DeltaBounds", BuiltInType.UInt16, 20.0, 10.0, BuiltInType.Int32, -10.0)]
        [TestCase("DeltaBounds", BuiltInType.UInt32, 10.0, 5.0, BuiltInType.Int64, -5.0)]
        [TestCase("DeltaBounds", BuiltInType.Int32, -2000000000.0, 2000000000.0, BuiltInType.Int64, 4000000000.0)]
        [TestCase("DeltaBounds", BuiltInType.UInt64, 10.0, 5.0, BuiltInType.Double, -5.0)]
        public void DeltaKeepsTheRawDataTypeOrWidensIt(
            string aggregateName,
            BuiltInType sourceType,
            double startValue,
            double endValue,
            BuiltInType expectedType,
            double expectedValue)
        {
            List<DataValue> raw = new List<DataValue> {
                Typed(0, sourceType, startValue),
                Typed(5, sourceType, endValue),
                Typed(10, sourceType, endValue),
                Typed(20, sourceType, endValue)
            };

            DataValue result = RunSingle(aggregateName, raw, 0, 10, false, Config(false));

            AssertType(result, expectedType, aggregateName);
            AssertValue(result, expectedValue, aggregateName);
        }
        #endregion

        #region Range and Range2 (Tables 62, 67)
        /// <summary>
        /// Range and Range2 return the data type of the raw values; a range that does not fit into
        /// that type is returned as Double. Stepped raw values: minimum or maximum at 0 s, the other
        /// one at 5 s, the same value again at 20 s, so the bounds of Range2 equal the raw values.
        /// </summary>
        [TestCase("Range", BuiltInType.Int32, 10.0, 30.0, BuiltInType.Int32, 20.0)]
        [TestCase("Range", BuiltInType.UInt16, 5.0, 60000.0, BuiltInType.UInt16, 59995.0)]
        [TestCase("Range", BuiltInType.SByte, -100.0, 100.0, BuiltInType.Double, 200.0)]
        [TestCase("Range", BuiltInType.Int32, -2000000000.0, 2000000000.0, BuiltInType.Double, 4000000000.0)]
        [TestCase("Range2", BuiltInType.Int32, 10.0, 30.0, BuiltInType.Int32, 20.0)]
        [TestCase("Range2", BuiltInType.UInt16, 5.0, 60000.0, BuiltInType.UInt16, 59995.0)]
        [TestCase("Range2", BuiltInType.SByte, -100.0, 100.0, BuiltInType.Double, 200.0)]
        [TestCase("Range2", BuiltInType.Int32, -2000000000.0, 2000000000.0, BuiltInType.Double, 4000000000.0)]
        public void RangeKeepsTheRawDataTypeOrReturnsDouble(
            string aggregateName,
            BuiltInType sourceType,
            double firstValue,
            double secondValue,
            BuiltInType expectedType,
            double expectedValue)
        {
            List<DataValue> raw = new List<DataValue> {
                Typed(0, sourceType, firstValue),
                Typed(5, sourceType, secondValue),
                Typed(20, sourceType, secondValue)
            };

            DataValue result = RunSingle(aggregateName, raw, 0, 10, true, Config(false));

            AssertType(result, expectedType, aggregateName);
            AssertValue(result, expectedValue, aggregateName);
        }
        #endregion

        #region Integer conversion of interpolated values (OPC 10000-4 §7.7.3, Mantis 11494)
        /// <summary>
        /// Interpolative returns the raw data type (Table 50) and rounds an interpolated value of an
        /// integer variable instead of truncating it (Mantis 11494, 11499). Int32 raw 0@0 and 5@10 give
        /// 2.5 at 5 s, which is 3 (truncation and banker's rounding would give 2). A positive .5 is used
        /// because the rounding rule for a negative .5 is not defined (Mantis 11518).
        /// </summary>
        [Test]
        public void InterpolativeRoundsInsteadOfTruncating()
        {
            List<DataValue> raw = new List<DataValue> {
                Typed(0, BuiltInType.Int32, 0),
                Typed(10, BuiltInType.Int32, 5),
                Typed(20, BuiltInType.Int32, 5)
            };

            DataValue result = RunSingle("Interpolative", raw, 5, 6, false, Config(false));

            AssertType(result, BuiltInType.Int32, "Interpolative");
            AssertValue(result, 3, "2.5 rounded, not truncated");
        }

        /// <summary>
        /// StartBound of an Int32 variable rounds a negative half up as well: raw 0@0 and -5@10 give
        /// -2.5 at 5 s, which is -2 (adding 0.5 gives -2.0; rounding away from zero would give -3).
        /// This is the rule of RoundHalfUp; Part 13 does not define the negative .5 case (Mantis 11518).
        /// </summary>
        [Test]
        public void StartBoundRoundsANegativeHalfUp()
        {
            List<DataValue> raw = new List<DataValue> {
                Typed(0, BuiltInType.Int32, 0),
                Typed(10, BuiltInType.Int32, -5),
                Typed(20, BuiltInType.Int32, -5)
            };

            DataValue result = RunSingle("StartBound", raw, 5, 6, false, Config(false));

            AssertType(result, BuiltInType.Int32, "StartBound");
            AssertValue(result, -2, "-2.5 rounded half up");
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
