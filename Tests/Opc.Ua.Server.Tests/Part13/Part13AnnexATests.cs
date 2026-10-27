/* ========================================================================
 * OPC UA Part 13 (OPC 10000-13 v1.05.07) aggregate conformance tests.
 *
 * Runs every Annex A example table (AggregateExamples.csv) through the stack's
 * aggregate calculators - the same calculators the server uses for ReadProcessed -
 * and compares value, StatusCode, aggregate bits and timestamp of every interval.
 *
 * Expectations: the Annex A row, unless Part13Deviations lists the row as contradicting
 * the normative text; then the normative expectation is used and the report shows both.
 *
 * Run:  dotnet test Tests/Opc.Ua.Server.Tests --filter "Category=Part13"
 * The report test writes Part13-AnnexA-report.csv and Part13-AnnexA-summary.txt to
 * %PART13_REPORT_DIR% (default: the test work directory).
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.Part13
{
    /// <summary>
    /// Part 13 Annex A conformance of the aggregate calculators.
    /// </summary>
    [TestFixture]
    [Category("Part13")]
    [Category("Part13AnnexA")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class Part13AnnexATests
    {
        /// <summary>One test case per Annex A example table.</summary>
        public static IEnumerable<TestCaseData> AnnexATables()
        {
            foreach (AnnexAExampleTable table in AnnexAExamples.Tables)
            {
                yield return new TestCaseData(table.Key)
                    .SetName("AnnexA." + table.AggregateName + "." + table.DataSetName)
                    .SetDescription(table.Configuration.ToString());
            }
        }

        /// <summary>
        /// The CSV contains the five data sets and an example table for each of the 37
        /// standard aggregates.
        /// </summary>
        [Test]
        public void AnnexAFileIsComplete()
        {
            Assert.That(AnnexAExamples.DataSets.Keys, Is.EquivalentTo(new[] { "Historian1", "Historian2", "Historian3", "Historian4", "Historian5" }));
            Assert.That(AnnexAExamples.Tables.Select(t => t.AggregateName).Distinct().Count(), Is.EqualTo(37));

            foreach (AnnexAExampleTable table in AnnexAExamples.Tables)
            {
                Assert.That(table.Rows.Count, Is.GreaterThan(0), table.Key);
                Assert.That(AnnexAExamples.DataSets.ContainsKey(table.DataSetName), Is.True, table.Key);
                Assert.That(table.AggregateId, Is.Not.Null, table.Key);
                Assert.That(table.Configuration.ProcessingInterval, Is.GreaterThan(0), table.Key);
            }
        }

        /// <summary>
        /// Every interval of the example table matches Part 13: value, StatusCode, aggregate bits
        /// (Raw/Calculated/Interpolated, Partial, MultipleValues) and timestamp.
        /// </summary>
        [TestCaseSource(nameof(AnnexATables))]
        public void StackCalculatorMatchesPart13(string key)
        {
            AnnexAExampleTable table = AnnexAExamples.GetTable(key);
            List<Part13ExpectedRow> expected = Part13Deviations.Resolve(table);
            List<DataValue> actual = Run(table, false, AnnexAExamples.DefaultBaseDate);
            List<Part13RowResult> rows = Compare(expected, actual, AnnexAExamples.DefaultBaseDate);
            string diff = AggregateResultComparer.FormatDiff(rows, AnnexAExamples.DefaultBaseDate);

            string header = string.Format(
                CultureInfo.InvariantCulture,
                "Annex A {0} on {1} ({2}), AggregateExamples.csv line {3}",
                table.AggregateName,
                table.DataSetName,
                table.Configuration,
                table.LineNumber);

            TestContext.Out.WriteLine(header);
            TestContext.Out.WriteLine(diff);

            foreach (Part13Deviation deviation in rows.Where(r => r.Deviation != null).Select(r => r.Deviation).Distinct())
            {
                TestContext.Out.WriteLine("Normative override {0}: {1} - {2}", deviation.Id, deviation.Clause, deviation.Reason);
            }

            if (rows.Exists(r => !r.Matches))
            {
                Assert.Fail(header + Environment.NewLine + diff);
            }
        }

        /// <summary>
        /// Writes a row-by-row report of all tables: the Annex A row, the normative expectation,
        /// the forward result and the backward (StartTime &gt; EndTime) result of the same range.
        /// The test only fails if the report cannot be produced.
        /// </summary>
        [Test]
        [Category("Part13Report")]
        public void WriteAnnexAComparisonReport()
        {
            DateTime baseDate = AnnexAExamples.DefaultBaseDate;
            StringBuilder csv = new StringBuilder();
            StringBuilder summary = new StringBuilder();
            int tablesMatchingCsv = 0;
            int tablesMatchingPart13 = 0;
            int rowsTotal = 0;
            int rowsMatchingCsv = 0;
            int rowsMatchingPart13 = 0;

            csv.AppendLine("Aggregate,DataSet,IntervalMs,Stepped,TreatUncertainAsBad,PercentDataBad,PercentDataGood,UseSlopedExtrapolation," +
                           "Row,CsvLine,CsvTime,CsvValue,CsvStatus,DeviationId,Part13Time,Part13Value,Part13Status," +
                           "ActualTime,ActualValue,ActualStatus,MatchesCsv,MatchesPart13,DiffersFromPart13," +
                           "BackwardTime,BackwardValue,BackwardStatus");

            List<string> failingTables = new List<string>();

            foreach (AnnexAExampleTable table in AnnexAExamples.Tables)
            {
                List<Part13ExpectedRow> expected = Part13Deviations.Resolve(table);
                List<DataValue> forward = Run(table, false, baseDate);
                List<DataValue> backward = Run(table, true, baseDate);
                backward.Reverse();

                List<Part13RowResult> vsCsv = AggregateResultComparer.Compare(table.Rows, forward, baseDate);
                List<Part13RowResult> vsPart13 = Compare(expected, forward, baseDate);

                bool csvOk = vsCsv.TrueForAll(r => r.Matches);
                bool part13Ok = vsPart13.TrueForAll(r => r.Matches);
                tablesMatchingCsv += csvOk ? 1 : 0;
                tablesMatchingPart13 += part13Ok ? 1 : 0;

                for (int ii = 0; ii < vsPart13.Count; ii++)
                {
                    Part13RowResult row = vsPart13[ii];
                    Part13RowResult csvRow = ii < vsCsv.Count ? vsCsv[ii] : null;
                    AnnexAExpectedValue csvExpected = ii < table.Rows.Count ? table.Rows[ii] : null;
                    DataValue back = ii < backward.Count ? backward[ii] : null;

                    rowsTotal++;
                    rowsMatchingCsv += csvRow != null && csvRow.Matches ? 1 : 0;
                    rowsMatchingPart13 += row.Matches ? 1 : 0;

                    csv.AppendLine(string.Join(",", new[] {
                        table.AggregateName,
                        table.DataSetName,
                        Format(table.Configuration.ProcessingInterval),
                        Format(table.Configuration.Stepped),
                        Format(table.Configuration.TreatUncertainAsBad),
                        Format(table.Configuration.PercentDataBad),
                        Format(table.Configuration.PercentDataGood),
                        Format(table.Configuration.UseSlopedExtrapolation),
                        Format(ii),
                        csvExpected != null ? Format(csvExpected.LineNumber) : string.Empty,
                        csvExpected != null ? AggregateResultComparer.FormatTime(csvExpected.Time) : string.Empty,
                        Quote(csvExpected != null ? csvExpected.ValueText : string.Empty),
                        Quote(csvExpected != null ? csvExpected.StatusText : string.Empty),
                        row.Deviation != null ? row.Deviation.Id : string.Empty,
                        row.Expected != null ? AggregateResultComparer.FormatTime(row.Expected.Time) : string.Empty,
                        Quote(row.Expected != null ? row.Expected.ValueText : string.Empty),
                        Quote(row.Expected != null ? row.Expected.StatusText : string.Empty),
                        row.Actual != null ? AggregateResultComparer.FormatTime(row.Actual.SourceTimestamp - baseDate) : string.Empty,
                        Quote(AggregateResultComparer.FormatValue(row.Actual)),
                        Quote(row.Actual != null ? AggregateResultComparer.FormatStatus(row.Actual.StatusCode) : string.Empty),
                        Format(csvRow != null && csvRow.Matches),
                        Format(row.Matches),
                        Quote(row.Matches ? string.Empty : row.DifferingFields + (row.ValueReason != null ? " (" + row.ValueReason + ")" : string.Empty)),
                        back != null ? AggregateResultComparer.FormatTime(back.SourceTimestamp - baseDate) : string.Empty,
                        Quote(AggregateResultComparer.FormatValue(back)),
                        Quote(back != null ? AggregateResultComparer.FormatStatus(back.StatusCode) : string.Empty)
                    }));
                }

                if (!csvOk || !part13Ok)
                {
                    failingTables.Add(table.Key);
                    summary.AppendLine(
                        "==== " + table.AggregateName + " / " + table.DataSetName + " (" + table.Configuration +
                        "), CSV line " + Format(table.LineNumber));
                    summary.AppendLine("matches Annex A CSV: " + Format(csvOk) + ", matches Part 13 expectation: " + Format(part13Ok));
                    summary.AppendLine("-- versus Annex A CSV:");
                    summary.AppendLine(AggregateResultComparer.FormatDiff(vsCsv, baseDate));

                    if (expected.Exists(e => e.Deviation != null))
                    {
                        summary.AppendLine("-- versus Part 13 expectation (normative overrides applied):");
                        summary.AppendLine(AggregateResultComparer.FormatDiff(vsPart13, baseDate));
                    }

                    summary.AppendLine();
                }
            }

            StringBuilder head = new StringBuilder();
            head.AppendLine("OPC 10000-13 v1.05.07 Annex A (AggregateExamples.csv) versus the stack's aggregate calculators");
            head.AppendLine("Generated " + DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture));
            head.AppendLine(string.Format(CultureInfo.InvariantCulture, "Tables: {0}; matching Annex A: {1}; matching Part 13 expectation: {2}",
                AnnexAExamples.Tables.Count, tablesMatchingCsv, tablesMatchingPart13));
            head.AppendLine(string.Format(CultureInfo.InvariantCulture, "Rows:   {0}; matching Annex A: {1}; matching Part 13 expectation: {2}",
                rowsTotal, rowsMatchingCsv, rowsMatchingPart13));
            head.AppendLine("Normative overrides: " + Format(Part13Deviations.All.Count));
            head.AppendLine("Tables with differences: " + string.Join(" ", failingTables));
            head.AppendLine();

            string directory = ReportDirectory();
            Directory.CreateDirectory(directory);
            string csvPath = Path.Combine(directory, "Part13-AnnexA-report.csv");
            string summaryPath = Path.Combine(directory, "Part13-AnnexA-summary.txt");
            File.WriteAllText(csvPath, csv.ToString(), Encoding.UTF8);
            File.WriteAllText(summaryPath, head.ToString() + summary.ToString(), Encoding.UTF8);

            TestContext.Out.WriteLine(head.ToString());
            TestContext.Out.WriteLine("Report: " + csvPath);
            TestContext.Out.WriteLine("Summary: " + summaryPath);
            TestContext.AddTestAttachment(csvPath);
            TestContext.AddTestAttachment(summaryPath);
        }

        /// <summary>
        /// Runs the stack calculator for the table: forward [12:00:00, End) or backward
        /// (StartTime = End, EndTime = 12:00:00).
        /// </summary>
        public static List<DataValue> Run(AnnexAExampleTable table, bool backward, DateTime baseDate)
        {
            AnnexARawDataSet dataSet = AnnexAExamples.DataSets[table.DataSetName];
            DateTime start = baseDate + table.StartTime;
            DateTime end = baseDate + table.EndTime;

            return AggregateCalculatorRunner.Run(
                table.AggregateId,
                dataSet.ToDataValues(baseDate),
                backward ? end : start,
                backward ? start : end,
                table.Configuration.ProcessingInterval,
                table.Configuration.Stepped,
                table.Configuration.ToAggregateConfiguration());
        }

        /// <summary>
        /// Compares actual values with the resolved expectations.
        /// </summary>
        public static List<Part13RowResult> Compare(List<Part13ExpectedRow> expected, IList<DataValue> actual, DateTime baseDate)
        {
            List<Part13RowResult> rows = AggregateResultComparer.Compare(expected.ConvertAll(e => e.Expected), actual, baseDate);

            for (int ii = 0; ii < rows.Count && ii < expected.Count; ii++)
            {
                rows[ii].Deviation = expected[ii].Deviation;
                rows[ii].AnnexARow = expected[ii].Deviation != null ? expected[ii].AnnexARow : null;
            }

            return rows;
        }

        /// <summary>The directory for the reports.</summary>
        public static string ReportDirectory()
        {
            string directory = Environment.GetEnvironmentVariable("PART13_REPORT_DIR");

            if (string.IsNullOrEmpty(directory))
            {
                directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "Part13");
            }

            return directory;
        }

        private static string Format(object value)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }
    }
}
