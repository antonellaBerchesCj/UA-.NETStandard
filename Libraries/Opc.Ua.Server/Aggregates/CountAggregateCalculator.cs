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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Calculates the value of an aggregate.
    /// </summary>
    public class CountAggregateCalculator : AggregateCalculator
    {
        /// <summary>
        /// Initializes the aggregate calculator.
        /// </summary>
        /// <param name="aggregateId">The aggregate function to apply.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="processingInterval">The processing interval.</param>
        /// <param name="stepped">Whether to use stepped interpolation.</param>
        /// <param name="configuration">The aggregate configuration.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public CountAggregateCalculator(
            NodeId aggregateId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration,
            ITelemetryContext telemetry)
            : base(aggregateId, startTime, endTime, processingInterval, stepped, configuration, telemetry)
        {
            SetPartialBit = true;
        }

        /// <summary>
        /// Computes the value for the timeslice.
        /// </summary>
        protected override DataValue ComputeValue(TimeSlice slice)
        {
            uint? id = AggregateId.Identifier as uint?;

            if (id == null)
            {
                return base.ComputeValue(slice);
            }
            switch (id.Value)
            {
                case Objects.AggregateFunction_Count:
                    return ComputeCount(slice);
                case Objects.AggregateFunction_AnnotationCount:
                    return ComputeAnnotationCount(slice);
                case Objects.AggregateFunction_DurationInStateZero:
                    return ComputeDurationInState(slice, false);
                case Objects.AggregateFunction_DurationInStateNonZero:
                    return ComputeDurationInState(slice, true);
                case Objects.AggregateFunction_NumberOfTransitions:
                    return ComputeNumberOfTransitions(slice);
                default:
                    return base.ComputeValue(slice);
            }
        }

        /// <summary>
        /// Calculates the Count aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeCount(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

            if (values == null || values.Count == 0)
            {
                var status = CurrentSlice.OutOfDataRange ? StatusCodes.BadNoData : StatusCodes.Good;
                var noData = new DataValue
                {
                    StatusCode = status,
                    SourceTimestamp = GetTimestamp(slice),
                    ServerTimestamp = GetTimestamp(slice),
                    WrappedValue = new Variant(0, TypeInfo.Scalars.Int32)
                };

                if (!StatusCode.IsBad(noData.StatusCode))
                {
                    noData.StatusCode = noData.StatusCode.SetAggregateBits(noData.StatusCode.AggregateBits | AggregateBits.Calculated);
                }
                return noData;
            }

            // count the values.
            int count = 0;

            for (int ii = 0; ii < values.Count; ii++)
            {
                // Mantis 11495: the Count definition (5.4.3.21) overrides the
                // general rule of 4.2.1.2. Only Good values are counted, whatever TreatUncertainAsBad
                // is; non-Good values only affect the StatusCode (value-based, 5.4.3), where an
                // Uncertain value is not Good. A count of 0 means that there is no Good data.
                if (StatusCode.IsGood(values[ii].StatusCode))
                {
                    count++;
                }
            }

            // set the timestamp and status.
            var value = new DataValue
            {
                WrappedValue = new Variant(count, TypeInfo.Scalars.Int32),
                SourceTimestamp = GetTimestamp(slice),
                ServerTimestamp = GetTimestamp(slice)
            };
            value.StatusCode = GetValueBasedStatusCode(slice, values, value.StatusCode);

            if (!StatusCode.IsBad(value.StatusCode))
            {
                // set aggregate bits fon non Bad values
                value.StatusCode = value.StatusCode.SetAggregateBits(value.StatusCode.AggregateBits | AggregateBits.Calculated);
            }
            // return result.
            return value;
        }

        /// <summary>
        /// Calculates the AnnotationCount aggregate for the timeslice: the number of Annotations in
        /// the interval (Part 13 §5.4.3.20), which the history layer passes to
        /// <see cref="AggregateCalculator.QueueAnnotation"/>. Raw values are not counted; they only
        /// decide whether the interval is before the start or after the end of data, which is
        /// Bad_NoData (Table 68).
        /// </summary>
        protected DataValue ComputeAnnotationCount(TimeSlice slice)
        {
            // check for an interval before the start or after the end of data.
            if (IsBeyondData(slice))
            {
                return GetNoDataValue(slice);
            }

            // count the Annotations.
            int count = CountAnnotations(slice);

            // set the timestamp and status.
            var value = new DataValue
            {
                WrappedValue = new Variant(count, TypeInfo.Scalars.Int32),
                SourceTimestamp = GetTimestamp(slice),
                ServerTimestamp = GetTimestamp(slice)
            };
            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            // return result.
            return value;
        }

        /// <summary>
        /// Calculates the DurationInStateZero and DurationInStateNonZero aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeDurationInState(TimeSlice slice, bool isNonZero)
        {
            // get the values in the slice.
            List<DataValue> values = GetValuesWithSimpleBounds(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                // an empty slice is Bad_NoData, unless it overlaps the start of data: then the whole
                // slice counts as Bad_NoData for the status calculation.
                if (!ContainsDataStart(slice))
                {
                    return GetNoDataValue(slice);
                }

                values = new List<DataValue>
                {
                    CreateNoDataValue(slice.StartTime),
                    CreateNoDataValue(slice.EndTime)
                };
            }

            // get the regions.
            List<SubRegion> regions = GetRegionsInValueSet(values, false, true);

            double duration = 0;

            // only Good regions count for the duration.
            for (int ii = 0; ii < regions.Count; ii++)
            {
                if (StatusCode.IsNotGood(regions[ii].StatusCode))
                {
                    continue;
                }

                if (isNonZero ? regions[ii].StartValue != 0 : regions[ii].StartValue == 0)
                {
                    duration += regions[ii].Duration;
                }
            }

            // Part 13 §5.4.3.2.2: with SlopedInterpolation (Stepped = False) a region that ends in a Bad
            // or Uncertain value is Uncertain. This affects the status only; the duration is counted
            // above. GetTimeBasedStatusCode then applies TreatUncertainAsBad (§5.4.3.2.1).
            if (!Stepped)
            {
                // index the values by timestamp once instead of searching the whole list for every region.
                var byTimestamp = new List<(long Ticks, int Index)>(values.Count);

                for (int ii = 0; ii < values.Count; ii++)
                {
                    byTimestamp.Add((values[ii].SourceTimestamp.Ticks, ii));
                }

                byTimestamp.Sort();

                foreach (SubRegion region in regions)
                {
                    if (!StatusCode.IsGood(region.StatusCode))
                    {
                        continue;
                    }

                    DateTime regionEnd = region.StartTime.AddMilliseconds(region.Duration);

                    DataValue endPoint = FindValueAt(values, byTimestamp, regionEnd);

                    if (endPoint != null && StatusCode.IsNotGood(endPoint.StatusCode))
                    {
                        region.StatusCode = StatusCodes.UncertainDataSubNormal;
                    }
                }
            }

            // the duration is rounded to whole milliseconds, half to even (Mantis 11499: Part 13 will
            // say that Duration values are rounded, without fixing the algorithm).
            duration = Math.Round(duration, MidpointRounding.ToEven);

            // set the timestamp and status.
            var value = new DataValue
            {
                WrappedValue = new Variant(duration, TypeInfo.Scalars.Double),
                SourceTimestamp = GetTimestamp(slice),
                ServerTimestamp = GetTimestamp(slice)
            };
            value.StatusCode = GetTimeBasedStatusCode(regions, value.StatusCode);
            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            // return result.
            return value;
        }

        /// <summary>
        /// Returns the first value (in list order) whose source timestamp is less than 0.5 ms from the timestamp,
        /// or NULL. byTimestamp holds the (ticks, index) of the values, sorted by ticks and then by index.
        /// </summary>
        private static DataValue FindValueAt(
            List<DataValue> values,
            List<(long Ticks, int Index)> byTimestamp,
            DateTime timestamp)
        {
            const long kToleranceTicks = TimeSpan.TicksPerMillisecond / 2;
            long ticks = timestamp.Ticks;

            // first entry that is later than (timestamp - 0.5 ms).
            int lo = 0;
            int hi = byTimestamp.Count;

            while (lo < hi)
            {
                int mid = lo + ((hi - lo) / 2);

                if (byTimestamp[mid].Ticks - ticks > -kToleranceTicks)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            int found = -1;

            for (int ii = lo; ii < byTimestamp.Count && byTimestamp[ii].Ticks - ticks < kToleranceTicks; ii++)
            {
                if (found < 0 || byTimestamp[ii].Index < found)
                {
                    found = byTimestamp[ii].Index;
                }
            }

            return found >= 0 ? values[found] : null;
        }

        /// <summary>
        /// Creates a BadNoData value at the specified timestamp with an exact BadNoData status.
        /// </summary>
        private static DataValue CreateNoDataValue(DateTime timestamp)
        {
            return new DataValue
            {
                StatusCode = StatusCodes.BadNoData,   // must be exactly BadNoData to match the check in GetRegionsInValueSet
                SourceTimestamp = timestamp,
                ServerTimestamp = timestamp
            };
        }

        /// <summary>
        /// Calculates the Count aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeNumberOfTransitions(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

            // check for empty slice.
            if (values == null)
            {
                return GetNoDataValue(slice);
            }

            // Part 13 §5.4.3.24 uses the non-Bad values. The definition says nothing about Uncertain
            // values in the interval, so §4.2.1.2 applies: with TreatUncertainAsBad = True an
            // Uncertain value is Bad and is not counted (IsGood). The previous value is the bound
            // (Table 72: a non-Bad value prior to the interval), and TreatUncertainAsBad does not
            // apply to bounds (Mantis 11462), so it is the early bound, also when it is Uncertain.
            // The first usable value is a transition when no previous value exists.
            LinkedListNode<DataValue> previousValue = slice.EarlyBound;
            bool hasLastValue = previousValue != null;
            object lastValue = previousValue?.Value.Value;

            // count the transitions.
            int count = 0;

            for (int ii = 0; ii < values.Count; ii++)
            {
                if (!IsGood(values[ii]))
                {
                    continue;
                }

                object nextValue = values[ii].Value;

                if (!hasLastValue || !Utils.IsEqual(lastValue, nextValue))
                {
                    count++;
                }

                hasLastValue = true;
                lastValue = nextValue;
            }

            // set the timestamp and status.
            var value = new DataValue
            {
                WrappedValue = new Variant(count, TypeInfo.Scalars.Int32),
                SourceTimestamp = GetTimestamp(slice),
                ServerTimestamp = GetTimestamp(slice)
            };
            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);
            value.StatusCode = GetValueBasedStatusCode(slice, values, value.StatusCode);

            // return result.
            return value;
        }
    }
}
