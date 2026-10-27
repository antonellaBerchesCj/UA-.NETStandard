/* ========================================================================
 * OPC UA Part 13 (OPC 10000-13 v1.05.07) aggregate conformance tests.
 *
 * Stepped and sloped interpolation per data type (Int32, Float, Double, Boolean, String)
 * for the aggregates the CTT reported for StaticHistoricalDataItem_HistorianInt2:
 * DeltaBounds, DurationInStateNonZero, DurationInStateZero, EndBound, Interpolative,
 * Maximum2, MaximumActualTime2, Minimum2, MinimumActualTime2, Range2, StartBound,
 * TimeAverage2, Total and Total2 (plus TimeAverage).
 *
 * Rules:
 *  - OPC 10000-11 §5.2.2: the Stepped Property of the HistoricalDataConfiguration says whether
 *    the variable uses stepped (True) or sloped (False) interpolation. The calculation must use
 *    the value the variable exposes, because clients (and the CTT) reproduce it from there.
 *  - OPC 10000-13 §3.1.8/§3.1.9: interpolated and simple bounding values are stepped or sloped
 *    according to Stepped.
 *  - §5.4.3.6, §5.4.3.8: TimeAverage and Total always use a sloped line; TimeAverage2 and
 *    Total2 follow Stepped.
 *  - §5.4.2.3, Table 50: only numeric values can be interpolated with a sloped line; Boolean and
 *    String values always use the prior raw value.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.Part13
{
    /// <summary>
    /// Stepped versus sloped interpolation per data type.
    /// </summary>
    [TestFixture]
    [Category("Part13")]
    [Category("Part13Stepped")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class Part13SteppedDataTypeTests
    {
        private static readonly DateTime s_t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// The aggregates whose result depends on Stepped, with the result for the numeric data
        /// 10@0 s, 30@10 s, 24@20 s, 64@30 s and the interval [5 s, 25 s):
        /// sloped bounds 20@5 s and 44@25 s, stepped bounds 10@5 s and 24@25 s.
        /// DurationInState* use 0@0 s, 10@10 s, 0@20 s, 0@30 s: the sloped start bound 5 is
        /// non-zero, the stepped start bound 0 is zero.
        /// Columns: aggregate, sloped (Stepped = False), stepped (Stepped = True).
        /// </summary>
        private static readonly object[][] s_numericExpectations = {
            new object[] { "Interpolative", 20.0, 10.0 },
            new object[] { "StartBound", 20.0, 10.0 },
            new object[] { "EndBound", 44.0, 24.0 },
            new object[] { "DeltaBounds", 24.0, 14.0 },
            new object[] { "Minimum2", 20.0, 10.0 },
            new object[] { "Maximum2", 44.0, 30.0 },
            new object[] { "MinimumActualTime2", 20.0, 10.0 },
            new object[] { "MaximumActualTime2", 44.0, 30.0 },
            new object[] { "Range2", 24.0, 20.0 },
            new object[] { "TimeAverage2", 28.25, 23.5 },
            new object[] { "Total2", 565.0, 470.0 },
            new object[] { "TimeAverage", 28.25, 28.25 },
            new object[] { "Total", 565.0, 565.0 },
            new object[] { "DurationInStateZero", 5000.0, 10000.0 },
            new object[] { "DurationInStateNonZero", 15000.0, 10000.0 }
        };

        /// <summary>The aggregates that return the data type of the raw values (Table 50).</summary>
        private static readonly HashSet<string> s_rawTypeAggregates = new HashSet<string>(StringComparer.Ordinal) {
            "Interpolative", "StartBound", "EndBound", "DeltaBounds", "Minimum2", "Maximum2",
            "MinimumActualTime2", "MaximumActualTime2"
        };

        /// <summary>One case per numeric data type, aggregate and Stepped value.</summary>
        public static IEnumerable<TestCaseData> NumericCases()
        {
            foreach (string dataType in new[] { "Int32", "Float", "Double" })
            {
                foreach (object[] row in s_numericExpectations)
                {
                    foreach (bool stepped in new[] { false, true })
                    {
                        string aggregateName = (string)row[0];
                        double expected = stepped ? (double)row[2] : (double)row[1];
                        yield return new TestCaseData(dataType, aggregateName, stepped, expected)
                            .SetName("Stepped." + dataType + "." + aggregateName + (stepped ? ".SteppedTrue" : ".SteppedFalse"));
                    }
                }
            }
        }

        /// <summary>One case per non-numeric data type, aggregate and Stepped value.</summary>
        public static IEnumerable<TestCaseData> NonNumericCases()
        {
            // Boolean: false@0 s, true@10 s, false@20 s, true@30 s. String: "A", "B", "C", "D".
            // The bounds are the prior raw values: false/"A" at 5 s, false/"C" at 25 s.
            object[][] rows = {
                new object[] { "Boolean", "StartBound", false },
                new object[] { "Boolean", "EndBound", false },
                new object[] { "Boolean", "DurationInStateZero", 10000.0 },
                new object[] { "Boolean", "DurationInStateNonZero", 10000.0 },
                new object[] { "String", "StartBound", "A" },
                new object[] { "String", "EndBound", "C" }
            };

            foreach (object[] row in rows)
            {
                foreach (bool stepped in new[] { false, true })
                {
                    yield return new TestCaseData((string)row[0], (string)row[1], stepped, row[2])
                        .SetName("Stepped." + row[0] + "." + row[1] + (stepped ? ".SteppedTrue" : ".SteppedFalse"));
                }
            }
        }

        #region Numeric data
        /// <summary>
        /// Numeric variables follow the Stepped Property: sloped bounds with Stepped = False,
        /// stepped bounds with Stepped = True; TimeAverage is always sloped.
        /// Aggregates that return the raw data type keep it (Int32 stays Int32), the
        /// TimeAverage/Total family returns a Double.
        /// </summary>
        [TestCaseSource(nameof(NumericCases))]
        public void NumericVariableFollowsTheSteppedProperty(string dataType, string aggregateName, bool stepped, double expected)
        {
            bool durationInState = aggregateName.StartsWith("DurationInState", StringComparison.Ordinal);
            double[] samples = durationInState
                ? new double[] { 0, 10, 0, 0 }
                : new double[] { 10, 30, 24, 64 };

            List<DataValue> raw = new List<DataValue>();

            for (int ii = 0; ii < samples.Length; ii++)
            {
                raw.Add(Raw(ii * 10, ToVariant(dataType, samples[ii])));
            }

            DataValue result = RunSingle(aggregateName, raw, 5, 25, 20000, stepped, CttConfiguration());

            Assert.That(
                result.StatusCode.CodeBits,
                Is.EqualTo(StatusCodes.Good),
                "all raw values are Good: " + AggregateResultComparer.FormatStatus(result.StatusCode));
            Assert.That(result.Value, Is.Not.Null, "value expected");
            Assert.That(
                Convert.ToDouble(result.Value, CultureInfo.InvariantCulture),
                Is.EqualTo(expected).Within(1e-6),
                aggregateName + " with Stepped=" + stepped + " on " + dataType);

            if (dataType == "Int32" && s_rawTypeAggregates.Contains(aggregateName))
            {
                Assert.That(result.Value, Is.InstanceOf<int>(), "Table 50: " + aggregateName + " returns the raw data type");
            }

            // Range2 returns a Double as in 1.5.378 (Table 50 gives the raw data type; kept to avoid a
            // breaking change for clients).
            if (aggregateName == "Range2" ||
                aggregateName.StartsWith("TimeAverage", StringComparison.Ordinal) || aggregateName.StartsWith("Total", StringComparison.Ordinal))
            {
                Assert.That(result.Value, Is.InstanceOf<double>(), aggregateName + " returns a Double");
            }
        }
        #endregion

        #region Non-numeric data
        /// <summary>
        /// Boolean and String variables are interpolated stepped whatever the Stepped Property says
        /// (§5.4.2.3, Table 50): the bound is the prior raw value, never a sloped mix of two values
        /// (0.5 cast to Boolean is True; a String cannot be converted to a number at all).
        /// </summary>
        [TestCaseSource(nameof(NonNumericCases))]
        public void NonNumericVariableIsAlwaysStepped(string dataType, string aggregateName, bool stepped, object expected)
        {
            List<DataValue> raw = new List<DataValue>();

            if (dataType == "Boolean")
            {
                raw.Add(Raw(0, new Variant(false)));
                raw.Add(Raw(10, new Variant(true)));
                raw.Add(Raw(20, new Variant(false)));
                raw.Add(Raw(30, new Variant(true)));
            }
            else
            {
                raw.Add(Raw(0, new Variant("A")));
                raw.Add(Raw(10, new Variant("B")));
                raw.Add(Raw(20, new Variant("C")));
                raw.Add(Raw(30, new Variant("D")));
            }

            DataValue result = RunSingle(aggregateName, raw, 5, 25, 20000, stepped, CttConfiguration());

            Assert.That(
                result.StatusCode.CodeBits,
                Is.EqualTo(StatusCodes.Good),
                "all raw values are Good: " + AggregateResultComparer.FormatStatus(result.StatusCode));

            if (expected is double)
            {
                Assert.That(Convert.ToDouble(result.Value, CultureInfo.InvariantCulture), Is.EqualTo((double)expected).Within(1e-6), aggregateName);
            }
            else
            {
                Assert.That(result.Value, Is.EqualTo(expected), aggregateName + " uses the prior raw value");
            }
        }

        /// <summary>
        /// TimeAverage, TimeAverage2, Total and Total2 are valid for numeric data only (Table 50), so
        /// String values give Bad_AggregateInvalidInputs (Table 45), not Good 0.
        /// </summary>
        [TestCase("TimeAverage")]
        [TestCase("TimeAverage2")]
        [TestCase("Total")]
        [TestCase("Total2")]
        public void StringDataGivesNoNumericResult(string aggregateName)
        {
            List<DataValue> raw = new List<DataValue> {
                Raw(0, new Variant("A")),
                Raw(10, new Variant("B")),
                Raw(20, new Variant("C")),
                Raw(30, new Variant("D"))
            };

            DataValue result = RunSingle(aggregateName, raw, 5, 25, 20000, false, CttConfiguration());

            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.BadAggregateInvalidInputs),
                aggregateName + ": " + AggregateResultComparer.FormatStatus(result.StatusCode));
            Assert.That(result.Value, Is.Null, aggregateName + " has no numeric value");
        }
        #endregion

        #region CTT graph: DeltaBounds on StaticHistoricalDataItem_HistorianInt2
        /// <summary>
        /// The CTT graph for DeltaBounds: request 13:00:10.000 to 13:04:08.000, ProcessingInterval 0
        /// (one interval), TreatUncertainAsBad = True, PercentDataBad = PercentDataGood = 100,
        /// UseSlopedExtrapolation = False. Raw values: 912.92 at 13:00:10, 792.35 at 13:04:00 and
        /// 50.70 at 13:04:10. The node exposes Stepped = False, so the end bound is sloped:
        /// 792.35 - (792.35 - 50.70) x 0.8 = 199.03 and DeltaBounds = 199.03 - 912.92 = -713.89 (the
        /// CTT value). The server returned -120.57, the result for Stepped = True (end bound 792.35).
        /// </summary>
        [TestCase(false, 912.92, 199.03, -713.89)]
        [TestCase(true, 912.92, 792.35, -120.57)]
        public void CttDeltaBoundsGraphDouble(bool stepped, double startBound, double endBound, double deltaBounds)
        {
            List<DataValue> raw = CttGraphRaw("Double");

            Assert.Multiple(() => {
                AssertValue(RunCttGraph("StartBound", raw, stepped), startBound, "StartBound");
                AssertValue(RunCttGraph("EndBound", raw, stepped), endBound, "EndBound");
                AssertValue(RunCttGraph("DeltaBounds", raw, stepped), deltaBounds, "DeltaBounds");
            });
        }

        /// <summary>
        /// The same graph with Int32 values 913, 792 and 51: the sloped end bound 199.2 is rounded to
        /// the raw data type (OPC 10000-4 §7.7.3), DeltaBounds = 199 - 913 = -714; stepped -121.
        /// </summary>
        [TestCase(false, 913, 199, -714)]
        [TestCase(true, 913, 792, -121)]
        public void CttDeltaBoundsGraphInt32(bool stepped, int startBound, int endBound, int deltaBounds)
        {
            List<DataValue> raw = CttGraphRaw("Int32");

            Assert.Multiple(() => {
                AssertValue(RunCttGraph("StartBound", raw, stepped), startBound, "StartBound");
                AssertValue(RunCttGraph("EndBound", raw, stepped), endBound, "EndBound");
                AssertValue(RunCttGraph("DeltaBounds", raw, stepped), deltaBounds, "DeltaBounds");
            });
        }
        #endregion

        #region AggregateManager: Stepped and AggregateConfiguration from the HA Configuration
        /// <summary>
        /// AggregateManager.CreateCalculator(..., HistoricalDataConfigurationState, ...) takes Stepped
        /// from the HA Configuration the variable exposes, so a node manager cannot calculate with a
        /// different value than the one clients read. With UseServerCapabilitiesDefaults = True the
        /// node's AggregateConfiguration applies.
        /// </summary>
        [TestCase(false, -713.89)]
        [TestCase(true, -120.57)]
        public void AggregateManagerTakesSteppedFromTheHaConfiguration(bool exposedStepped, double expectedDeltaBounds)
        {
            HistoricalDataConfigurationState haConfiguration = CreateHaConfiguration(exposedStepped, true, 100, 100, false);

            IAggregateCalculator calculator;

            using (AggregateManager manager = new AggregateManager(null))
            {
                manager.RegisterFactory(
                    ObjectIds.AggregateFunction_DeltaBounds,
                    Opc.Ua.BrowseNames.AggregateFunction_DeltaBounds,
                    Aggregators.CreateStandardCalculator);

                calculator = manager.CreateCalculator(
                    ObjectIds.AggregateFunction_DeltaBounds,
                    CttTime(0, 10),
                    CttTime(4, 8),
                    0,
                    haConfiguration.Stepped.Value,
                    new AggregateConfiguration { UseServerCapabilitiesDefaults = true });
            }

            Assert.That(calculator, Is.Not.Null, "DeltaBounds calculator");

            foreach (DataValue value in CttGraphRaw("Double"))
            {
                calculator.QueueRawValue(value);
            }

            DataValue result = calculator.GetProcessedValue(true);
            AssertValue(result, expectedDeltaBounds, "DeltaBounds with the exposed Stepped=" + exposedStepped);
            Assert.That(calculator.GetProcessedValue(true), Is.Null, "one interval");
        }

        /// <summary>
        /// UseServerCapabilitiesDefaults = True: the values of the variable's AggregateConfiguration
        /// object are used and the other request fields are ignored (OPC 10000-4 §7.22.4 Table 146).
        /// With False the request applies.
        /// </summary>
        [Test]
        public void AggregateManagerUsesTheExposedAggregateConfiguration()
        {
            HistoricalDataConfigurationState haConfiguration = CreateHaConfiguration(false, false, 80, 70, true);

            using (AggregateManager manager = new AggregateManager(null))
            {
                AggregateConfiguration request = new AggregateConfiguration {
                    UseServerCapabilitiesDefaults = true,
                    TreatUncertainAsBad = true,
                    PercentDataBad = 100,
                    PercentDataGood = 100,
                    UseSlopedExtrapolation = false
                };

                AggregateConfiguration resolved = manager.GetConfiguration(haConfiguration, request);
                Assert.That(resolved.UseServerCapabilitiesDefaults, Is.False);
                Assert.That(resolved.TreatUncertainAsBad, Is.False);
                Assert.That(resolved.PercentDataBad, Is.EqualTo(80));
                Assert.That(resolved.PercentDataGood, Is.EqualTo(70));
                Assert.That(resolved.UseSlopedExtrapolation, Is.True);

                request.UseServerCapabilitiesDefaults = false;
                Assert.That(manager.GetConfiguration(haConfiguration, request), Is.SameAs(request), "an explicit configuration applies");

                AggregateConfiguration defaults = manager.GetConfiguration(null, null);
                Assert.That(defaults.TreatUncertainAsBad, Is.True, "Part 13 §4.2.1.2 default");
                Assert.That(defaults.PercentDataBad, Is.EqualTo(100));
                Assert.That(defaults.PercentDataGood, Is.EqualTo(100));
                Assert.That(defaults.UseSlopedExtrapolation, Is.False);
            }
        }

        /// <summary>
        /// The HA Configuration is found as the child "HA Configuration" of the variable; a missing
        /// configuration or Stepped Property means Stepped = False (OPC 10000-11 §5.2.2 default).
        /// </summary>
        [Test]
        public void AggregateManagerFindsTheHaConfiguration()
        {
            BaseDataVariableState variable = new BaseDataVariableState(null);
            variable.BrowseName = new QualifiedName("HistorianInt2", 1);
            HistoricalDataConfigurationState haConfiguration = CreateHaConfiguration(true, true, 100, 100, false);
            variable.AddChild(haConfiguration);

            SystemContext context = new SystemContext(null);
            Assert.That(AggregateManager.FindHistoricalConfiguration(context, variable), Is.SameAs(haConfiguration));
            Assert.That(AggregateManager.IsStepped(haConfiguration), Is.True);
            Assert.That(AggregateManager.IsStepped(null), Is.False);

            haConfiguration.Stepped = null;
            Assert.That(AggregateManager.IsStepped(haConfiguration), Is.False);
        }
        #endregion

        #region Helpers
        private static DateTime At(double seconds)
        {
            return s_t0.AddMilliseconds(seconds * 1000.0);
        }

        private static DateTime CttTime(int minutes, int seconds)
        {
            return new DateTime(2026, 10, 1, 13, minutes, seconds, DateTimeKind.Utc);
        }

        private static DataValue Raw(double seconds, Variant value)
        {
            return new DataValue(value, StatusCodes.Good, At(seconds), At(seconds));
        }

        private static Variant ToVariant(string dataType, double value)
        {
            switch (dataType)
            {
                case "Int32": return new Variant((int)value);
                case "Float": return new Variant((float)value);
                default: return new Variant(value);
            }
        }

        private static List<DataValue> CttGraphRaw(string dataType)
        {
            double[][] points = {
                new double[] { 0, 0, 900.00 },
                new double[] { 0, 10, 912.92 },
                new double[] { 2, 0, 600.00 },
                new double[] { 4, 0, 792.35 },
                new double[] { 4, 10, 50.70 }
            };

            List<DataValue> raw = new List<DataValue>();

            foreach (double[] point in points)
            {
                DateTime time = CttTime((int)point[0], (int)point[1]);
                Variant value = dataType == "Int32"
                    ? new Variant((int)Math.Floor(point[2] + 0.5))
                    : new Variant(point[2]);
                raw.Add(new DataValue(value, StatusCodes.Good, time, time));
            }

            return raw;
        }

        private static DataValue RunCttGraph(string aggregateName, List<DataValue> raw, bool stepped)
        {
            List<DataValue> results = AggregateCalculatorRunner.Run(
                AnnexAExamples.GetAggregateId(aggregateName),
                raw,
                CttTime(0, 10),
                CttTime(4, 8),
                0,
                stepped,
                CttConfiguration());

            Assert.That(results.Count, Is.EqualTo(1), "ProcessingInterval 0 gives one interval for " + aggregateName);
            TestContext.Out.WriteLine("{0} (Stepped={1}): {2}", aggregateName, stepped, AggregateResultComparer.FormatActual(results[0], CttTime(0, 0)));
            return results[0];
        }

        private static DataValue RunSingle(
            string aggregateName,
            List<DataValue> raw,
            double startSeconds,
            double endSeconds,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration)
        {
            List<DataValue> results = AggregateCalculatorRunner.Run(
                AnnexAExamples.GetAggregateId(aggregateName),
                raw,
                At(startSeconds),
                At(endSeconds),
                processingInterval,
                stepped,
                configuration);

            Assert.That(results.Count, Is.EqualTo(1), "one interval expected for " + aggregateName);
            TestContext.Out.WriteLine("{0} (Stepped={1}): {2}", aggregateName, stepped, AggregateResultComparer.FormatActual(results[0], s_t0));
            return results[0];
        }

        /// <summary>The configuration of the CTT graph and of the node's HA Configuration.</summary>
        private static AggregateConfiguration CttConfiguration()
        {
            return new AggregateConfiguration {
                UseServerCapabilitiesDefaults = false,
                TreatUncertainAsBad = true,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = false
            };
        }

        private static HistoricalDataConfigurationState CreateHaConfiguration(
            bool stepped,
            bool treatUncertainAsBad,
            byte percentDataBad,
            byte percentDataGood,
            bool useSlopedExtrapolation)
        {
            HistoricalDataConfigurationState configuration = new HistoricalDataConfigurationState(null);
            configuration.BrowseName = new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration);
            configuration.ReferenceTypeId = ReferenceTypeIds.HasHistoricalConfiguration;
            configuration.Stepped = new PropertyState<bool>(configuration) { Value = stepped };

            AggregateConfigurationState aggregateConfiguration = new AggregateConfigurationState(configuration);
            aggregateConfiguration.TreatUncertainAsBad = new PropertyState<bool>(aggregateConfiguration) { Value = treatUncertainAsBad };
            aggregateConfiguration.PercentDataBad = new PropertyState<byte>(aggregateConfiguration) { Value = percentDataBad };
            aggregateConfiguration.PercentDataGood = new PropertyState<byte>(aggregateConfiguration) { Value = percentDataGood };
            aggregateConfiguration.UseSlopedExtrapolation = new PropertyState<bool>(aggregateConfiguration) { Value = useSlopedExtrapolation };
            configuration.AggregateConfiguration = aggregateConfiguration;

            return configuration;
        }

        private static void AssertValue(DataValue result, double expected, string because)
        {
            Assert.That(result, Is.Not.Null, because + ": result expected");
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.Good), because + ": " + AggregateResultComparer.FormatStatus(result.StatusCode));
            Assert.That(result.Value, Is.Not.Null, because + ": value expected");
            Assert.That(Convert.ToDouble(result.Value, CultureInfo.InvariantCulture), Is.EqualTo(expected).Within(1e-6), because);
        }
        #endregion
    }
}
