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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Calculates the value of an aggregate.
    /// </summary>
    public class AggregateCalculator : IAggregateCalculator
    {
        /// <summary>
        /// Create calculator
        /// </summary>
        [Obsolete("Use constructor with ITelemetryContext")]
        public AggregateCalculator(
            NodeId aggregateId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration)
            : this(aggregateId, startTime, endTime, processingInterval, stepped, configuration, null)
        {
        }

        /// <summary>
        /// Initializes the calculation stream.
        /// </summary>
        /// <param name="aggregateId">The aggregate function to apply.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="processingInterval">The processing interval.</param>
        /// <param name="stepped">Whether to use stepped interpolation.</param>
        /// <param name="configuration">The aggregate configuration.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public AggregateCalculator(
            NodeId aggregateId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration,
            ITelemetryContext telemetry)
        {
            m_logger = telemetry.CreateLogger<AggregateCalculator>();
            AggregateId = aggregateId;
            StartTime = startTime;
            EndTime = endTime;
            ProcessingInterval = processingInterval;
            m_stepped = stepped;
            m_validDataTypes = AggregateManager.GetValidDataTypes(aggregateId);
            m_numericOnly = m_validDataTypes == AggregateManager.AggregateDataTypes.Numeric;
            Configuration = configuration;
            TimeFlowsBackward = endTime < startTime;

            if (processingInterval == 0)
            {
                if (endTime == DateTime.MinValue || startTime == DateTime.MinValue)
                {
                    throw new ArgumentException(
                        "Non-zero processingInterval required.",
                        nameof(processingInterval));
                }

                ProcessingInterval = Math.Abs((endTime - startTime).TotalMilliseconds);
            }

            m_values = new LinkedList<DataValue>();
        }

        /// <summary>
        /// The aggregate function applied by the calculator.
        /// </summary>
        public NodeId AggregateId { get; }

        /// <summary>
        /// The interval contains the start of data (3.1.9: the partial interval at the start of historical)
        /// </summary>
        protected bool ContainsDataStart(TimeSlice slice)
        {
            return EarliestRawTimestamp != DateTime.MaxValue &&
                   EarliestRawTimestamp > slice.StartTime &&
                   EarliestRawTimestamp < slice.EndTime;
        }

        /// <summary>
        /// True if the interval lies before the start or after the end of data (Part 13 Table 68).
        /// The interval is [StartTime, EndTime), or (StartTime, EndTime] when time flows backward, as
        /// for the raw values (§5.4.2.2).
        /// </summary>
        private protected bool IsBeyondData(TimeSlice slice)
        {
            if (m_startOfData == DateTime.MinValue)
            {
                return true;
            }

            if (TimeFlowsBackward)
            {
                return slice.EndTime < m_startOfData || slice.StartTime >= m_endOfData;
            }

            return slice.EndTime <= m_startOfData || slice.StartTime > m_endOfData;
        }

        /// <summary>
        /// The number of queued Annotations in the interval, with the interval boundaries of the raw
        /// values: [StartTime, EndTime), or (StartTime, EndTime] when time flows backward.
        /// </summary>
        private protected int CountAnnotations(TimeSlice slice)
        {
            return IndexOfAnnotation(slice.EndTime) - IndexOfAnnotation(slice.StartTime);
        }

        /// <summary>
        /// The index of the first queued Annotation at or after the time (after it when time flows
        /// backward).
        /// </summary>
        private int IndexOfAnnotation(DateTime time)
        {
            int low = 0;
            int high = m_annotations.Count;

            while (low < high)
            {
                int middle = low + ((high - low) / 2);

                if (m_annotations[middle] < time || (TimeFlowsBackward && m_annotations[middle] == time))
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        /// <summary>
        /// Queues a raw value for processing.
        /// </summary>
        /// <param name="value">The data value to process.</param>
        /// <returns>True if successful, false if the timestamp has been superseded by values already in the stream.</returns>
        public bool QueueRawValue(DataValue value)
        {
            // ignore bad data.
            if (value == null)
            {
                return false;
            }

            // ignore placeholders in the stream (e.g. the "first archive entry" Bad_NoData value):
            // they are not raw data, so they neither start the data nor take part in a calculation.
            if (value.StatusCode.CodeBits == StatusCodes.BadNoData ||
                value.StatusCode.CodeBits == StatusCodes.BadBoundNotFound)
            {
                return true;
            }

            // ensure values are being queued in the right order.
            if (TimeFlowsBackward)
            {
                if (m_values.First != null && CompareTimestamps(value, m_values.First) > 0)
                {
                    return false;
                }
            }
            else if (m_values.Last != null && CompareTimestamps(value, m_values.Last) < 0)
            {
                return false;
            }

            // non-numeric values cannot be interpolated with a sloped line.
            if (!m_nonNumericData &&
                value.WrappedValue.Value != null &&
                value.WrappedValue.TypeInfo != null &&
                !TypeInfo.IsNumericType(value.WrappedValue.TypeInfo.BuiltInType))
            {
                m_nonNumericData = true;
            }

            // track the chronological start and end of the queued data. Values arrive newest-first
            // when time flows backward, so the queue order cannot be used for either; rejected
            // out-of-order values do not move the data edges.
            if (m_startOfData == DateTime.MinValue || value.SourceTimestamp < m_startOfData)
            {
                m_startOfData = value.SourceTimestamp;
            }

            if (value.SourceTimestamp > m_endOfData)
            {
                m_endOfData = value.SourceTimestamp;
            }

            if (value.SourceTimestamp < EarliestRawTimestamp)
            {
                EarliestRawTimestamp = value.SourceTimestamp;
            }

            // ensure value list is always ordered from past to future.
            if (TimeFlowsBackward)
            {
                m_values.AddFirst(value);
            }
            else
            {
                m_values.AddLast(value);
            }

            return true;
        }

        /// <summary>
        /// Queues the timestamp of an Annotation of the variable for the AnnotationCount aggregate
        /// (Part 13 §5.4.3.20). Annotations are not raw values: they do not move the start or end of
        /// data, and the other aggregates ignore them. They can be queued in any order.
        /// </summary>
        /// <param name="timestamp">The timestamp of the Annotation, i.e. of the value it annotates.</param>
        public void QueueAnnotation(DateTime timestamp)
        {
            int index = m_annotations.BinarySearch(timestamp);
            m_annotations.Insert(index < 0 ? ~index : index, timestamp);
        }

        /// <summary>
        /// Returns the next processed value.
        /// </summary>
        /// <param name="returnPartial">If true a partial interval should be processed.</param>
        /// <returns>The processed value. Null if nothing available and returnPartial is false.</returns>
        public DataValue GetProcessedValue(bool returnPartial)
        {
            // check if all done.
            if (Complete)
            {
                return null;
            }

            // update the slice.
            if (CurrentSlice == null)
            {
                CurrentSlice = CreateSlice(null);
            }
            else
            {
                UpdateSlice(CurrentSlice);
            }

            // check if a value can be produced.
            if (!CurrentSlice.Complete && !returnPartial)
            {
                return null;
            }

            // check if the slice extends beyond the range of available data.
            DateTime earlyTime = CurrentSlice.StartTime;
            DateTime lateTime = CurrentSlice.EndTime;

            if (m_values.First == null ||
                CompareTimestamps(lateTime, m_values.First) < 0 ||
                CompareTimestamps(earlyTime, m_values.Last) > 0)
            {
                CurrentSlice.OutOfDataRange = true;
            }

            m_logger.LogTrace("Computing Aggregate {StartTime:HH:mm:ss.fff}", CurrentSlice.StartTime);

            // compute the value.
            DataValue value;

            if (!IsValidConfiguration(Configuration))
            {
                // Part 13 §4.2.1.2: PercentDataGood/PercentDataBad above 100 or with
                // PercentDataGood < 100 - PercentDataBad give Bad_AggregateInvalidInputs.
                value = GetInvalidInputsValue(CurrentSlice);
            }
            else if (!UsesValidDataTypes(CurrentSlice))
            {
                // Part 13 Table 50: the aggregate does not support the data type of a value the
                // interval uses; Table 45: invalid data inputs give Bad_AggregateInvalidInputs.
                value = GetInvalidInputsValue(CurrentSlice);
            }
            else
            {
                try
                {
                    // no raw data at all (only placeholders were queued).
                    value = m_values.First == null
                        ? GetNoDataValue(CurrentSlice)
                        : ComputeValue(CurrentSlice);
                }
                // Part 13 Table 45: errors attempting to perform data conversions.
                catch (OverflowException)
                {
                    value = GetInvalidInputsValue(CurrentSlice);
                }
                catch (InvalidCastException)
                {
                    value = GetInvalidInputsValue(CurrentSlice);
                }
            }

            // check if overlapping the start or end of data (Part 13 §5.3.3.2): the interval that
            // contains the start of data, and the interval that contains the end of data unless
            // the value was extrapolated. Both checks use the chronological interval, so they
            // apply in either time direction.
            if (SetPartialBit && m_startOfData != DateTime.MinValue)
            {
                bool containsStartOfData = m_startOfData > earlyTime && m_startOfData < lateTime;
                bool containsEndOfData = !UsingExtrapolation && m_endOfData >= earlyTime && m_endOfData < lateTime;

                // the last interval is shorter than the processing interval and data exists beyond
                // the end time (§5.3.3.2).
                bool shortenedWithDataBeyondEnd = CurrentSlice.Partial &&
                    (TimeFlowsBackward ? m_startOfData < earlyTime : m_endOfData >= lateTime);

                if (containsStartOfData || containsEndOfData || shortenedWithDataBeyondEnd)
                {
                    value.StatusCode = value.StatusCode.SetAggregateBits(value.StatusCode.AggregateBits | AggregateBits.Partial);
                }
            }

            // force value to null if status code is Bad/BadNoData.
            if (StatusCode.IsBad(value.StatusCode))
            {
                value.WrappedValue = Variant.Null;
            }

            // delete unneeded data.
            if (TimeFlowsBackward)
            {
                if (CurrentSlice.LateBound != null)
                {
                    LinkedListNode<DataValue> ii = CurrentSlice.LateBound.Next;

                    while (ii != null)
                    {
                        LinkedListNode<DataValue> next = ii.Next;
                        m_values.Remove(ii);
                        ii = next;
                    }
                }
            }
            else if (CurrentSlice.EarlyBound != null)
            {
                LinkedListNode<DataValue> ii = CurrentSlice.EarlyBound.Previous;

                if (CurrentSlice.SecondEarlyBound != null)
                {
                    ii = CurrentSlice.SecondEarlyBound.Previous;
                }

                while (ii != null)
                {
                    LinkedListNode<DataValue> next = ii.Previous;
                    m_values.Remove(ii);
                    ii = next;
                }
            }

            // check if more to be done.
            Complete =
                (!TimeFlowsBackward && CurrentSlice.EndTime >= EndTime) ||
                (TimeFlowsBackward && CurrentSlice.StartTime <= EndTime);

            if (!Complete)
            {
                CurrentSlice = CreateSlice(CurrentSlice);
            }

            // return the processed value.
            return value;
        }

        /// <summary>
        /// Returns true if the specified time is later than the end of the current interval.
        /// </summary>
        /// <remarks>Return true if time flows forward and the time is later than the end time.</remarks>
        public bool HasEndTimePassed(DateTime currentTime)
        {
            if (CurrentSlice == null)
            {
                return false;
            }

            if (TimeFlowsBackward)
            {
                return CurrentSlice.EndTime >= currentTime;
            }

            return CurrentSlice.EndTime <= currentTime;
        }

        /// <summary>
        /// The start time for the request.
        /// </summary>
        protected DateTime StartTime { get; }

        /// <summary>
        /// The end time for the request.
        /// </summary>
        protected DateTime EndTime { get; }

        /// <summary>
        /// The processing interval for the request.
        /// </summary>
        protected double ProcessingInterval { get; }

        /// <summary>
        /// True if the data series requires stepped interpolation: the variable is configured as
        /// Stepped, or its values are not numeric. Only numeric values can be interpolated with a
        /// sloped line (Part 13 §5.4.2.3, Table 50), so Boolean, String and other non-numeric values
        /// always use the value of the prior raw value. A numeric-only aggregate keeps the configured
        /// interpolation, because it rejects the intervals that use non-numeric values.
        /// </summary>
        protected bool Stepped => m_stepped || (m_nonNumericData && !m_numericOnly);

        /// <summary>
        /// True if values calculated by sloped interpolation are converted back to the data type of
        /// the raw values (OPC 10000-4 §7.7.3), as aggregates that return the raw data type need.
        /// TimeAverage and Total return a Double and use the real-valued bound (Part 13 §3.1.8), so
        /// their calculator clears it.
        /// </summary>
        protected bool CastBoundsToSourceType { get; set; } = true;

        /// <summary>
        /// The configuration to use when processing.
        /// </summary>
        protected AggregateConfiguration Configuration { get; }

        /// <summary>
        /// Whether to use the server timestamp for all processing.
        /// </summary>
        protected bool UseServerTimestamp { get; }

        /// <summary>
        /// True if data is being processed in reverse order.
        /// </summary>
        protected bool TimeFlowsBackward { get; }

        /// <summary>
        /// Whether to use the server timestamp for all processing.
        /// </summary>
        protected TimeSlice CurrentSlice { get; private set; }

        /// <summary>
        /// True if all values required for the request have been received and processed
        /// </summary>
        protected bool Complete { get; private set; }

        /// <summary>
        /// True if the GetProcessedValue method should set the Partial bit when appropriate.
        /// </summary>
        protected bool SetPartialBit { get; set; }

        /// <summary>
        /// True if data is extrapolated after the end of data.
        /// </summary>
        protected bool UsingExtrapolation { get; set; }


        /// <summary>
        /// Original data type of the raw values (e.g. Boolean, String, Double), before any aggregation.
        /// Determines how regions are treated: types like Boolean/String aren't interpolatable,
        /// so they follow different Duration rules than numeric types.
        /// </summary>
        protected TypeInfo RawDataType
        {
            get
            {
                for (var i = m_values.First; i != null; i = i.Next)
                {
                    var typeInfo = i.Value.WrappedValue.TypeInfo;
                    if (!i.Value.WrappedValue.IsNull && typeInfo != null && typeInfo.BuiltInType != BuiltInType.Null)
                    {
                        return typeInfo;
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// Earliest raw point timestamp, excluding BadNoData. Identifies the start of available data.
        /// </summary>
        private DateTime EarliestRawTimestamp { get; set; } = DateTime.MaxValue;

        /// <summary>
        /// Compares timestamps for two DataValues according to the current UseServerTimestamp setting.
        /// </summary>
        /// <param name="value1">The first value to compare.</param>
        /// <param name="value2">The second value to compare.</param>
        /// <returns>Less than 0 if value1 is earlier than value2; 0 if they are equal; Greater than zero otherwise.</returns>
        protected int CompareTimestamps(DataValue value1, DataValue value2)
        {
            if (value1 == null)
            {
                return value2 == null ? 0 : -1;
            }

            if (value2 == null)
            {
                return +1;
            }

            if (UseServerTimestamp)
            {
                int result = value1.ServerTimestamp.CompareTo(value2.ServerTimestamp);

                if (result == 0)
                {
                    return value1.ServerPicoseconds.CompareTo(value2.ServerPicoseconds);
                }

                return result;
            }
            else
            {
                int result = value1.SourceTimestamp.CompareTo(value2.SourceTimestamp);

                if (result == 0)
                {
                    return value1.SourcePicoseconds.CompareTo(value2.SourcePicoseconds);
                }

                return result;
            }
        }

        /// <summary>
        /// Compares timestamps for two DataValues according to the current UseServerTimestamp setting.
        /// </summary>
        /// <param name="value1">The first value to compare.</param>
        /// <param name="value2">The second value to compare.</param>
        /// <returns>Less than 0 if value1 is earlier than value2; 0 if they are equal; Greater than zero otherwise.</returns>
        protected int CompareTimestamps(DataValue value1, LinkedListNode<DataValue> value2)
        {
            if (value2 == null)
            {
                return value1 == null ? 0 : +1;
            }

            return CompareTimestamps(value1, value2.Value);
        }

        /// <summary>
        /// Compares timestamps for two DataValues according to the current UseServerTimestamp setting.
        /// </summary>
        /// <param name="value1">The first value to compare.</param>
        /// <param name="value2">The second value to compare.</param>
        /// <returns>Less than 0 if value1 is earlier than value2; 0 if they are equal; Greater than zero otherwise.</returns>
        protected int CompareTimestamps(
            LinkedListNode<DataValue> value1,
            LinkedListNode<DataValue> value2)
        {
            if (value1 == null)
            {
                return value2 == null ? 0 : -1;
            }

            if (value2 == null)
            {
                return +1;
            }

            return CompareTimestamps(value1.Value, value2.Value);
        }

        /// <summary>
        /// Compares timestamps for a timestamp to a DataValue according to the current UseServerTimestamp setting.
        /// </summary>
        /// <param name="value1">The timestamp to compare.</param>
        /// <param name="value2">The data value to compare.</param>
        /// <returns>Less than 0 if value1 is earlier than value2; 0 if they are equal; Greater than zero otherwise.</returns>
        protected int CompareTimestamps(DateTime value1, LinkedListNode<DataValue> value2)
        {
            if (value2 == null || value2.Value == null)
            {
                return +1;
            }

            if (UseServerTimestamp)
            {
                return value1.CompareTo(value2.Value.ServerTimestamp);
            }

            return value1.CompareTo(value2.Value.SourceTimestamp);
        }

        /// <summary>
        /// Checks if the value is good according to the configuration rules.
        /// </summary>
        /// <param name="value">The value to test.</param>
        /// <returns>True if the value is good.</returns>
        protected bool IsGood(DataValue value)
        {
            if (value == null)
            {
                return false;
            }

            if (Configuration.TreatUncertainAsBad)
            {
                if (StatusCode.IsNotGood(value.StatusCode))
                {
                    return false;
                }
            }
            else if (StatusCode.IsBad(value.StatusCode))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if a raw value can form a bounding value, or if a calculated bounding value can be
        /// used: it is not Bad. Bounding values are built from non-Bad raw values and
        /// TreatUncertainAsBad does not apply to them (Part 13 §3.1.8, §3.1.9; WG answer to Mantis
        /// 11462). An Uncertain raw value makes the bound Uncertain_DataSubNormal, and an Uncertain
        /// bound is returned as it is (Tables 76–78).
        /// </summary>
        /// <param name="value">The value to test.</param>
        /// <returns>True if the value is not Bad.</returns>
        protected static bool IsBoundCandidate(DataValue value)
        {
            return value != null && !StatusCode.IsBad(value.StatusCode);
        }

        /// <summary>
        /// Stores information about a slice of data to be processed.
        /// </summary>
        protected class TimeSlice
        {
            /// <summary>
            /// The start time for the slice.
            /// </summary>
            public DateTime StartTime { get; set; }

            /// <summary>
            /// The end time for the slice.
            /// </summary>
            public DateTime EndTime { get; set; }

            /// <summary>
            /// True if the slice is a partial interval.
            /// </summary>
            public bool Partial { get; set; }

            /// <summary>
            /// True if all of the data required to process the slice has been collected.
            /// </summary>
            public bool Complete { get; set; }

            /// <summary>
            /// True if the slice includes times that are outside of the available dataset.
            /// </summary>
            public bool OutOfDataRange { get; set; }

            /// <summary>
            /// The first early bound for the slice.
            /// </summary>
            public LinkedListNode<DataValue> EarlyBound { get; set; }

            /// <summary>
            /// The latest non-bad value before the slice.
            /// </summary>
            internal LinkedListNode<DataValue> NonBadEarlyBound { get; set; }

            /// <summary>
            /// The second early bound for the slice (always earlier than the first).
            /// </summary>
            public LinkedListNode<DataValue> SecondEarlyBound { get; set; }

            /// <summary>
            /// The beginning of the slice.
            /// </summary>
            public LinkedListNode<DataValue> Begin { get; set; }

            /// <summary>
            /// The end of the slice.
            /// </summary>
            public LinkedListNode<DataValue> End { get; set; }

            /// <summary>
            /// The late bound for the slice.
            /// </summary>
            public LinkedListNode<DataValue> LateBound { get; set; }

            /// <summary>
            /// The last value which was processed.
            /// </summary>
            public LinkedListNode<DataValue> LastProcessedValue { get; set; }
        }

        /// <summary>
        /// Creates a new time slice to process.
        /// </summary>
        /// <param name="previousSlice">The previous processed slice.</param>
        /// <returns>The new time slice.</returns>
        protected TimeSlice CreateSlice(TimeSlice previousSlice)
        {
            var slice = new TimeSlice();

            // ensure slice is oriented from past to future even if request is going backwards.
            if (TimeFlowsBackward)
            {
                if (previousSlice == null)
                {
                    slice.EndTime = StartTime;
                }
                else
                {
                    slice.EndTime = previousSlice.StartTime;
                }

                slice.StartTime = slice.EndTime.AddMilliseconds(-ProcessingInterval);

                // check for end of request.
                if (slice.StartTime < EndTime)
                {
                    slice.StartTime = EndTime;
                    slice.Partial = true;
                }
            }
            else
            {
                if (previousSlice == null)
                {
                    slice.StartTime = StartTime;
                }
                else
                {
                    slice.StartTime = previousSlice.EndTime;
                }

                slice.EndTime = slice.StartTime.AddMilliseconds(ProcessingInterval);

                // check for end of request.
                if (slice.EndTime > EndTime)
                {
                    slice.EndTime = EndTime;
                    slice.Partial = true;
                }
            }

            // update the slice with current data.
            UpdateSlice(slice);
            return slice;
        }

        /// <summary>
        /// Creates a new time slice to process.
        /// </summary>
        /// <param name="slice">The slice to update.</param>
        /// <returns>True if the slice is complete.</returns>
        protected bool UpdateSlice(TimeSlice slice)
        {
            // check if nothing to do.
            if (m_values.First == null)
            {
                return slice.Complete;
            }

            // restart processing from where it left off.
            LinkedListNode<DataValue> start = m_values.First;

            if (!TimeFlowsBackward && slice.LastProcessedValue != null)
            {
                start = slice.LastProcessedValue.Next;
            }

            // reset the begin bound each time we go through the values.
            if (TimeFlowsBackward)
            {
                slice.Begin = null;
            }

            // initialize slice from value list. The early and late bounds are the nearest non-Bad raw
            // values: TreatUncertainAsBad does not apply to bounds (Mantis 11462).
            for (LinkedListNode<DataValue> ii = start; ii != null; ii = ii.Next)
            {
                if (TimeFlowsBackward)
                {
                    // check if before the beginning of the slice.
                    if (CompareTimestamps(slice.StartTime, ii) >= 0)
                    {
                        if (StatusCode.IsNotBad(ii.Value.StatusCode))
                        {
                            slice.NonBadEarlyBound = ii;
                        }

                        if (IsBoundCandidate(ii.Value))
                        {
                            slice.SecondEarlyBound = slice.EarlyBound;
                            slice.EarlyBound = ii;
                        }

                        continue;
                    }

                    // check if after the end if the slice.
                    if (CompareTimestamps(slice.EndTime, ii) < 0)
                    {
                        if (IsBoundCandidate(ii.Value))
                        {
                            slice.LateBound = ii;
                            break;
                        }

                        continue;
                    }

                    // save first value in the slice.
                    slice.End ??= ii;

                    // save end of slice.
                    if (slice.Begin == null)
                    {
                        slice.Begin = ii;
                        slice.LastProcessedValue = ii;
                    }
                }
                else
                {
                    // check if before the beginning of the slice.
                    if (CompareTimestamps(slice.StartTime, ii) > 0)
                    {
                        if (StatusCode.IsNotBad(ii.Value.StatusCode))
                        {
                            slice.NonBadEarlyBound = ii;
                        }

                        if (IsBoundCandidate(ii.Value))
                        {
                            slice.SecondEarlyBound = slice.EarlyBound;
                            slice.EarlyBound = ii;
                            slice.LastProcessedValue = ii;
                        }

                        continue;
                    }

                    // check if after the end if the slice.
                    if (CompareTimestamps(slice.EndTime, ii) < 0)
                    {
                        if (IsBoundCandidate(ii.Value))
                        {
                            slice.LateBound = ii;
                            slice.LastProcessedValue = ii;
                            break;
                        }

                        continue;
                    }

                    // save first value in the slice.
                    slice.Begin ??= ii;

                    // save end of slice.
                    slice.End = ii;
                    slice.LastProcessedValue = ii;
                }
            }

            // check if no more data needs to be collected.
            LinkedListNode<DataValue> requiredBound;
            if (TimeFlowsBackward)
            {
                // only need second early bound if using sloped extrapolation and there is no late bound.
                if (Configuration.UseSlopedExtrapolation && slice.LateBound == null)
                {
                    requiredBound = slice.SecondEarlyBound;
                }
                else
                {
                    requiredBound = slice.EarlyBound;
                }
            }
            else
            {
                requiredBound = slice.LateBound;
            }

            // all done if required bound exists.
            if (requiredBound != null)
            {
                slice.Complete = true;
            }

            return slice.Complete;
        }

        /// <summary>
        /// Calculates the value for the timeslice.
        /// </summary>
        /// <param name="slice">The slice to process.</param>
        /// <returns>The processed value.</returns>
        protected virtual DataValue ComputeValue(TimeSlice slice)
        {
            return Interpolate(slice);
        }

        /// <summary>
        /// Calculate the interpolate aggregate for the timeslice.
        /// </summary>
        protected DataValue Interpolate(TimeSlice slice)
        {
            if (TimeFlowsBackward)
            {
                return Interpolate(slice.EndTime, slice);
            }

            return Interpolate(slice.StartTime, slice);
        }

        /// <summary>
        /// Return a value indicating there is no data in the time slice.
        /// </summary>
        protected DataValue GetNoDataValue(TimeSlice slice)
        {
            if (TimeFlowsBackward)
            {
                return GetNoDataValue(slice.EndTime);
            }

            return GetNoDataValue(slice.StartTime);
        }

        /// <summary>
        /// Returns the timestamp to use for the slice value.
        /// </summary>
        protected DateTime GetTimestamp(TimeSlice slice)
        {
            if (TimeFlowsBackward)
            {
                return slice.EndTime;
            }

            return slice.StartTime;
        }

        /// <summary>
        /// Return a value indicating there is no data in the time slice.
        /// </summary>
        protected DataValue GetNoDataValue(DateTime timestamp)
        {
            return new DataValue(Variant.Null, StatusCodes.BadNoData, timestamp, timestamp);
        }

        /// <summary>
        /// Returns a Bad_AggregateInvalidInputs value for the time slice (Part 13 Table 45).
        /// </summary>
        private DataValue GetInvalidInputsValue(TimeSlice slice)
        {
            DateTime timestamp = GetTimestamp(slice);
            return new DataValue(Variant.Null, StatusCodes.BadAggregateInvalidInputs, timestamp, timestamp);
        }

        /// <summary>
        /// True if every non-Bad value the interval uses has a data type the aggregate supports
        /// (Part 13 Table 50). The values used are the raw values in the interval and the raw values
        /// the bounds are calculated from; Interpolative uses only the bound at the start time,
        /// DeltaBounds only its two bounds, and NumberOfTransitions also the non-Bad value before the
        /// interval. A Bad value is not used, so its data type does not matter.
        /// </summary>
        private bool UsesValidDataTypes(TimeSlice slice)
        {
            if (m_validDataTypes == AggregateManager.AggregateDataTypes.All || m_values.First == null)
            {
                return true;
            }

            NodeId id = AggregateId;
            bool interpolative = id == ObjectIds.AggregateFunction_Interpolative;

            // Interpolative and DeltaBounds use only their bounds, not the raw values in between.
            if (!interpolative && id != ObjectIds.AggregateFunction_DeltaBounds)
            {
                List<DataValue> values = GetValues(slice);

                if (values != null && !values.TrueForAll(IsValidDataType))
                {
                    return false;
                }
            }

            // Interpolative returns the value at the start of the interval in the request direction.
            if (interpolative)
            {
                return BoundSourcesAreValid(GetTimestamp(slice), true);
            }

            if (id == ObjectIds.AggregateFunction_TimeAverage || id == ObjectIds.AggregateFunction_Total)
            {
                return BoundSourcesAreValid(slice.StartTime, true) && BoundSourcesAreValid(slice.EndTime, true);
            }

            if (id == ObjectIds.AggregateFunction_NumberOfTransitions)
            {
                // slices are chronological in both directions: StartTime is the early time. The
                // previous value is the non-Bad value before the interval (Table 72), a bound.
                DataValue prior = null;

                for (LinkedListNode<DataValue> ii = m_values.First; ii != null && CompareTimestamps(slice.StartTime, ii) > 0; ii = ii.Next)
                {
                    if (IsBoundCandidate(ii.Value))
                    {
                        prior = ii.Value;
                    }
                }

                return IsValidDataType(prior);
            }

            if (id == ObjectIds.AggregateFunction_TimeAverage2 ||
                id == ObjectIds.AggregateFunction_Total2 ||
                id == ObjectIds.AggregateFunction_Minimum2 ||
                id == ObjectIds.AggregateFunction_Maximum2 ||
                id == ObjectIds.AggregateFunction_MinimumActualTime2 ||
                id == ObjectIds.AggregateFunction_MaximumActualTime2 ||
                id == ObjectIds.AggregateFunction_Range2 ||
                id == ObjectIds.AggregateFunction_DeltaBounds ||
                id == ObjectIds.AggregateFunction_DurationInStateZero ||
                id == ObjectIds.AggregateFunction_DurationInStateNonZero)
            {
                return BoundSourcesAreValid(slice.StartTime, false) && BoundSourcesAreValid(slice.EndTime, false);
            }

            return true;
        }

        /// <summary>
        /// True if the raw values a bound at the timestamp is calculated from have a supported data
        /// type: the raw value at the timestamp, or else the value before it and (sloped) the value
        /// after it. Interpolated bounds skip Bad values (§3.1.8); Simple Bounding Values use the
        /// nearest raw values (§3.1.9).
        /// </summary>
        private bool BoundSourcesAreValid(DateTime timestamp, bool interpolated)
        {
            DataValue before = null;
            DataValue after = null;

            for (LinkedListNode<DataValue> ii = m_values.First; ii != null; ii = ii.Next)
            {
                int comparison = CompareTimestamps(timestamp, ii);

                if (comparison == 0)
                {
                    if (StatusCode.IsNotBad(ii.Value.StatusCode))
                    {
                        return IsValidDataType(ii.Value);
                    }

                    // a Bad raw value on the timestamp is the Simple Bounding Value itself (Bad_NoData).
                    if (!interpolated)
                    {
                        return true;
                    }

                    continue;
                }

                if (comparison > 0)
                {
                    if (!interpolated || StatusCode.IsNotBad(ii.Value.StatusCode))
                    {
                        before = ii.Value;
                    }

                    continue;
                }

                if (!interpolated || StatusCode.IsNotBad(ii.Value.StatusCode))
                {
                    after = ii.Value;
                    break;
                }
            }

            return IsValidDataType(before) && (Stepped || IsValidDataType(after));
        }

        /// <summary>
        /// True if the value can be used by the aggregate: it is Bad or empty, or it is a scalar of a
        /// data type the aggregate supports (Part 13 Table 50).
        /// </summary>
        private bool IsValidDataType(DataValue value)
        {
            if (value == null || StatusCode.IsBad(value.StatusCode) || value.WrappedValue.IsNull)
            {
                return true;
            }

            TypeInfo typeInfo = value.WrappedValue.TypeInfo;

            if (typeInfo == null)
            {
                return true;
            }

            // arrays are not supported (§5.4.2.3).
            if (typeInfo.ValueRank >= ValueRanks.OneOrMoreDimensions)
            {
                return false;
            }

            return AggregateManager.IsValidBuiltInType(typeInfo.BuiltInType, m_validDataTypes);
        }

        /// <summary>
        /// True if PercentDataGood and PercentDataBad can be used (Part 13 §4.2.1.2): neither exceeds
        /// 100 and PercentDataGood is at least 100 - PercentDataBad.
        /// </summary>
        private static bool IsValidConfiguration(AggregateConfiguration configuration)
        {
            return configuration.PercentDataGood <= 100 &&
                configuration.PercentDataBad <= 100 &&
                configuration.PercentDataGood + configuration.PercentDataBad >= 100;
        }

        /// <summary>
        /// Interpolates a value at the timestamp.
        /// </summary>
        /// <param name="timestamp">The timestamp.</param>
        /// <param name="reference">The timeslice that contains the timestamp.</param>
        /// <returns>The interpolated value.</returns>
        protected DataValue Interpolate(DateTime timestamp, TimeSlice reference)
        {
            // a Bad raw value on the timestamp is skipped (Part 13 Table 52); an Uncertain one is
            // the bound, since TreatUncertainAsBad does not apply to bounds (Mantis 11462).
            bool skippedValueAtTimestamp = false;

            for (LinkedListNode<DataValue> ii = m_values.First; ii != null; ii = ii.Next)
            {
                int comparison = CompareTimestamps(timestamp, ii);

                if (comparison == 0)
                {
                    if (IsBoundCandidate(ii.Value))
                    {
                        return ii.Value;
                    }

                    skippedValueAtTimestamp = true;
                    break;
                }

                if (comparison < 0)
                {
                    break;
                }
            }

            var slice = new TimeSlice { StartTime = timestamp, EndTime = timestamp };
            UpdateSlice(slice);

            // check for value at the timestamp.
            if (slice.Begin != null && IsBoundCandidate(slice.Begin.Value))
            {
                return slice.Begin.Value;
            }

            bool stepped = Stepped;

            DataValue dataValue;
            // check if the required bounds are available.
            if (!Stepped)
            {
                // check if sloped interpolation is possible.
                if (slice.EarlyBound != null && slice.LateBound != null)
                {
                    dataValue = SlopedInterpolate(
                        timestamp,
                        slice.EarlyBound.Value,
                        slice.LateBound.Value,
                        CastBoundsToSourceType);

                    if (!ReferenceEquals(slice.EarlyBound.Next, slice.LateBound))
                    {
                        dataValue.StatusCode = dataValue.StatusCode
                            .SetCodeBits(StatusCodes.UncertainDataSubNormal);
                    }

                    return dataValue;
                }

                // check if extrapolation is possible.
                if (slice.EarlyBound != null)
                {
                    if (Configuration.UseSlopedExtrapolation &&
                        slice.SecondEarlyBound != null)
                    {
                        UsingExtrapolation = true;
                        dataValue = SlopedInterpolate(
                            timestamp,
                            slice.SecondEarlyBound.Value,
                            slice.EarlyBound.Value,
                            CastBoundsToSourceType);
                        dataValue.StatusCode = dataValue.StatusCode
                            .SetCodeBits(StatusCodes.UncertainDataSubNormal);
                        return dataValue;
                    }

                    // do stepped extrapolation.
                    stepped = true;
                }
            }

            // do stepped interpolation.
            if (stepped && slice.EarlyBound != null)
            {
                dataValue = SteppedInterpolate(timestamp, slice.EarlyBound.Value);

                if (slice.EarlyBound.Next == null ||
                    CompareTimestamps(timestamp, slice.EarlyBound.Next) > 0)
                {
                    UsingExtrapolation = true;
                    dataValue.StatusCode = dataValue.StatusCode
                        .SetCodeBits(StatusCodes.UncertainDataSubNormal);
                }

                // Part 13 Table 52: Uncertain_DataSubNormal if a value was skipped to calculate the
                // bound, which includes a value on the timestamp itself.
                if (skippedValueAtTimestamp && StatusCode.IsNotBad(dataValue.StatusCode))
                {
                    dataValue.StatusCode = dataValue.StatusCode
                        .SetCodeBits(StatusCodes.UncertainDataSubNormal);
                }

                return dataValue;
            }

            // no data found.
            return GetNoDataValue(timestamp);
        }

        /// <summary>
        /// Calculate the value at the timestamp using slopped interpolation.
        /// </summary>
        public static DataValue SteppedInterpolate(DateTime timestamp, DataValue earlyBound)
        {
            // can't interpolate if no start bound.
            if (StatusCode.IsBad(earlyBound.StatusCode))
            {
                return new DataValue(Variant.Null, StatusCodes.BadNoData, timestamp, timestamp);
            }

            var dataValue = new DataValue
            {
                WrappedValue = earlyBound.WrappedValue,
                SourceTimestamp = timestamp,
                ServerTimestamp = timestamp,
                StatusCode = StatusCodes.Good
            };

            // update status code.
            if (StatusCode.IsBad(earlyBound.StatusCode))
            {
                dataValue.StatusCode = StatusCodes.BadNoData;
            }

            // update status code.
            if (StatusCode.IsNotGood(earlyBound.StatusCode))
            {
                dataValue.StatusCode = StatusCodes.UncertainDataSubNormal;
            }

            dataValue.StatusCode = dataValue.StatusCode
                .SetAggregateBits(AggregateBits.Interpolated);
            return dataValue;
        }

        /// <summary>
        /// Calculate the value at the timestamp using sloped interpolation. The interpolated value
        /// is converted back to the variable's original type with TypeInfo.Cast (as in 1.5.378).
        /// A missing bound or a conversion error gives Bad_TypeMismatch.
        /// </summary>
        public static DataValue SlopedInterpolate(
            DateTime timestamp,
            DataValue earlyBound,
            DataValue lateBound)
        {
            try
            {
                DataValue dataValue = SlopedInterpolate(timestamp, earlyBound, lateBound, false);

                // only an interpolated value is a Double that is converted back.
                if (StatusCode.IsNotBad(dataValue.StatusCode) && StatusCode.IsNotBad(lateBound.StatusCode))
                {
                    dataValue.WrappedValue = new Variant(
                        TypeInfo.Cast(dataValue.Value, TypeInfo.Scalars.Double, earlyBound.WrappedValue.TypeInfo.BuiltInType),
                        earlyBound.WrappedValue.TypeInfo);
                }

                return dataValue;
            }
            // exception occurs on a missing bound and on data conversion errors.
            catch (Exception)
            {
                return new DataValue(
                    Variant.Null,
                    StatusCodes.BadTypeMismatch,
                    timestamp,
                    timestamp);
            }
        }

        /// <summary>
        /// Calculate the value at the timestamp using slopped interpolation.
        /// <param name="timestamp"></param>
        /// <param name="earlyBound"></param>
        /// <param name="lateBound"></param>
        /// <param name="useOriginalType">
        /// If true, the interpolated value is converted back to the variable's original type.
        /// useOriginalType=true rounds the double result (e.g. Int)
        /// useOriginalType=false it stays a raw Double
        /// </param>
        /// </summary>
        public static DataValue SlopedInterpolate(
            DateTime timestamp,
            DataValue earlyBound,
            DataValue lateBound,
            bool useOriginalType)
        {
            if (earlyBound is null)
            {
                throw new ArgumentNullException(nameof(earlyBound));
            }

            try
            {
                // can't interpolate if no start bound.
                if (StatusCode.IsBad(earlyBound.StatusCode))
                {
                    return new DataValue(Variant.Null, StatusCodes.BadNoData, timestamp, timestamp);
                }

                // revert to stepped if no end bound.
                if (StatusCode.IsBad(lateBound.StatusCode))
                {
                    DataValue dataValue2 = SteppedInterpolate(timestamp, earlyBound);

                    if (StatusCode.IsNotBad(dataValue2.StatusCode))
                    {
                        dataValue2.StatusCode = dataValue2.StatusCode
                            .SetCodeBits(StatusCodes.UncertainDataSubNormal);
                    }

                    return dataValue2;
                }

                // convert to doubles.
                double earlyValue = CastToDouble(earlyBound);
                double lateValue = CastToDouble(lateBound);

                // do interpolation.
                double range = (lateBound.SourceTimestamp - earlyBound.SourceTimestamp)
                    .TotalMilliseconds;
                double slope = (lateValue - earlyValue) / range;
                double calculatedValue =
                    (slope * (timestamp - earlyBound.SourceTimestamp).TotalMilliseconds) +
                    earlyValue;

                // convert back to original type.
                var dataValue = new DataValue
                {
                    WrappedValue = useOriginalType
                        ? CastToOriginalType(calculatedValue, earlyBound)
                        : new Variant(calculatedValue, TypeInfo.Scalars.Double),
                    SourceTimestamp = timestamp,
                    ServerTimestamp = timestamp,
                    StatusCode = StatusCodes.Good
                };

                // update status code.
                if (StatusCode.IsNotGood(earlyBound.StatusCode) ||
                    StatusCode.IsNotGood(lateBound.StatusCode))
                {
                    dataValue.StatusCode = StatusCodes.UncertainDataSubNormal;
                }

                dataValue.StatusCode = dataValue.StatusCode
                    .SetAggregateBits(AggregateBits.Interpolated);

                return dataValue;
            }
            // exception occurs on data conversion errors.
            catch (Exception)
            {
                return new DataValue(
                    Variant.Null,
                    StatusCodes.BadTypeMismatch,
                    timestamp,
                    timestamp);
            }
        }

        /// <summary>
        /// Converts the value to a double for use in calculations (throws exceptions if conversion fails).
        /// </summary>
        protected static double CastToDouble(DataValue value)
        {
            return (double)TypeInfo.Cast(
                value.Value,
                value.WrappedValue.TypeInfo,
                BuiltInType.Double);
        }

        /// <summary>
        /// Converts the value back to its original type (throws exceptions if conversion fails).
        /// </summary>
        protected static Variant CastToOriginalType(double value, DataValue original)
        {
            object castValue = TypeInfo.Cast(
                value,
                TypeInfo.Scalars.Double,
                original.WrappedValue.TypeInfo.BuiltInType);
            return new Variant(castValue, original.WrappedValue.TypeInfo);
        }

        /// <summary>
        /// Converts a calculated value to the source data type and back, the way bounds of an
        /// integer variable are converted (TypeInfo.Cast rounds to the nearest value, ties to
        /// even). Double, non-numeric and unknown source types leave the value unchanged.
        /// </summary>
        /// <exception cref="OverflowException">The value does not fit into the source type.</exception>
        internal static double RoundToSourceType(double value, TypeInfo sourceType)
        {
            if (sourceType == null ||
                sourceType.ValueRank >= 0 ||
                sourceType.BuiltInType == BuiltInType.Double ||
                !TypeInfo.IsNumericType(sourceType.BuiltInType))
            {
                return value;
            }

            object castValue = TypeInfo.Cast(value, TypeInfo.Scalars.Double, sourceType.BuiltInType);
            return Convert.ToDouble(castValue, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns the simple bound for the timestamp, converted to the variable's original type.
        /// </summary>
        protected DataValue GetSimpleBound(DateTime timestamp, TimeSlice slice)
        {
            return GetSimpleBound(timestamp, slice, true);
        }

        /// <summary>
        /// Returns the simple bound for the timestamp.
        /// </summary>
        protected DataValue GetSimpleBound(DateTime timestamp, TimeSlice slice, bool useOriginalType)
        {
            // choose the start point
            LinkedListNode<DataValue> start = slice.EarlyBound ?? m_values.First;

            // look for a raw value at or immediately before the timestamp.
            LinkedListNode<DataValue> startBound = start;

            for (LinkedListNode<DataValue> ii = start; ii != null; ii = ii.Next)
            {
                // check for an exact match.
                if (CompareTimestamps(timestamp, ii) == 0)
                {
                    return new DataValue(ii.Value);
                }

                // looking for an end bound.
                if (CompareTimestamps(timestamp, ii) < 0)
                {
                    // only can find an end bound.
                    if (ii.Previous == null)
                    {
                        return GetNoDataValue(timestamp);
                    }

                    startBound = ii.Previous;
                    break;
                }

                // update start bound.
                startBound = ii;
            }

            // check if no data found or if start bound is bad. Part 13 §3.1.9: only a Bad raw
            // value before the timestamp gives Bad_NoData, an Uncertain one gives an Uncertain
            // bound; TreatUncertainAsBad does not apply to bounds (Mantis 11462).
            if (startBound == null || !IsBoundCandidate(startBound.Value))
            {
                return GetNoDataValue(timestamp);
            }

            // look for an end bound.
            bool revertToStepped = false;
            LinkedListNode<DataValue> endBound = startBound.Next;

            if (!Stepped)
            {
                if (endBound != null)
                {
                    // do sloped interpolation if two good bounds exist.
                    if (IsBoundCandidate(endBound.Value))
                    {
                        return SlopedInterpolate(timestamp, startBound.Value, endBound.Value, useOriginalType);
                    }
                }

                // have to use stepped because end bound is not good.
                revertToStepped = true;
            }

            // check if end of data.
            if (startBound.Next == null)
            {
                return GetNoDataValue(timestamp);
            }

            // do stepped interpolation for all other cases.
            DataValue value = SteppedInterpolate(timestamp, startBound.Value);

            // need to make it uncertain if interpolation was required but not used.
            if (StatusCode.IsGood(value.StatusCode) && revertToStepped)
            {
                value.StatusCode = StatusCodes.UncertainDataSubNormal;
                value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Interpolated);
            }

            return value;
        }

        /// <summary>
        /// Finds the raw value at or immediately before the timestamp.
        /// </summary>
        /// <param name="timestamp">The timestamp.</param>
        /// <param name="value">The raw value, null if none was found.</param>
        /// <returns>True if a raw value at or before the timestamp exists.</returns>
        protected bool TryGetRawValueAtOrBefore(DateTime timestamp, out DataValue value)
        {
            value = null;

            for (LinkedListNode<DataValue> ii = m_values.First; ii != null; ii = ii.Next)
            {
                if (CompareTimestamps(timestamp, ii) < 0)
                {
                    break;
                }

                value = ii.Value;
            }

            return value != null;
        }

        /// <summary>
        /// Returns the values in the list with simple bounds.
        /// </summary>
        protected List<DataValue> GetValuesWithSimpleBounds(TimeSlice slice)
        {
            // check if slice is beyond end of available data.
            if (CompareTimestamps(slice.StartTime, m_values.Last) > 0 ||
                CompareTimestamps(slice.EndTime, m_values.First) < 0)
            {
                return null;
            }

            var values = new List<DataValue>();

            // add the start point.
            DataValue startBound = GetSimpleBound(slice.StartTime, slice, useOriginalType: false);

            if (startBound != null)
            {
                values.Add(startBound);
            }

            // initialize slice from value list.
            for (LinkedListNode<DataValue> ii = slice.Begin; ii != null; ii = ii.Next)
            {
                if (CompareTimestamps(slice.EndTime, ii) <= 0)
                {
                    break;
                }

                if (CompareTimestamps(slice.StartTime, ii) < 0)
                {
                    values.Add(ii.Value);
                }
            }

            // add the end point.
            DataValue endBound = GetSimpleBound(slice.EndTime, slice, useOriginalType: false);

            if (endBound != null)
            {
                values.Add(endBound);
            }

            return values;
        }

        /// <summary>
        /// Returns the values between the start time and the end time for the slice.
        /// </summary>
        protected List<DataValue> GetValues(TimeSlice slice)
        {
            // check if slice is beyond end of available data.
            if (CompareTimestamps(slice.StartTime, m_values.Last) > 0 ||
                CompareTimestamps(slice.EndTime, m_values.First) < 0)
            {
                return null;
            }

            var values = new List<DataValue>();

            // initialize slice from value list.
            for (LinkedListNode<DataValue> ii = slice.Begin; ii != null; ii = ii.Next)
            {
                if (TimeFlowsBackward)
                {
                    if (CompareTimestamps(slice.EndTime, ii) < 0)
                    {
                        break;
                    }

                    if (CompareTimestamps(slice.StartTime, ii) < 0)
                    {
                        values.Add(ii.Value);
                    }
                }
                else
                {
                    if (CompareTimestamps(slice.EndTime, ii) <= 0)
                    {
                        break;
                    }

                    if (CompareTimestamps(slice.StartTime, ii) <= 0)
                    {
                        values.Add(ii.Value);
                    }
                }
            }

            return values;
        }

        /// <summary>
        /// Returns the values in the list with interpolated bounds.
        /// </summary>
        protected List<DataValue> GetValuesWithInterpolatedBounds(TimeSlice slice)
        {
            // check if slice is before the available data.
            if (CompareTimestamps(slice.EndTime, m_values.First) < 0)
            {
                return null;
            }

            var values = new List<DataValue>();

            // add the start point.
            DataValue startBound = Interpolate(slice.StartTime, slice);

            if (startBound != null)
            {
                values.Add(startBound);
            }

            // initialize slice from value list.
            for (LinkedListNode<DataValue> ii = slice.Begin; ii != null; ii = ii.Next)
            {
                if (CompareTimestamps(slice.EndTime, ii) <= 0)
                {
                    break;
                }

                if (CompareTimestamps(slice.StartTime, ii) <= 0)
                {
                    values.Add(ii.Value);
                }
            }

            // add the end point.
            DataValue endBound = Interpolate(slice.EndTime, slice);

            if (endBound != null)
            {
                values.Add(endBound);
            }

            return values;
        }

        /// <summary>
        /// A subset of a slice bounded by two raw data points.
        /// </summary>
        protected class SubRegion
        {
            /// <summary>
            /// The value at the start of the region.
            /// </summary>
            public double StartValue { get; set; }

            /// <summary>
            /// The value at the end of the region.
            /// </summary>
            public double EndValue { get; set; }

            /// <summary>
            /// The timestamp at the start of the region.
            /// </summary>
            public DateTime StartTime;

            /// <summary>
            /// The length of the region.
            /// </summary>
            public double Duration { get; set; }

            /// <summary>
            /// The status for the region.
            /// </summary>
            public StatusCode StatusCode;

            /// <summary>
            /// The data point at the start of the region.
            /// </summary>
            public DataValue DataPoint;
        }

        /// <summary>
        /// Returns the values in the list with simple bounds.
        /// </summary>
        protected List<SubRegion> GetRegionsInValueSet(
            List<DataValue> values,
            bool ignoreBadData,
            bool useSteppedCalculations)
        {
            return GetRegionsInValueSet(values, ignoreBadData, useSteppedCalculations, false);
        }

        /// <summary>
        /// Returns the regions in the value set. With <paramref name="omitUncertain"/> an Uncertain
        /// value is treated as Bad whatever TreatUncertainAsBad is, for aggregates whose definition
        /// leaves out all non-Good data (TimeAverage2 and Total2, Part 13 §5.4.3.7, Tables 55, 57).
        /// </summary>
        private protected List<SubRegion> GetRegionsInValueSet(
            List<DataValue> values,
            bool ignoreBadData,
            bool useSteppedCalculations,
            bool omitUncertain)
        {
            bool IsUsable(DataValue value)
            {
                return omitUncertain ? value != null && StatusCode.IsGood(value.StatusCode) : IsGood(value);
            }

            // nothing to do if no data.
            if (values == null)
            {
                return null;
            }

            SubRegion currentRegion = null;
            var regions = new List<SubRegion>();

            for (int ii = 0; ii < values.Count; ii++)
            {
                double currentValue = 0;
                DateTime currentTime = values[ii].SourceTimestamp;
                StatusCode currentStatus = values[ii].StatusCode;

                // convert to doubles to facilitate numeric calculations.
                if (StatusCode.IsNotBad(currentStatus))
                {
                    try
                    {
                        currentValue = CastToDouble(values[ii]);
                    }
                    catch (Exception)
                    {
                        currentStatus = StatusCodes.BadTypeMismatch;
                    }
                }
                else
                {
                    // use the previous value if end of region is bad.
                    if (currentRegion != null)
                    {
                        currentValue = currentRegion.StartValue;
                    }
                }

                // some aggregates ignore bad data so remove them from the set.
                if (ignoreBadData)
                {
                    // always keep the first region.
                    if (currentRegion != null)
                    {
                        if (!IsUsable(values[ii]))
                        {
                            // set the status to sub normal if bad end data ignored.
                            if (StatusCode.IsNotBad(currentRegion.StatusCode))
                            {
                                currentRegion.StatusCode = StatusCodes.UncertainDataSubNormal;
                            }

                            // skip everything but the endpoint.
                            if (ii < values.Count - 1)
                            {
                                continue;
                            }
                        }
                        else if (!useSteppedCalculations &&
                            StatusCode.IsNotGood(values[ii].StatusCode))
                        {
                            currentRegion.StatusCode = StatusCodes.UncertainDataSubNormal;
                        }
                    }
                }

                if (currentRegion != null)
                {
                    // if using stepped calculations the end value is not used.
                    if (useSteppedCalculations)
                    {
                        currentRegion.EndValue = currentRegion.StartValue;
                    }
                    // using interpolated calculations means the end affects the status of the current region.
                    else if (IsUsable(values[ii]))
                    {
                        // handle case with uncertain end point.
                        if (StatusCode.IsNotGood(values[ii].StatusCode) &&
                            StatusCode.IsNotBad(currentRegion.StatusCode))
                        {
                            currentRegion.StatusCode = StatusCodes.UncertainDataSubNormal;
                        }

                        currentRegion.EndValue = currentValue;
                    }
                    else
                    {
                        if (StatusCode.IsNotBad(currentRegion.StatusCode))
                        {
                            currentRegion.StatusCode = StatusCodes.UncertainDataSubNormal;
                        }

                        if (ignoreBadData && StatusCode.IsNotBad(currentStatus))
                        {
                            currentRegion.EndValue = currentValue;
                        }
                    }

                    // calculate region span. A region that starts at the last raw value runs to the
                    // end bound like any other region (Part 13 §5.4.3.31, §5.4.3.32: each region starts
                    // with a data point; Tables 55 and 57: a missing end bound is Bad_NoData).
                    currentRegion.Duration = (currentTime - currentRegion.StartTime)
                        .TotalMilliseconds;

                    regions.Add(currentRegion);
                }

                // set uncertain status to bad if treat uncertain as bad is true (Part 13 §5.4.3.2.1:
                // Uncertain regions are included as Bad regions). Without ignoreBadData (Simple
                // Bounding Values) this applies to the first region too: an Uncertain start bound is
                // treated as any other Uncertain value in the interval (Table 55). With Interpolated
                // Bounding Values an Uncertain start bound is used and makes the result Uncertain.
                if (StatusCode.IsUncertain(currentStatus) &&
                    !IsUsable(values[ii]) &&
                    (currentRegion != null || !ignoreBadData))
                {
                    currentStatus = StatusCodes.BadNoData;
                }

                // start a new region.
                currentRegion = new SubRegion
                {
                    StartValue = currentValue,
                    EndValue = currentValue,
                    StartTime = currentTime,
                    StatusCode = currentStatus,
                    DataPoint = values[ii]
                };
            }

            return regions;
        }

        /// <summary>
        /// Calculates the value based status code for the slice. A Good value counts as Good and a
        /// Bad value as Bad. An Uncertain value counts as Bad with TreatUncertainAsBad = True
        /// (Part 13 §4.2.1.2, PercentDataBad) and otherwise as neither Good nor Bad, since the value
        /// is still treated as Uncertain when the result status is calculated (§4.2.1.2; Mantis 7975
        /// and 11495).
        /// </summary>
        /// <param name="slice">The time slice.</param>
        /// <param name="values">The values in the slice.</param>
        /// <param name="statusCode">The status code to update.</param>
        protected StatusCode GetValueBasedStatusCode(
            TimeSlice slice,
            List<DataValue> values,
            StatusCode statusCode)
        {
            // compute the total good/bad.
            double badCount = 0;
            double goodCount = 0;
            double totalCount = 0;

            for (int ii = 0; ii < values.Count; ii++)
            {
                totalCount++;

                bool isUncertain = StatusCode.IsUncertain(values[ii].StatusCode);

                if (StatusCode.IsGood(values[ii].StatusCode))
                {
                    goodCount++;
                }
                else if (!isUncertain || Configuration.TreatUncertainAsBad)
                {
                    badCount++;
                }
            }

            double percentGood = totalCount > 0 ? (goodCount / totalCount * 100.0) : 100.0;
            double percentBad = totalCount > 0 ? (badCount / totalCount * 100.0) : 0.0;

            // indicates whether the quality decision is based only on the PercentDataGood
            bool useGoodOnly = Configuration.PercentDataGood + Configuration.PercentDataBad == 100;

            if (percentGood >= Configuration.PercentDataGood)
            {
                statusCode = statusCode.SetCodeBits(StatusCodes.Good);
            }
            else if (!useGoodOnly && percentBad >= Configuration.PercentDataBad)
            {
                statusCode = statusCode.SetCodeBits(StatusCodes.Bad);
            }
            else
            {
                statusCode = statusCode.SetCodeBits(StatusCodes.UncertainDataSubNormal);
            }

            return statusCode;
        }

        /// <summary>
        /// Calculates the status code for the slice
        /// </summary>
        protected StatusCode GetTimeBasedStatusCode(
            TimeSlice slice,
            List<DataValue> values,
            StatusCode defaultCode)
        {
            // get the regions in the slice.
            List<SubRegion> regions = GetRegionsInValueSet(values, false, Stepped);

            if (regions == null || regions.Count == 0)
            {
                return StatusCodes.BadNoData;
            }

            return GetTimeBasedStatusCode(regions, defaultCode);
        }

        /// <summary>
        /// Calculates the status code for the slice
        /// </summary>
        protected StatusCode GetTimeBasedStatusCode(List<SubRegion> regions, StatusCode statusCode)
        {
            // check for empty set.
            if (regions == null || regions.Count == 0)
            {
                return StatusCodes.BadNoData;
            }

            // compute the total good/bad/uncertain.
            double badDuration = 0;
            double goodDuration = 0;
            double totalDuration = 0;

            foreach (SubRegion region in regions)
            {
                totalDuration += region.Duration;

                // Part 13 §5.4.3.2.1: Uncertain regions count as Bad when TreatUncertainAsBad
                // is true and as Good otherwise.
                if (StatusCode.IsBad(region.StatusCode) ||
                    (Configuration.TreatUncertainAsBad &&
                        StatusCode.IsUncertain(region.StatusCode)))
                {
                    badDuration += region.Duration;
                }
                else
                {
                    goodDuration += region.Duration;
                }
            }

            if (totalDuration == 0 ||
                goodDuration / totalDuration * 100 >= Configuration.PercentDataGood)
            {
                // good if the good duration is greater than or equal to the configured threshold.
                statusCode = statusCode.SetCodeBits(StatusCodes.Good);
            }
            else if (Configuration.PercentDataGood + Configuration.PercentDataBad != 100 &&
                badDuration / totalDuration * 100 >= Configuration.PercentDataBad)
            {
                // bad if the bad duration is greater than or equal to the configured threshold.
                // Part 13 §4.2.1.2: "PercentDataGood ≥ (100 – PercentDataBad). If they are equal the
                // result of the PercentDataGood calculation is used", so with PercentDataGood =
                // 100 - PercentDataBad an interval below PercentDataGood is Uncertain, not Bad (as
                // Annex A Historian3 expects).
                // Keep the aggregate bits like the Good and Uncertain results.
                statusCode = statusCode.SetCodeBits(StatusCodes.Bad);
            }
            else
            {
                // uncertain if did not meet the Good or Bad requirements
                statusCode = statusCode.SetCodeBits(StatusCodes.UncertainDataSubNormal);
            }

            // always calculated.
            return statusCode;
        }

        private readonly ILogger m_logger;
        private readonly LinkedList<DataValue> m_values;
        private readonly List<DateTime> m_annotations = [];
        private DateTime m_startOfData;
        private DateTime m_endOfData;
        private readonly bool m_stepped;
        private readonly bool m_numericOnly;
        private readonly AggregateManager.AggregateDataTypes m_validDataTypes;
        private bool m_nonNumericData;
    }
}
