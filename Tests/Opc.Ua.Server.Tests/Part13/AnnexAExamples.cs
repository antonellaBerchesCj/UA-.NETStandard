/* ========================================================================
 * OPC UA Part 13 (OPC 10000-13 v1.05.07) aggregate conformance tests.
 *
 * Loads the official Annex A example file "AggregateExamples.csv".
 * OPC 10000-13 v1.05.07 Annex A.1.1: "The Aggregate examples are produced
 * programatically with the resulting output as a CSV file", published at
 * http://www.opcfoundation.org/UA/schemas/1.05/AggregateExamples.csv and in
 * https://github.com/OPCFoundation/UA-Nodeset/blob/UA-1.05.07-2026-04-15/Schema/AggregateExamples.csv
 * (SHA-256 917a8d6a6565586765548be21f80f37ed6acad47e86aed2763754c7b4aca2c3d, unchanged
 * since UA-1.05.02).
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Opc.Ua.Server.Tests.Part13
{
    /// <summary>
    /// The aggregate configuration of an Annex A data set or example table.
    /// </summary>
    public sealed class AnnexAConfiguration
    {
        /// <summary>The Stepped property of the historized variable.</summary>
        public bool Stepped { get; set; }

        /// <summary>TreatUncertainAsBad (Part 13 §4.2.1.2).</summary>
        public bool TreatUncertainAsBad { get; set; }

        /// <summary>PercentDataBad (Part 13 §4.2.1.2).</summary>
        public byte PercentDataBad { get; set; }

        /// <summary>PercentDataGood (Part 13 §4.2.1.2).</summary>
        public byte PercentDataGood { get; set; }

        /// <summary>UseSlopedExtrapolation (Part 13 §4.2.1.2).</summary>
        public bool UseSlopedExtrapolation { get; set; }

        /// <summary>The processing interval in milliseconds (example tables only).</summary>
        public double ProcessingInterval { get; set; }

        /// <summary>
        /// Builds the AggregateConfiguration sent with ReadProcessedDetails. The server
        /// defaults are never used (UseServerCapabilitiesDefaults = false).
        /// </summary>
        public AggregateConfiguration ToAggregateConfiguration()
        {
            return new AggregateConfiguration {
                UseServerCapabilitiesDefaults = false,
                TreatUncertainAsBad = TreatUncertainAsBad,
                PercentDataBad = PercentDataBad,
                PercentDataGood = PercentDataGood,
                UseSlopedExtrapolation = UseSlopedExtrapolation
            };
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Interval={0}ms Stepped={1} TreatUncertainAsBad={2} PercentDataBad={3} PercentDataGood={4} UseSlopedExtrapolation={5}",
                ProcessingInterval,
                Stepped,
                TreatUncertainAsBad,
                PercentDataBad,
                PercentDataGood,
                UseSlopedExtrapolation);
        }
    }

    /// <summary>
    /// One raw value of an Annex A data set.
    /// </summary>
    public sealed class AnnexARawValue
    {
        /// <summary>The time of day (the CSV has no date).</summary>
        public TimeSpan Time { get; set; }

        /// <summary>The value text as written in the CSV.</summary>
        public string ValueText { get; set; }

        /// <summary>The status text as written in the CSV.</summary>
        public string StatusText { get; set; }

        /// <summary>The note column (annotations are written here).</summary>
        public string Notes { get; set; }

        /// <summary>The line in the CSV file.</summary>
        public int LineNumber { get; set; }

        /// <summary>
        /// Converts the CSV row to the raw DataValue that is stored in history.
        /// </summary>
        public DataValue ToDataValue(DateTime baseTime)
        {
            DateTime timestamp = baseTime + Time;
            Variant value = AnnexAExamples.ParseRawValue(ValueText);
            StatusCode status = AnnexAExamples.ParseStatusCodeName(StatusText);
            return new DataValue(value, status, timestamp, timestamp);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:hh\\:mm\\:ss\\.fff} {1} {2}", Time, ValueText, StatusText);
        }
    }

    /// <summary>
    /// A raw data set (Historian1 ... Historian5).
    /// </summary>
    public sealed class AnnexARawDataSet
    {
        /// <summary>The data set name.</summary>
        public string Name { get; set; }

        /// <summary>The configuration of the historized variable.</summary>
        public AnnexAConfiguration Configuration { get; set; }

        /// <summary>The raw values in chronological order.</summary>
        public List<AnnexARawValue> Values { get; } = new List<AnnexARawValue>();

        /// <summary>True if the values are Boolean (Historian4).</summary>
        public bool IsBoolean
        {
            get
            {
                return Values.Exists(v => string.Equals(v.ValueText, "true", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(v.ValueText, "false", StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Returns the raw DataValues for the given base date.
        /// </summary>
        public List<DataValue> ToDataValues(DateTime baseTime)
        {
            return Values.ConvertAll(v => v.ToDataValue(baseTime));
        }
    }

    /// <summary>
    /// One expected processed value of an Annex A example table.
    /// </summary>
    public sealed class AnnexAExpectedValue
    {
        /// <summary>The expected timestamp (time of day).</summary>
        public TimeSpan Time { get; set; }

        /// <summary>The expected value text; empty means no value.</summary>
        public string ValueText { get; set; }

        /// <summary>The expected status text, e.g. "UncertainDataSubNormal, Calculated, Partial".</summary>
        public string StatusText { get; set; }

        /// <summary>The note column.</summary>
        public string Notes { get; set; }

        /// <summary>The line in the CSV file.</summary>
        public int LineNumber { get; set; }

        /// <summary>The expected StatusCode (code bits and aggregate bits).</summary>
        public StatusCode StatusCode
        {
            get { return AnnexAExamples.ParseStatusText(StatusText); }
        }

        /// <summary>Creates a copy.</summary>
        public AnnexAExpectedValue Clone()
        {
            return (AnnexAExpectedValue)MemberwiseClone();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:hh\\:mm\\:ss\\.fff},{1},\"{2}\"", Time, ValueText, StatusText);
        }
    }

    /// <summary>
    /// One Annex A example table: an aggregate applied to a data set.
    /// </summary>
    public sealed class AnnexAExampleTable
    {
        /// <summary>The aggregate BrowseName, e.g. "Minimum2".</summary>
        public string AggregateName { get; set; }

        /// <summary>The data set name, e.g. "Historian1".</summary>
        public string DataSetName { get; set; }

        /// <summary>The configuration used for the request.</summary>
        public AnnexAConfiguration Configuration { get; set; }

        /// <summary>The expected values, one per interval.</summary>
        public List<AnnexAExpectedValue> Rows { get; } = new List<AnnexAExpectedValue>();

        /// <summary>The line of the "Aggregate," header in the CSV file.</summary>
        public int LineNumber { get; set; }

        /// <summary>A unique, stable key: Aggregate/DataSet/Interval.</summary>
        public string Key
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}/{1}/{2}", AggregateName, DataSetName, Configuration.ProcessingInterval);
            }
        }

        /// <summary>The request start time (time of day). All examples start at 12:00:00.</summary>
        public TimeSpan StartTime
        {
            get { return AnnexAExamples.StartOfRequest; }
        }

        /// <summary>
        /// The request end time (time of day). The tables have one row per interval, so the
        /// end time is StartTime + rows x ProcessingInterval.
        /// </summary>
        public TimeSpan EndTime
        {
            get { return StartTime + TimeSpan.FromMilliseconds(Rows.Count * Configuration.ProcessingInterval); }
        }

        /// <summary>The aggregate function NodeId.</summary>
        public NodeId AggregateId
        {
            get { return AnnexAExamples.GetAggregateId(AggregateName); }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Key;
        }
    }

    /// <summary>
    /// Loads and parses the Annex A example file.
    /// </summary>
    public static class AnnexAExamples
    {
        /// <summary>The logical name of the embedded resource.</summary>
        public const string ResourceName = "Part13.AggregateExamples.csv";

        /// <summary>All example requests start at 12:00:00.</summary>
        public static readonly TimeSpan StartOfRequest = new TimeSpan(12, 0, 0);

        /// <summary>
        /// The date used for the time-of-day values in the CSV. The annotations in the CSV refer
        /// to January 2012; any date works for the calculation.
        /// </summary>
        public static readonly DateTime DefaultBaseDate = new DateTime(2012, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        private static readonly object s_lock = new object();
        private static Dictionary<string, AnnexARawDataSet> s_dataSets;
        private static List<AnnexAExampleTable> s_tables;

        /// <summary>The raw data sets by name.</summary>
        public static IReadOnlyDictionary<string, AnnexARawDataSet> DataSets
        {
            get
            {
                EnsureLoaded();
                return s_dataSets;
            }
        }

        /// <summary>All example tables in file order.</summary>
        public static IReadOnlyList<AnnexAExampleTable> Tables
        {
            get
            {
                EnsureLoaded();
                return s_tables;
            }
        }

        /// <summary>Finds a table by its key (Aggregate/DataSet/Interval).</summary>
        public static AnnexAExampleTable GetTable(string key)
        {
            AnnexAExampleTable table = Tables.FirstOrDefault(t => t.Key == key);

            if (table == null)
            {
                throw new ArgumentException("No Annex A example table " + key, nameof(key));
            }

            return table;
        }

        /// <summary>Returns the NodeId of a standard aggregate function from its BrowseName.</summary>
        public static NodeId GetAggregateId(string aggregateName)
        {
            FieldInfo field = typeof(ObjectIds).GetField("AggregateFunction_" + aggregateName, BindingFlags.Public | BindingFlags.Static);

            if (field == null)
            {
                throw new ArgumentException("Unknown aggregate " + aggregateName, nameof(aggregateName));
            }

            return (NodeId)field.GetValue(null);
        }

        /// <summary>
        /// Parses a status name used in the CSV ("Good", "Bad_NoData", "UncertainDataSubNormal", ...).
        /// </summary>
        public static StatusCode ParseStatusCodeName(string name)
        {
            string trimmed = (name ?? string.Empty).Trim().Replace("_", string.Empty).Replace(" ", string.Empty);

            if (trimmed.Length == 0 || string.Equals(trimmed, "NoData", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCodes.BadNoData;
            }

            FieldInfo field = typeof(StatusCodes).GetField(trimmed, BindingFlags.Public | BindingFlags.Static);

            if (field == null)
            {
                throw new FormatException("Unknown StatusCode name '" + name + "'.");
            }

            return new StatusCode(Convert.ToUInt32(field.GetValue(null), CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Parses an expected status text: a status name followed by aggregate bit names.
        /// </summary>
        public static StatusCode ParseStatusText(string text)
        {
            string[] parts = (text ?? string.Empty).Split(',');
            StatusCode code = ParseStatusCodeName(parts[0]);
            AggregateBits bits = AggregateBits.Raw;
            bool hasBits = false;

            for (int ii = 1; ii < parts.Length; ii++)
            {
                string flag = parts[ii].Trim();

                if (flag.Length == 0)
                {
                    continue;
                }

                hasBits = true;

                switch (flag)
                {
                    case "Calculated": bits |= AggregateBits.Calculated; break;
                    case "Interpolated": bits |= AggregateBits.Interpolated; break;
                    case "Raw": break;
                    case "Partial": bits |= AggregateBits.Partial; break;
                    case "MultipleValues": bits |= AggregateBits.MultipleValues; break;
                    case "ExtraData": bits |= AggregateBits.ExtraData; break;
                    default: throw new FormatException("Unknown aggregate bit '" + flag + "' in '" + text + "'.");
                }
            }

            if (hasBits)
            {
                code = code.SetAggregateBits(bits);
            }

            return code;
        }

        /// <summary>
        /// Parses a raw value. Empty and "undefined" mean no value; "true"/"false" are Boolean;
        /// everything else is a Double.
        /// </summary>
        public static Variant ParseRawValue(string text)
        {
            string trimmed = (text ?? string.Empty).Trim();

            if (trimmed.Length == 0 || string.Equals(trimmed, "undefined", StringComparison.OrdinalIgnoreCase))
            {
                return Variant.Null;
            }

            if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase))
            {
                return new Variant(true);
            }

            if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
            {
                return new Variant(false);
            }

            return new Variant(double.Parse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        /// <summary>Parses "hh:mm:ss" or "hh:mm:ss.fff".</summary>
        public static TimeSpan ParseTime(string text)
        {
            string[] formats = new[] { @"hh\:mm\:ss\.fff", @"hh\:mm\:ss" };
            return TimeSpan.ParseExact(text.Trim(), formats, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Splits a CSV line. Quoted fields may contain commas.
        /// </summary>
        public static List<string> SplitCsv(string line)
        {
            List<string> fields = new List<string>();
            System.Text.StringBuilder current = new System.Text.StringBuilder();
            bool quoted = false;

            for (int ii = 0; ii < line.Length; ii++)
            {
                char ch = line[ii];

                if (ch == '"')
                {
                    if (quoted && ii + 1 < line.Length && line[ii + 1] == '"')
                    {
                        current.Append('"');
                        ii++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (ch == ',' && !quoted)
                {
                    fields.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(ch);
                }
            }

            fields.Add(current.ToString());
            return fields;
        }

        /// <summary>Reads the CSV text from the embedded resource.</summary>
        public static string ReadCsvText()
        {
            Assembly assembly = typeof(AnnexAExamples).Assembly;

            using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException(
                        "Embedded resource " + ResourceName + " not found. Available: " +
                        string.Join(", ", assembly.GetManifestResourceNames()));
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>Parses the CSV text.</summary>
        public static void Parse(string csv, out Dictionary<string, AnnexARawDataSet> dataSets, out List<AnnexAExampleTable> tables)
        {
            dataSets = new Dictionary<string, AnnexARawDataSet>(StringComparer.Ordinal);
            tables = new List<AnnexAExampleTable>();

            string[] lines = csv.Replace("\r\n", "\n").Split('\n');
            bool inRaw = false;
            bool inProcessed = false;
            string currentAggregate = null;
            int aggregateLine = 0;
            int ii = 0;

            while (ii < lines.Length)
            {
                string line = lines[ii].Trim();

                if (line == "Start of Raw Data Tables") { inRaw = true; inProcessed = false; ii++; continue; }
                if (line == "End of Raw Data Tables") { inRaw = false; ii++; continue; }
                if (line == "Start of Processed Data Tables") { inProcessed = true; ii++; continue; }

                if (inProcessed && line.StartsWith("Aggregate,", StringComparison.Ordinal))
                {
                    currentAggregate = line.Substring("Aggregate,".Length).Trim();
                    aggregateLine = ii + 1;
                    ii++;
                    continue;
                }

                if ((inRaw || inProcessed) && line.StartsWith("Historian", StringComparison.Ordinal) && line.IndexOf(',') < 0)
                {
                    string name = line;
                    AnnexAConfiguration configuration = new AnnexAConfiguration();
                    ii++;

                    // configuration lines until the column header.
                    while (ii < lines.Length && !lines[ii].StartsWith("Timestamp,", StringComparison.Ordinal))
                    {
                        List<string> kv = SplitCsv(lines[ii]);

                        if (kv.Count >= 2)
                        {
                            ApplySetting(configuration, kv[0].Trim(), kv[1].Trim());
                        }

                        ii++;
                    }

                    ii++; // skip the column header.

                    if (inRaw)
                    {
                        AnnexARawDataSet dataSet = new AnnexARawDataSet { Name = name, Configuration = configuration };

                        // rows start with a timestamp or, for the end-of-data row, with a comma.
                        // "End of Raw Data Tables" follows the last data set without a blank line.
                        while (ii < lines.Length && lines[ii].Trim().Length > 0 &&
                               (char.IsDigit(lines[ii].Trim()[0]) || lines[ii].Trim()[0] == ','))
                        {
                            List<string> fields = SplitCsv(lines[ii]);

                            // the last row (",,No Data,...") marks the end of data and has no timestamp.
                            if (fields[0].Trim().Length > 0)
                            {
                                dataSet.Values.Add(new AnnexARawValue {
                                    Time = ParseTime(fields[0]),
                                    ValueText = fields.Count > 1 ? fields[1].Trim() : string.Empty,
                                    StatusText = fields.Count > 2 ? fields[2].Trim() : string.Empty,
                                    Notes = fields.Count > 3 ? fields[3].Trim() : string.Empty,
                                    LineNumber = ii + 1
                                });
                            }

                            ii++;
                        }

                        dataSets[name] = dataSet;
                    }
                    else
                    {
                        AnnexAExampleTable table = new AnnexAExampleTable {
                            AggregateName = currentAggregate,
                            DataSetName = name,
                            Configuration = configuration,
                            LineNumber = aggregateLine
                        };

                        // rows start with a timestamp; "End of Processed Data Tables" ends the file.
                        while (ii < lines.Length && lines[ii].Trim().Length > 0 &&
                               char.IsDigit(lines[ii].Trim()[0]))
                        {
                            List<string> fields = SplitCsv(lines[ii]);

                            table.Rows.Add(new AnnexAExpectedValue {
                                Time = ParseTime(fields[0]),
                                ValueText = fields.Count > 1 ? fields[1].Trim() : string.Empty,
                                StatusText = fields.Count > 2 ? fields[2].Trim() : string.Empty,
                                Notes = fields.Count > 3 ? fields[3].Trim() : string.Empty,
                                LineNumber = ii + 1
                            });

                            ii++;
                        }

                        tables.Add(table);
                    }

                    continue;
                }

                ii++;
            }
        }

        private static void ApplySetting(AnnexAConfiguration configuration, string name, string value)
        {
            switch (name)
            {
                case "Stepped": configuration.Stepped = bool.Parse(value); break;
                case "Treat Uncertain as Bad": configuration.TreatUncertainAsBad = bool.Parse(value); break;
                case "Percent Bad": configuration.PercentDataBad = byte.Parse(value, CultureInfo.InvariantCulture); break;
                case "Percent Good": configuration.PercentDataGood = byte.Parse(value, CultureInfo.InvariantCulture); break;
                case "Use Sloped Extrapolation": configuration.UseSlopedExtrapolation = bool.Parse(value); break;
                case "Processing Interval": configuration.ProcessingInterval = double.Parse(value, CultureInfo.InvariantCulture); break;
            }
        }

        private static void EnsureLoaded()
        {
            lock (s_lock)
            {
                if (s_tables != null)
                {
                    return;
                }

                Dictionary<string, AnnexARawDataSet> dataSets;
                List<AnnexAExampleTable> tables;
                Parse(ReadCsvText(), out dataSets, out tables);
                s_dataSets = dataSets;
                s_tables = tables;
            }
        }
    }
}
