using ComputerAlgebra;
using System;
using System.ComponentModel;

namespace Circuit
{
    /// <summary>
    /// What kind of sweep a parameter should make.
    /// </summary>
    public enum SweepType
    {
        Linear,
        Sigmoid,
        AntiSigmoid,
        Logarithmic,
        AntiLogarithmic,
        // These are equivalent to just swapping around connections on potentiometers,
        // but variable resistors sometimes need them.
        ReverseLinear,
        ReverseSigmoid,
        ReverseAntiSigmoid,
        ReverseLogarithmic,
        ReverseAntiLogarithmic,
    }

    static class SweepTypeMethods
    {
        /// <summary>
        /// Return the Asian/American marking letter of the sweep.
        /// </summary>
        public static string GetCode(this SweepType sweep)
        {
            switch (sweep)
            {
                case SweepType.Linear:
                case SweepType.ReverseLinear:
                    return "B";
                case SweepType.Sigmoid:
                case SweepType.ReverseSigmoid:
                case SweepType.AntiSigmoid:
                case SweepType.ReverseAntiSigmoid:
                    return "W";
                case SweepType.Logarithmic:
                case SweepType.ReverseLogarithmic:
                    return "A";
                case SweepType.AntiLogarithmic:
                case SweepType.ReverseAntiLogarithmic:
                    return "C";
                default:
                    return "";
            }
        }
    }

    [Category("Generic")]
    [DisplayName("Variable Resistor")]
    [DefaultProperty("Resistance")]
    [Description("Variable resistor.")]
    public class VariableResistor : TwoTerminal, IPotControl
    {
        protected Quantity resistance = new Quantity(100, Units.Ohm);
        [Serialize, Description("Resistance of this variable resistor.")]
        public Quantity Resistance { get { return resistance; } set { if (resistance.Set(value)) NotifyChanged(nameof(Resistance)); } }

        protected double wipe = 0.5;
        [Serialize, Description("Position of the wiper on this variable resistor, between 0 and 1.")]
        public double Wipe { get { return wipe; } set { wipe = value; NotifyChanged(nameof(Wipe)); } }
        // IPotControl
        double IPotControl.PotValue { get { return Wipe; } set { Wipe = value; } }

        protected SweepType sweep = SweepType.Linear;
        [Serialize, Description("Sweep mapping of the wiper.")]
        public SweepType Sweep { get { return sweep; } set { sweep = value; NotifyChanged(nameof(Sweep)); } }

        private string group = "";
        [Serialize, Description("Potentiometer group this potentiometer is a section of.")]
        public string Group { get { return group; } set { group = value; NotifyChanged(nameof(Group)); } }

        public VariableResistor() { Name = "R1"; }

        public override void Analyze(Analysis Mna)
        {
            if (!Mna.IsLive(Name))
            {
                // Exactly what this component did before Stompbench milestone A4.
                Resistor.Analyze(Mna, Name, Anode, Cathode, (Expression)Resistance * AdjustWipe(wipe, sweep));
                return;
            }

            // A conductance rather than a resistance, for the reason given in
            // Resistor.AnalyzeConductance: modified nodal analysis is linear in one and not the
            // other, and the difference only shows once the value is a symbol.
            //
            // The sweep is captured now rather than read from the field inside the mapping, so that
            // the parameter handed out here always describes the curve these equations were built
            // with. A sweep change is a structural change and invalidates the analysis; a parameter
            // whose curve silently followed the field would not say so.
            SweepType s = sweep;
            double resistance = (double)Resistance;

            Func<double, double> conductance = x => 1.0 / (resistance * AdjustWipe(x, s));
            LiveParameter.RangeOf(conductance, out double low, out double high);

            Expression G = Mna.DeclareParameter(Name, "G", wipe, conductance, low, high);

            Resistor.AnalyzeConductance(Mna, Name, Anode, Cathode, G);
        }

        /// <summary>
        /// The taper: turns a control position into the fraction of the resistance in circuit.
        /// </summary>
        /// <remarks>
        /// Stompbench milestone A4 left this on the control side rather than making it symbolic. The
        /// clamp on the first line is why that decision is not merely tidy: it is the only thing
        /// standing between a control surface and a zero resistance, which is a division by zero in
        /// the current through it, and doing it here means the equations never see the degenerate
        /// value at all. The curves would also have made a linear circuit nonlinear if they had gone
        /// into the equations, because a <c>Pow</c> in a coefficient creates a Newton iteration where
        /// none existed.
        /// </remarks>
        public static double AdjustWipe(double x, SweepType Sweep)
        {
            x = Math.Min(Math.Max(x, 1e-3), 1.0 - 1e-3);
            
            // If we want the parameter to be backwards, swap it.
            if (Sweep == SweepType.ReverseLinear || Sweep == SweepType.ReverseSigmoid ||
                Sweep == SweepType.ReverseLogarithmic || Sweep == SweepType.ReverseAntiLogarithmic)
                x = 1 - x;

            double exp = Math.Exp(2);
            const double k = 1.8; // Sigmoid shape factor.
            switch (Sweep)
            {
                // If we want the parameter to be logarithmic, apply an exponential curve
                // passing through 0 and 1.
                case SweepType.Logarithmic:
                case SweepType.ReverseLogarithmic:
                    return (Math.Pow(exp, x) - 1.0) / (exp - 1.0);
                // If we want the parameter to be anti-logarithmic, apply an exponential curve
                // passing through 0 and 1.
                case SweepType.AntiLogarithmic:
                case SweepType.ReverseAntiLogarithmic:
                    return 1 - (Math.Pow(exp, 1-x) - 1.0) / (exp - 1.0);
                // If we want the parameter to be s-shaped, apply an sigmoid curve
                // passing through 0 and 1.
                case SweepType.Sigmoid:
                case SweepType.ReverseSigmoid:
                    return 1 / (1 + Math.Pow(x/(1-x), -k));
                // If we want the parameter to be s-shaped inverted, apply an sigmoid curve
                // passing through 0 and 1.
                case SweepType.AntiSigmoid:
                case SweepType.ReverseAntiSigmoid:
                    return 1 / (1 + Math.Pow(x/(1-x), -1/k));
                default:
                    return x;
            }
        }

        protected internal override void LayoutSymbol(SymbolLayout Sym)
        {
            base.LayoutSymbol(Sym);

            Sym.AddWire(Anode, new Coord(0, 16));
            Sym.AddWire(Cathode, new Coord(0, -16));
            Sym.InBounds(new Coord(-10, 0), new Coord(10, 0));

            Resistor.Draw(Sym, 0, -16, 16, 7);

            Sym.DrawArrow(EdgeType.Black, new Coord(-6, -15), new Coord(6, 15), 0.1);

            Sym.DrawText(() => Sweep.GetCode()+Resistance.ToString(), new Coord(-7, 0), Alignment.Far, Alignment.Center);
            Sym.DrawText(() => Wipe.ToString("G3"), new Coord(9, 3), Alignment.Near, Alignment.Near);
            Sym.DrawText(() => Name, new Coord(9, -3), Alignment.Near, Alignment.Far);
        }
    }
}
