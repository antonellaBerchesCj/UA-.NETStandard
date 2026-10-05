/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Calculates the value of an aggregate. 
    /// </summary>
    public class StartEndAggregateCalculator : AggregateCalculator
    {
        #region Constructors
        /// <summary>
        /// Initializes the aggregate calculator.
        /// </summary>
        /// <param name="aggregateId">The aggregate function to apply.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="processingInterval">The processing interval.</param>
        /// <param name="stepped">Whether to use stepped interpolation.</param>
        /// <param name="configuration">The aggregate configuration.</param>
        public StartEndAggregateCalculator(
            NodeId aggregateId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration)
        : 
            base(aggregateId, startTime, endTime, processingInterval, stepped, configuration)
        {
            SetPartialBit = true;
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Computes the value for the timeslice.
        /// </summary>
        protected override DataValue ComputeValue(TimeSlice slice)
        {
            uint? id = AggregateId.Identifier as uint?;

            if (id != null)
            {
                switch (id.Value)
                {
                    case Objects.AggregateFunction_Start:
                    {
                        return ComputeStartEnd(slice, false);
                    }

                    case Objects.AggregateFunction_End:
                    {
                        return ComputeStartEnd(slice, true);
                    }

                    case Objects.AggregateFunction_Delta:
                    {
                        return ComputeDelta(slice);
                    }

                    case Objects.AggregateFunction_StartBound:
                    {
                        return ComputeStartEnd2(slice, false);
                    }

                    case Objects.AggregateFunction_EndBound:
                    {
                        return ComputeStartEnd2(slice, true);
                    }

                    case Objects.AggregateFunction_DeltaBounds:
                    {
                        return ComputeDelta2(slice);
                    }
                }
            }

            return base.ComputeValue(slice);
        }
        #endregion

        #region Protected Methods
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

            // return end value.
            else
            {
                return values[values.Count - 1];
            }
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

            // find start value.
            DataValue start = null;
            double startValue = Double.NaN;
            TypeInfo originalType = null;
            bool badDataSkipped = false;

            for (int ii = 0; ii < values.Count; ii++)
            {
                start = values[ii];

                if (IsGood(start))
                {
                    try
                    {
                        startValue = CastToDouble(start);
                        originalType = start.WrappedValue.TypeInfo;
                        break;
                    }
                    catch (Exception)
                    {
                        startValue = Double.NaN;
                    }
                }

                start = null;
                badDataSkipped = true;
            }

            // find end value.
            DataValue end = null;
            double endValue = Double.NaN;

            for (int ii = values.Count - 1; ii >= 0; ii--)
            {
                end = values[ii];

                if (IsGood(end))    
                {
                    try
                    {
                        endValue = CastToDouble(end);
                        break;
                    }
                    catch (Exception)
                    {
                        endValue = Double.NaN;
                    }

                    break;
                }

                end = null;
                badDataSkipped = true;
            }

            // check if no good data.
            if (Double.IsNaN(startValue) || Double.IsNaN(endValue))
            {
                return GetNoDataValue(slice);
            }
            
            DataValue value = new DataValue();
            value.SourceTimestamp = GetTimestamp(slice);
            value.ServerTimestamp = GetTimestamp(slice);

            // set status code.
            if (badDataSkipped)
            {
                value.StatusCode = StatusCodes.UncertainDataSubNormal;
            }
            
            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);
            
            // calculate delta.
            double delta = endValue - startValue;

            if (originalType != null && originalType.BuiltInType != BuiltInType.Double)
            {
                // widen to the next larger signed type when the delta does not fit the
                // source type (Part 13 §5.4.3.27 asks for the signed difference).
                BuiltInType deltaType = GetDeltaType(originalType.BuiltInType, delta);
                object delta2 = TypeInfo.Cast(delta, TypeInfo.Scalars.Double, deltaType);
                value.WrappedValue = new Variant(
                    delta2,
                    deltaType == originalType.BuiltInType
                        ? originalType
                        : new TypeInfo(deltaType, ValueRanks.Scalar));
            }
            else
            {
                value.WrappedValue = new Variant(delta, TypeInfo.Scalars.Double);
            }

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

            DataValue value = null;

            // return start bound.
            if ((!returnEnd && !TimeFlowsBackward) || (returnEnd && TimeFlowsBackward))
            {
                value = values[0];
            }

            // return end bound.
            else
            {
                value = values[values.Count - 1];
            }

            if (!IsGood(value))
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
        /// Calculates the Delta2 aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeDelta2(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue> values = GetValuesWithSimpleBounds(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            DataValue start = values[0];
            DataValue end = values[values.Count-1];

            // Part 13 §5.4.3.30: Bad bounds return Bad_NoData.
            if (StatusCode.IsBad(start.StatusCode) || StatusCode.IsBad(end.StatusCode))
            {
                return GetNoDataValue(slice);
            }

            // convert to doubles.
            double startValue = 0;
            TypeInfo originalType = null;

            try
            {
                startValue = CastToDouble(start);
                originalType = start.WrappedValue.TypeInfo;
            }
            catch (Exception)
            {
                startValue = Double.NaN;
            }

            double endValue = 0;

            try
            {
                endValue = CastToDouble(end);
            }
            catch (Exception)
            {
                endValue = Double.NaN;
            }

            // check for bad bounds.
            if (Double.IsNaN(startValue) || Double.IsNaN(endValue))
            {
                return GetNoDataValue(slice);
            }

            DataValue value = new DataValue();
            value.SourceTimestamp = GetTimestamp(slice);
            value.ServerTimestamp = GetTimestamp(slice);

            // Part 13 §5.4.3.30: Uncertain bounds return Uncertain_DataSubNormal.
            if (StatusCode.IsUncertain(start.StatusCode) ||
                StatusCode.IsUncertain(end.StatusCode))
            {
                value.StatusCode = StatusCodes.UncertainDataSubNormal;
            }

            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            // calculate delta.
            double delta = endValue - startValue;

            if (originalType != null && originalType.BuiltInType != BuiltInType.Double)
            {
                // widen to the next larger signed type when the delta does not fit the
                // source type (Part 13 §5.4.3.27 asks for the signed difference).
                BuiltInType deltaType = GetDeltaType(originalType.BuiltInType, delta);
                object delta2 = TypeInfo.Cast(delta, TypeInfo.Scalars.Double, deltaType);
                value.WrappedValue = new Variant(
                    delta2,
                    deltaType == originalType.BuiltInType
                        ? originalType
                        : new TypeInfo(deltaType, ValueRanks.Scalar));
            }
            else
            {
                value.WrappedValue = new Variant(delta, TypeInfo.Scalars.Double);
            }

            // return result.
            return value;
        }

        /// <summary>
        /// Returns the data type of a Delta/DeltaBounds result. Part 13
        /// 5.4.3.27 asks for the source type but also for the signed difference;
        /// a delta outside the range of the source type (a negative delta of an
        /// unsigned source, or a signed delta that overflows) is widened to the
        /// next larger signed type (Double for 64-bit sources) instead of failing
        /// with Bad_TypeMismatch.
        /// </summary>
        private static BuiltInType GetDeltaType(BuiltInType sourceType, double delta)
        {
            switch (sourceType)
            {
                case BuiltInType.SByte:
                    return delta < sbyte.MinValue || delta > sbyte.MaxValue ? BuiltInType.Int16 : sourceType;
                case BuiltInType.Byte:
                    return delta < byte.MinValue || delta > byte.MaxValue ? BuiltInType.Int16 : sourceType;
                case BuiltInType.Int16:
                    return delta < short.MinValue || delta > short.MaxValue ? BuiltInType.Int32 : sourceType;
                case BuiltInType.UInt16:
                    return delta < ushort.MinValue || delta > ushort.MaxValue ? BuiltInType.Int32 : sourceType;
                case BuiltInType.Int32:
                    return delta < int.MinValue || delta > int.MaxValue ? BuiltInType.Int64 : sourceType;
                case BuiltInType.UInt32:
                    return delta < uint.MinValue || delta > uint.MaxValue ? BuiltInType.Int64 : sourceType;
                case BuiltInType.Int64:
                    return delta < long.MinValue || delta >= long.MaxValue ? BuiltInType.Double : sourceType;
                case BuiltInType.UInt64:
                    return delta < ulong.MinValue || delta >= ulong.MaxValue ? BuiltInType.Double : sourceType;
                default:
                    return sourceType;
            }
        }
        #endregion
    }
}
