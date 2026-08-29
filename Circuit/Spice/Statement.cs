using ComputerAlgebra;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Circuit.Spice
{
    public class Statement
    {
        // Parsing quantities.
        private static readonly Dictionary<string, double> Prefixes = new Dictionary<string, double>()
        {
            { "F", 1e-15 },
            { "P", 1e-12 },
            { "N", 1e-9 },
            { "U", 1e-6 },
            { "M", 1e-3 },
            { "K", 1e+3 },
            { "MEG", 1e+6 },
            { "G", 1e+9 },
            { "T", 1e+12 },
        };

        /// <summary>
        /// A number with an optional SPICE metric suffix on it.
        /// </summary>
        /// <remarks>
        /// <b>MEG comes first in the alternation, and that is the whole of a nine-order-of-magnitude
        /// bug.</b> .NET tries the branches of an alternation left to right and takes the first that
        /// lets the rest of the pattern succeed, and the rest of this pattern is ".*", which succeeds
        /// against anything. So with M before MEG the string "10MEG" matched M, the ".*" swallowed the
        /// "EG", and a ten-megohm resistor imported as ten milliohms. MEG was unreachable: no input
        /// could ever reach that branch. Longest first is the fix, and it costs nothing — "10M" tries
        /// MEG, fails for want of the EG, and falls through to M as it always did.
        ///
        /// The number is parsed in the invariant culture rather than the machine's, for the reason
        /// Expression.Parse and Real.Parse both record: this is a figure out of a vendor's model file
        /// rather than something a person typed, and under a comma-decimal locale "2.52" would
        /// otherwise read as two hundred and fifty-two.
        /// </remarks>
        private static readonly Regex Quantity = new Regex(@"([-+]?[0-9]*\.?[0-9]+([eE][-+]?[0-9]+)?)(MEG|F|P|N|U|M|K|G|T)?.*", RegexOptions.IgnoreCase);
        public static Expression ParseValue(string s)
        {
            Match m = Quantity.Match(s);
            if (m.Success)
            {
                double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                double p = 1;
                if (m.Groups[3].Success)
                    p = Prefixes[m.Groups[3].Value.ToUpper()];
                return v * p;
            }
            else
            {
                throw new Exception("Unable to parse quantity '" + s + "'.");
            }
        }
    }
}
