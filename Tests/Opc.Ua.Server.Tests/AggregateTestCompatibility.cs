/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Adapts the aggregate calculator tests written for the newer API
    /// (master378 with the CTT backports) to the 1.4.371 public contracts.
    /// </summary>
    internal static class AggregateTestCompatibility
    {
        /// <summary>
        /// Returns the next processed value; false when the calculator has no more values.
        /// </summary>
        public static bool TryGetProcessedValue(
            this IAggregateCalculator calculator,
            bool returnPartial,
            out DataValue value)
        {
            value = calculator.GetProcessedValue(returnPartial);
            return value != null;
        }

        /// <summary>
        /// Converts the value of the variant to a Double.
        /// </summary>
        public static double ConvertToDouble(this Variant value)
        {
            return Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns the value (keeps the newer <c>ConvertToDouble().GetDouble()</c> call chains).
        /// </summary>
        public static double GetDouble(this double value)
        {
            return value;
        }

        /// <summary>
        /// True if the variant has no value (the newer <c>Variant.IsNull</c> property).
        /// </summary>
        public static bool IsNull(this Variant value)
        {
            return value.Value == null;
        }

        /// <summary>
        /// Gets the value of the variant as the requested type.
        /// </summary>
        public static bool TryGetValue<T>(this Variant value, out T result)
        {
            if (value.Value is T typed)
            {
                result = typed;
                return true;
            }

            try
            {
                result = (T)Convert.ChangeType(value.Value, typeof(T), CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                result = default(T);
                return false;
            }
        }
    }
}
