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

            // if the variable type is not explicitly set, determine it from the current value
            if (VariableType == BuiltInType.Null && value.WrappedValue.TypeInfo != null)
            {
                var bt = value.WrappedValue.TypeInfo.BuiltInType;

                // allows only valid buil-in types
                if (bt != BuiltInType.Null)
                {
                    VariableType = bt;
                }
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

            // skip values that have no source timestamp.
            if (value.SourceTimestamp != default)
            {
                RawPoints.Add((value.SourceTimestamp, StatusCode.IsBad(value.StatusCode)));
            }

            return true;
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

            try
            {
                // no raw data at all (only placeholders were queued).
                value = m_values.First == null
                    ? GetNoDataValue(CurrentSlice)
                    : ComputeValue(CurrentSlice);
            }
            catch (OverflowException)
            {
                value = new DataValue(Variant.Null, StatusCodes.BadTypeMismatch, GetTimestamp(CurrentSlice), GetTimestamp(CurrentSlice));
            }
            catch (InvalidCastException)
            {
                value = new DataValue(Variant.Null, StatusCodes.BadTypeMismatch, GetTimestamp(CurrentSlice), GetTimestamp(CurrentSlice));
            }

            // check if overlapping the start or end of data (Part 13 §5.3.3.2): the interval that
            // contains the start of data, and the interval that contains the end of data unless
            // the value was extrapolated. Both checks use the chronological interval, so they
            // apply in either time direction.
            if (SetPartialBit && m_startOfData != DateTime.MinValue)
            {
                bool containsStartOfData = m_startOfData > earlyTime && m_startOfData < lateTime;
                bool containsEndOfData = !UsingExtrapolation && m_endOfData >= earlyTime && m_endOfData < lateTime;

                if (containsStartOfData || containsEndOfData)
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
        /// always use the value of the prior raw value.
        /// </summary>
        protected bool Stepped => m_stepped || m_nonNumericData;

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
        /// Variable data type (Boolean, String, Double, etc.), captured once from the first real data point.
        /// Remains unchanged across all slices, unlike values[0].TypeInfo, which may vary per slice.
        /// </summary>
        protected BuiltInType VariableType { get; private set; } = BuiltInType.Null;

        /// <summary>
        /// Raw point timestamps and real Bad flags, stored in the order they arrive.
        /// Used to determine the typical sampling interval and find the next point for end-of-data handling.
        /// </summary>
        protected List<(DateTime Timestamp, bool IsRealBad)> RawPoints { get; } = new List<(DateTime, bool)>();

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
        /// Checks if a raw value can form a bounding value. Part 13 §3.1.8 and §3.1.9 build
        /// bounding values from non-Bad raw values; TreatUncertainAsBad does not apply to them
        /// (Mantis 11462), an Uncertain raw value makes the bound Uncertain_DataSubNormal instead.
        /// </summary>
        /// <param name="value">The raw value to test.</param>
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

            // initialize slice from value list.
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
                    if (slice.End == null)
                    {
                        slice.End = ii;
                    }

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
                    if (slice.Begin == null)
                    {
                        slice.Begin = ii;
                    }

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
        /// Interpolates a value at the timestamp.
        /// </summary>
        /// <param name="timestamp">The timestamp.</param>
        /// <param name="reference">The timeslice that contains the timestamp.</param>
        /// <returns>The interpolated value.</returns>
        protected DataValue Interpolate(DateTime timestamp, TimeSlice reference)
        {
            for (LinkedListNode<DataValue> ii = m_values.First; ii != null; ii = ii.Next)
            {
                int comparison = CompareTimestamps(timestamp, ii);

                if (comparison == 0)
                {
                    if (IsBoundCandidate(ii.Value))
                    {
                        return ii.Value;
                    }

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
                    dataValue = SlopedInterpolateWithIntegerTruncation(
                        timestamp,
                        slice.EarlyBound.Value,
                        slice.LateBound.Value);

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

                // Part 13 §3.1.8, SteppedInterpolation: "Uncertain_DataSubNormal if any Bad values
                // exist between the before value and the timestamp". A Bad raw value exactly on the
                // timestamp is not between them, so the stepped bound stays Good (CTT 1.05.513
                // Interpolative 005-05 on the Stepped Historian3 at 12:00:42).

                return dataValue;
            }

            // no data found.
            return GetNoDataValue(timestamp);
        }

        /// <summary>
        /// Performs sloped interpolation for the Interpolative aggregate.
        /// Recalculates only the value, keeping the status and timestamps from <see cref="SlopedInterpolate(DateTime, DataValue, DataValue, bool)"/>.
        /// </summary>
        private DataValue SlopedInterpolateWithIntegerTruncation(DateTime timestamp, DataValue startBound, DataValue endBound)
        {
            DataValue result = SlopedInterpolate(timestamp, startBound, endBound, CastBoundsToSourceType);

            BuiltInType builtInType = result.WrappedValue.TypeInfo?.BuiltInType ?? BuiltInType.Null;

            bool isIntegerType = TypeInfo.IsNumericType(builtInType) &&
                                 builtInType != BuiltInType.Float &&
                                 builtInType != BuiltInType.Double;

            if (AggregateId != ObjectIds.AggregateFunction_Interpolative || StatusCode.IsBad(result.StatusCode) || !isIntegerType)
            {
                return result;
            }

            // arrange bounds chronologically so both time directions give the same result.
            if (startBound.SourceTimestamp > endBound.SourceTimestamp)
            {
                (startBound, endBound) = (endBound, startBound);
            }

            double startValue = CastToDouble(startBound);
            double endValue = CastToDouble(endBound);
            double intervalMs = (endBound.SourceTimestamp - startBound.SourceTimestamp).TotalMilliseconds;
            double offsetMs = (timestamp - startBound.SourceTimestamp).TotalMilliseconds;

            double interpolatedValue = startValue + (endValue - startValue) * offsetMs / intervalMs;

            const double truncationEpsilon = 1e-9; // e.g. epsilon keeps 388.9999999 from dropping to 388

            // truncate integer results with a small adjustment for floating-point errors.
            double truncatedValue = Math.Truncate(interpolatedValue + Math.Sign(interpolatedValue) * truncationEpsilon);

            result.Value = CastToOriginalType(interpolatedValue, startBound);

            return result;
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
        /// is converted back to the variable's original type.
        /// </summary>
        public static DataValue SlopedInterpolate(
            DateTime timestamp,
            DataValue earlyBound,
            DataValue lateBound)
        {
            return SlopedInterpolate(timestamp, earlyBound, lateBound, true);
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
            BuiltInType targetType = original.WrappedValue.TypeInfo.BuiltInType;

            // For integer types, use half-away-from-zero rounding rather than letting TypeInfo.Cast truncate.
            if (IsIntegerType(targetType))
            {
                value = Math.Round(value, MidpointRounding.AwayFromZero);
            }

            object castValue = TypeInfo.Cast(
                value,
                TypeInfo.Scalars.Double,
                targetType);

            return new Variant(castValue, original.WrappedValue.TypeInfo);
        }

        /// <summary>
        /// True if the value fits into the numeric built-in type without overflow.
        /// </summary>
        protected static bool FitsInType(double value, BuiltInType type)
        {
            switch (type)
            {
                case BuiltInType.SByte: return value >= sbyte.MinValue && value <= sbyte.MaxValue;
                case BuiltInType.Byte: return value >= byte.MinValue && value <= byte.MaxValue;
                case BuiltInType.Int16: return value >= short.MinValue && value <= short.MaxValue;
                case BuiltInType.UInt16: return value >= ushort.MinValue && value <= ushort.MaxValue;
                case BuiltInType.Int32: return value >= int.MinValue && value <= int.MaxValue;
                case BuiltInType.UInt32: return value >= uint.MinValue && value <= uint.MaxValue;
                case BuiltInType.Int64: return value >= long.MinValue && value < long.MaxValue;
                case BuiltInType.UInt64: return value >= ulong.MinValue && value < ulong.MaxValue;
                case BuiltInType.Float: return Math.Abs(value) <= float.MaxValue;
                case BuiltInType.Double: return true;
                default: return false;
            }
        }

        /// <summary>
        /// Rounds a real value for an integer data type as OPC 10000-4 §7.7.3 says: "adding 0.5
        /// and truncating" (2.5 becomes 3).
        /// </summary>
        protected static double RoundHalfUp(double value)
        {
            return Math.Floor(value + 0.5);
        }

        /// <summary>
        /// Checks whether the built-in type is an integer type.
        /// </summary>
        protected static bool IsIntegerType(BuiltInType t)
        {
            switch (t)
            {
                case BuiltInType.Int16:
                case BuiltInType.UInt16:
                case BuiltInType.Int32:
                case BuiltInType.UInt32:
                case BuiltInType.Int64:
                case BuiltInType.UInt64:
                case BuiltInType.SByte:
                case BuiltInType.Byte:
                    return true;
                default:
                    return false;
            }
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
                LinkedListNode<DataValue> endBound2 = startBound.Next;
                if (endBound != null)
                {
                    // do sloped interpolation unless the raw value after the timestamp is Bad
                    // (Part 13 §3.1.9); an Uncertain one makes the bound Uncertain_DataSubNormal.
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
                        // do not set BadTypeMismatch. For non-numeric types, a Good value remains Good.
                        currentValue = 0;
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
                        if (!IsGood(values[ii]))
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
                            // do not automatically mark non-Good values as UncertainDataSubNormal. Keep their numeric value instead.
                            try
                            {
                                currentValue = CastToDouble(values[ii]);
                            }
                            catch (Exception)
                            {
                                currentValue = 0;
                            }
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
                    else if (IsGood(values[ii]))
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

                    // if at end of data then duration is 1 tick.
                    // must be end of data if start of region is good yet end bound is bad.
                    if (!ignoreBadData &&
                        IsGood(currentRegion.DataPoint) &&
                        currentStatus.CodeBits == StatusCodes.BadNoData &&   // the end of data, not a Bad raw value
                        ii == values.Count - 1)
                    {
                        currentRegion.Duration = 1;
                    }
                    // calculate region span.
                    else
                    {
                        currentRegion.Duration = (currentTime - currentRegion.StartTime)
                            .TotalMilliseconds;
                    }

                    regions.Add(currentRegion);
                }

                // set uncertain status to bad if treat uncertain as bad is true (Part 13 §5.4.3.2.1:
                // Uncertain regions are included as Bad regions). Without ignoreBadData (Simple
                // Bounding Values) this applies to the first region too: an Uncertain start bound is
                // treated as any other Uncertain value in the interval (Mantis 11462). With
                // Interpolated Bounding Values an Uncertain start bound is used and makes the result
                // Uncertain.
                if (StatusCode.IsUncertain(currentStatus) &&
                    !IsGood(values[ii]) &&
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
        /// Calculates the value based status code for the slice; with TreatUncertainAsBad = false
        /// an Uncertain value counts as Good (Part 13 §4.2.1.2).
        /// </summary>
        /// <param name="slice">The time slice.</param>
        /// <param name="values">The values in the slice.</param>
        /// <param name="statusCode">The status code to update.</param>
        protected StatusCode GetValueBasedStatusCode(
            TimeSlice slice,
            List<DataValue> values,
            StatusCode statusCode)
        {
            return GetValueBasedStatusCode(slice, values, statusCode, true);
        }

        /// <summary>
        /// Calculates the value based status code for the slice
        /// </summary>
        /// <param name="slice">The time slice.</param>
        /// <param name="values">The values in the slice.</param>
        /// <param name="statusCode">The status code to update.</param>
        /// <param name="uncertainIsGood">
        /// True (default): with TreatUncertainAsBad = false an Uncertain value counts as Good
        /// (Part 13 §4.2.1.2). False: an Uncertain value is never Good; it counts as Bad with
        /// TreatUncertainAsBad = true and as neither Good nor Bad otherwise (Count, Mantis 11495).
        /// </param>
        protected StatusCode GetValueBasedStatusCode(
            TimeSlice slice,
            List<DataValue> values,
            StatusCode statusCode,
            bool uncertainIsGood)
        {
            // compute the total good/bad.
            double badCount = 0;
            double goodCount = 0;
            double totalCount = 0;

            for (int ii = 0; ii < values.Count; ii++)
            {
                totalCount++;

                // Part 13 §4.2.1.2 and §5.4.3.2.1:
                // - Good => goodCount++
                // - Bad => badCount++
                // - Uncertain + TreatUncertainAsBad=True  => Uncertain is considered Bad (badCount++)
                // - Uncertain + TreatUncertainAsBad=False => Uncertain is equivalent to Good (goodCount++)
                // - uncertainIsGood = false: Uncertain + TreatUncertainAsBad=False is neither Good nor Bad
                bool isUncertain = StatusCode.IsUncertain(values[ii].StatusCode);

                if (StatusCode.IsGood(values[ii].StatusCode) ||
                    (uncertainIsGood && !Configuration.TreatUncertainAsBad && isUncertain))
                {
                    goodCount++;
                }
                else if (!isUncertain || Configuration.TreatUncertainAsBad)
                {
                    badCount++;
                }
            }

            // adjust PercentDataGood for cases where the beginning of the slice is not covered by actual data
            double sliceDurationMs = Math.Abs((slice.EndTime - slice.StartTime).TotalMilliseconds);
            double requestDurationMs = Math.Abs((EndTime - StartTime).TotalMilliseconds);

            // the entire request it is not split into multiple slices
            bool isSingleInterval = ProcessingInterval <= 0 || ProcessingInterval >= requestDurationMs;

            double startGapMs = 0;

            // how much of the beginning of the slice has no data at all
            if (!TimeFlowsBackward
                && isSingleInterval
                && sliceDurationMs > 0
                && slice.StartTime < m_startOfData
                && slice.EndTime > m_startOfData)
            {
                startGapMs = (m_startOfData - slice.StartTime).TotalMilliseconds;
            }

            double dataCoverageRatio = sliceDurationMs > 0 ? (sliceDurationMs - startGapMs) / sliceDurationMs : 1.0;

            // reduce PercentGood when part of the interval has no data (dataCoverageRatio)
            double percentGood = (totalCount > 0 ? (goodCount / totalCount * 100.0) : 100.0) * dataCoverageRatio;
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
                // Annex A Historian3 and the CTT 1.05.513 Aggregate tests expect).
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
        private DateTime m_startOfData;
        private DateTime m_endOfData;
        private readonly bool m_stepped;
        private bool m_nonNumericData;
    }
}
