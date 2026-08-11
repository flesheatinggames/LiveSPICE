using ComputerAlgebra;
using System;

namespace Circuit
{
    /// <summary>
    /// A component value that stays a symbol through the solve, so that changing it is a memory
    /// write rather than a reason to solve the circuit again.
    /// </summary>
    /// <remarks>
    /// Added for Stompbench milestone A4. Before it, a potentiometer's wiper position was a plain
    /// <c>double</c> that <see cref="Potentiometer.Analyze"/> multiplied into the resistances, so it
    /// was part of the equations by the time anything downstream saw them and every knob movement
    /// invalidated the solution. A solve costs 35 to 528 ms measured across this repository's
    /// schematics and an audio buffer is 1333 µs, so that is not a thing that can happen while
    /// someone is playing.
    ///
    /// Two values rather than one, and the distinction is the whole design. <see cref="Position"/> is
    /// what a knob is at, from 0 to 1. <see cref="Value"/> is what the equations contain, which for a
    /// potentiometer is the position after its taper curve has been applied. The curve stays on the
    /// control side — <see cref="Map"/> is how a caller crosses from one to the other — because
    /// applying it symbolically would put a <c>Pow</c> inside the circuit equations and turn a linear
    /// circuit nonlinear, creating a Newton iteration where none existed and costing far more than
    /// leaving the position baked ever did.
    ///
    /// The same reasoning covers the clamp. <see cref="Minimum"/> and <see cref="Maximum"/> are the
    /// range the equations were derived on the assumption of, and they are not decoration: a wiper at
    /// exactly zero is a zero resistance, which is a division by zero in the current through it. Doing
    /// the clamping where the position is converted means the equations never see the degenerate
    /// value at all, and <see cref="Map"/> is what does it.
    /// </remarks>
    public sealed class LiveParameter
    {
        /// <summary>
        /// The symbol's name, and the name a control surface addresses this parameter by. Carries the
        /// subcircuit prefix the rest of the analysis uses, so "R1.Wipe" at the top level and
        /// "X1.R1.Wipe" one level in.
        /// </summary>
        public string Name { get; }

        /// <summary>The component this came from, without the prefix or the quantity.</summary>
        public string Component { get; }

        /// <summary>Which value of that component this is: "Wipe", and later "IS" or "N".</summary>
        public string Quantity { get; }

        /// <summary>The symbol standing in for this value everywhere in the equations.</summary>
        public Variable Symbol { get; }

        /// <summary>Where the control was when the circuit was analyzed. 0 to 1 for a wiper.</summary>
        public double Position { get; }

        /// <summary>
        /// What the equations were built with, which is <see cref="Map"/> of <see cref="Position"/>.
        /// This is the number the steady-state solve substitutes and the value a parameter slot starts
        /// at, so that a circuit that is never touched behaves exactly as it did before A4.
        /// </summary>
        public double Value { get; }

        /// <summary>The smallest value the equations remain valid for.</summary>
        public double Minimum { get; }

        /// <summary>The largest value the equations remain valid for.</summary>
        public double Maximum { get; }

        /// <summary>
        /// Turns a control position into the value the equations want, applying whatever curve and
        /// clamp the component defines. Guaranteed to land inside
        /// [<see cref="Minimum"/>, <see cref="Maximum"/>].
        /// </summary>
        public Func<double, double> Map { get; }

        public LiveParameter(
            string Name,
            string Component,
            string Quantity,
            double Position,
            Func<double, double> Map,
            double Minimum,
            double Maximum)
        {
            this.Name = Name;
            this.Component = Component;
            this.Quantity = Quantity;
            this.Position = Position;
            this.Map = Map;
            this.Minimum = Minimum;
            this.Maximum = Maximum;
            Symbol = Variable.New(Name);
            Value = Map(Position);
        }

        /// <summary>The substitution that puts this parameter's analyzed value back into a system.</summary>
        public Arrow Baked { get { return Arrow.New(Symbol, Value); } }

        /// <summary>
        /// The range a control-position-to-equation-value mapping covers, read off its ends.
        /// </summary>
        /// <remarks>
        /// Exact for every mapping this library declares, because each is monotonic in the control
        /// position: the tapers are increasing functions of the position, the reversed ones reverse
        /// the position first, and a conductance is a decreasing function of a resistance that is
        /// itself increasing. So the extremes are at the ends and nowhere in between, and evaluating
        /// the mapping there is a better way to establish the range than restating its algebra in a
        /// second place where the two could drift apart.
        /// </remarks>
        public static void RangeOf(Func<double, double> Map, out double Minimum, out double Maximum)
        {
            double a = Map(0), b = Map(1);
            Minimum = Math.Min(a, b);
            Maximum = Math.Max(a, b);
        }

        public override string ToString() { return Name + " = " + Value; }
    }
}
