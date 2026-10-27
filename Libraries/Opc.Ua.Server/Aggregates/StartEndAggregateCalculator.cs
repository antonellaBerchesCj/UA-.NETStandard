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
    public class StartEndAggregateCalculator : AggregateCalculator
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
        public StartEndAggregateCalculator(
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
                case Objects.AggregateFunction_Start:
                    return ComputeStartEnd(slice, false);
                case Objects.AggregateFunction_End:
                    return ComputeStartEnd(slice, true);
                case Objects.AggregateFunction_Delta:
                    return ComputeDelta(slice);
                case Objects.AggregateFunction_StartBound:
                    return ComputeStartEnd2(slice, false);
                case Objects.AggregateFunction_EndBound:
                    return ComputeStartEnd2(slice, true);
                case Objects.AggregateFunction_DeltaBounds:
                    return ComputeDelta2(slice);
                default:
                    return base.ComputeValue(slice);
            }
        }

        /// <summary>
        /// Calculate the Start and End aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeStartEnd(TimeSlice slice, bool returnEnd)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            // return start value.
            if (!returnEnd)
            {
                return values[0];
            }

            return values[^1];
        }

        /// <summary>
        /// Calculates the Delta aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeDelta(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            double startValue = double.NaN;
            TypeInfo originalType = null;
            bool badDataSkipped = false;

            for (int ii = 0; ii < values.Count; ii++)
            {
                // find start value: Part 13 §5.4.3.27 uses Good raw values only, whatever
                // TreatUncertainAsBad is (Mantis 2302, 3302, 11495).
                DataValue start = values[ii];

                if (StatusCode.IsGood(start.StatusCode))
                {
                    try
                    {
                        startValue = CastToDouble(start);
                        originalType = start.WrappedValue.TypeInfo;
                        break;
                    }
                    catch (Exception)
                    {
                        startValue = double.NaN;
                    }
                }

                badDataSkipped = true;
            }

            double endValue = double.NaN;

            for (int ii = values.Count - 1; ii >= 0; ii--)
            {
                // find end value.
                DataValue end = values[ii];

                if (StatusCode.IsGood(end.StatusCode))
                {
                    try
                    {
                        endValue = CastToDouble(end);
                        break;
                    }
                    catch (Exception)
                    {
                        endValue = double.NaN;
                    }

                    break;
                }

                badDataSkipped = true;
            }

            // check if no good data.
            if (double.IsNaN(startValue) || double.IsNaN(endValue))
            {
                return GetNoDataValue(slice);
            }

            var value = new DataValue
            {
                SourceTimestamp = GetTimestamp(slice),
                ServerTimestamp = GetTimestamp(slice)
            };

            // set status code.
            if (badDataSkipped)
            {
                value.StatusCode = StatusCodes.UncertainDataSubNormal;
            }

            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            // calculate delta.
            double delta = endValue - startValue;

            value.WrappedValue = CreateDeltaValue(delta, originalType);

            // return result.
            return value;
        }

        /// <summary>
        /// Calculate the Start2 and End2 aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeStartEnd2(TimeSlice slice, bool returnEnd)
        {
            // get the values in the slice.
            List<DataValue> values = GetValuesWithSimpleBounds(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            DataValue value;

            // return start bound.
            if ((!returnEnd && !TimeFlowsBackward) || (returnEnd && TimeFlowsBackward))
            {
                value = values[0];
            }
            // return end bound.
            else
            {
                value = values[^1];
            }

            // The interpolated bound is a Double. StartBound/EndBound return it in the source type
            // (Tables 76, 77), converted with TypeInfo.Cast as in 1.5.378 (Mantis 11494).
            value = ConvertToSourceType(value, slice);

            // Part 13 Tables 76/77: a Bad bound is Bad_NoData and an Uncertain bound keeps its status
            // ("Bound Uncertain: Same as bound"). TreatUncertainAsBad does not apply to bounds
            // (Mantis 11462).
            if (!IsBoundCandidate(value))
            {
                value.StatusCode = StatusCodes.BadNoData;
            }

            if (returnEnd)
            {
                value.SourceTimestamp = GetTimestamp(slice);
                value.ServerTimestamp = GetTimestamp(slice);

                if (StatusCode.IsNotBad(value.StatusCode))
                {
                    value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);
                }
            }

            return value;
        }

        /// <summary>
        /// Converts an interpolated (Double) bound to the source data type (Part 13 Table 50).
        /// </summary>
        private DataValue ConvertToSourceType(DataValue value, TimeSlice slice)
        {
            if (StatusCode.IsBad(value.StatusCode) || value.WrappedValue.TypeInfo?.BuiltInType != BuiltInType.Double)
            {
                return value; // exact match/raw/stepped
            }

            // the source type is taken from a raw value from the slice
            LinkedListNode<DataValue> raw = slice.EarlyBound ?? slice.Begin ?? slice.LateBound;

            if (raw == null)
            {
                return value;
            }

            TypeInfo sourceType = GetSourceType(slice);

            if (sourceType == null)
            {
                return value;
            }

            // non-numeric values (Boolean, String) cannot be interpolated with a sloped line.
            if (!TypeInfo.IsNumericType(sourceType.BuiltInType))
            {
                return GetNoDataValue(value.SourceTimestamp);
            }

            if (sourceType.BuiltInType == BuiltInType.Double)
            {
                return value;
            }

            object castValue = TypeInfo.Cast(CastToDouble(value), TypeInfo.Scalars.Double, sourceType.BuiltInType);

            return new DataValue(new Variant(castValue, sourceType), value.StatusCode, value.SourceTimestamp, value.ServerTimestamp);
        }


        private static TypeInfo GetSourceType(TimeSlice slice)
        {
            LinkedListNode<DataValue> node = slice.EarlyBound ?? slice.Begin ?? slice.LateBound;

            if (node?.List == null)
            {
                return null;
            }

            // first real raw value (jump over the BadBoundNotFound placeholders with Value = null)
            foreach (DataValue dv in node.List)
            {
                if (dv.Value != null && StatusCode.IsNotBad(dv.StatusCode))
                {
                    return dv.WrappedValue.TypeInfo;
                }
            }

            return null;
        }

        /// <summary>
        /// Calculates the Delta2 aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeDelta2(TimeSlice slice)
        {
            // the bounds as Double values; they are converted to the source type below.
            DataValue start = GetSimpleBound(slice.StartTime, slice, useOriginalType: false);
            DataValue end = GetSimpleBound(slice.EndTime, slice, useOriginalType: false);

            // Table 78: a Bad bound gives Bad_NoData and an Uncertain bound makes the result
            // Uncertain_DataSubNormal. TreatUncertainAsBad does not apply to bounds (Mantis 11462).
            if (!IsBoundCandidate(start) || !IsBoundCandidate(end))
            {
                return GetNoDataValue(slice);
            }

            // convert to double
            double startValue, endValue;
            try
            {
                startValue = CastToDouble(start);
            }
            catch (Exception)
            {
                startValue = double.NaN;
            }

            try
            {
                endValue = CastToDouble(end);
            }
            catch (Exception)
            {
                endValue = double.NaN;
            }

            // check for bad bounds.
            if (double.IsNaN(startValue) || double.IsNaN(endValue))
            {
                return GetNoDataValue(slice);
            }

            var value = new DataValue
            {
                SourceTimestamp = GetTimestamp(slice),
                ServerTimestamp = GetTimestamp(slice)
            };

            if (StatusCode.IsUncertain(start.StatusCode) ||
                StatusCode.IsUncertain(end.StatusCode))
            {
                value.StatusCode = StatusCodes.UncertainDataSubNormal;
            }

            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            TypeInfo originalType = RawDataType ?? start.WrappedValue.TypeInfo;

            // the bounds are values of the source data type (StartBound and EndBound, Table 50).
            double delta =
                RoundToSourceType(endValue, originalType) -
                RoundToSourceType(startValue, originalType);

            value.WrappedValue = CreateDeltaValue(delta, originalType);

            // return result.
            return value;
        }

        /// <summary>
        /// Returns the Delta/DeltaBounds result in the data type of the raw values (Part 13
        /// Table 50). A delta that does not fit into that type cannot be converted, which
        /// GetProcessedValue reports as Bad_AggregateInvalidInputs (Table 45).
        /// </summary>
        private static Variant CreateDeltaValue(double delta, TypeInfo originalType)
        {
            if (originalType != null && originalType.BuiltInType != BuiltInType.Double)
            {
                object delta2 = TypeInfo.Cast(
                    delta,
                    TypeInfo.Scalars.Double,
                    originalType.BuiltInType);
                return new Variant(delta2, originalType);
            }

            return new Variant(delta, TypeInfo.Scalars.Double);
        }
    }
}
