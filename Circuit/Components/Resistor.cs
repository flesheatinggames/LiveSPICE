using ComputerAlgebra;
using System;
using System.ComponentModel;

namespace Circuit
{
    /// <summary>
    /// Resistor is a linear component with V = R*i.
    /// </summary>
    [Category("Generic")]
    [DisplayName("Resistor")]
    [DefaultProperty("Resistance")]
    [Description("Standard resistor.")]
    public class Resistor : TwoTerminal
    {
        protected Quantity resistance = new Quantity(100, Units.Ohm);
        [Serialize, Description("Resistance of this resistor.")]
        public Quantity Resistance { get { return resistance; } set { if (resistance.Set(value)) NotifyChanged(nameof(Resistance)); } }

        public Resistor() { Name = "R1"; }

        public static Expression Analyze(Analysis Mna, string Name, Node Anode, Node Cathode, Expression R)
        {
            // i = V/R
            if (R.EqualsZero())
            {
                return Conductor.Analyze(Mna, Name, Anode, Cathode);
            }
            else
            {
                Expression i = (Anode.V - Cathode.V) / R;
                Mna.AddPassiveComponent(Anode, Cathode, i);
                return i;
            }
        }
        public static Expression Analyze(Analysis Mna, Node Anode, Node Cathode, Expression R) { return Analyze(Mna, "", Anode, Cathode, R); }

        /// <summary>
        /// The same component written the other way up: i = V*G rather than i = V/R.
        /// </summary>
        /// <remarks>
        /// Added for Stompbench milestone A4, which measured why it matters. Modified nodal analysis
        /// is linear in conductance and not in resistance, so where the value is a symbol rather than
        /// a number the two forms are not interchangeable. Written as i = V/R the symbol lands in a
        /// denominator, every row-reduction step has to put rows over a common denominator, and the
        /// solved expressions grow as rational functions of it. Written as i = V*G the symbol is a
        /// plain coefficient and the elimination handles it the way it handles every other one.
        ///
        /// This makes no difference at all when the value is a number, because either form folds. It
        /// is only for the symbolic case, which is why the components that use it fall back to
        /// <see cref="Analyze(Analysis, string, Node, Node, Expression)"/> when their value is baked:
        /// that path folds an exact rational where this one would fold a rounded reciprocal, and
        /// keeping it means the baked setting is exactly the behaviour that shipped before A4.
        /// </remarks>
        public static Expression AnalyzeConductance(Analysis Mna, string Name, Node Anode, Node Cathode, Expression G)
        {
            Expression i = (Anode.V - Cathode.V) * G;
            Mna.AddPassiveComponent(Anode, Cathode, i);
            return i;
        }

        /// <summary>
        /// How far either side of its nominal value a swept component ranges, as a factor.
        /// </summary>
        /// <remarks>
        /// A decade down and a decade up, with the nominal value at the middle of the travel, and
        /// the sweep logarithmic in between because component values are: the E12 series is
        /// logarithmic, and a linear sweep from a tenth to ten times would spend nine tenths of its
        /// travel above the nominal value.
        ///
        /// This is a policy about how far a sweep handle reaches, not a statement about where the
        /// equations are valid. A conductance is a plain coefficient and the algebra holds for any
        /// positive value, unlike a potentiometer's wiper, where a position of exactly zero is a
        /// division by zero in the current through it. An interface that wants to set an absolute
        /// value rather than a position on a handle is the natural extension, and is what the
        /// interface–engine contract should carry.
        /// </remarks>
        public const double SweepDecades = 1.0;

        /// <summary>
        /// The value a sweep handle at <paramref name="position"/> asks for, given a nominal value.
        /// </summary>
        public static double Sweep(double position, double nominal)
        {
            position = Math.Min(Math.Max(position, 0.0), 1.0);
            return nominal * Math.Pow(10.0, SweepDecades * (2.0 * position - 1.0));
        }

        public override void Analyze(Analysis Mna)
        {
            // Named only, never by default, and this is the whole reason a plain resistor asks
            // IsNamedLive where a potentiometer asks IsLive. A knob is drawn on the schematic as
            // adjustable and there are a handful of them; a resistor is not, and a circuit has
            // dozens. What a live value costs compounds with how many there are — milestone A4
            // measured a second live knob at up to thirty-one times the first — so a default that
            // made every resistor symbolic would not be a slower solve, it would be no solve at all.
            //
            // What this is for is the sweep handle of requirements §33: the interface names the one
            // component whose label is being dragged, pays one solve for it, and every value the
            // drag passes through afterwards is a memory write.
            if (!Mna.IsNamedLive(Name))
            {
                Analyze(Mna, Name, Anode, Cathode, Resistance);
                return;
            }

            // A conductance rather than a resistance, for the reason given in AnalyzeConductance:
            // modified nodal analysis is linear in one and not the other, and the difference only
            // shows once the value is a symbol.
            double nominal = (double)Resistance;
            Func<double, double> conductance = x => 1.0 / Sweep(x, nominal);
            LiveParameter.RangeOf(conductance, out double low, out double high);

            // Half way along the handle is the value the schematic says, so a circuit nobody sweeps
            // is the circuit that was drawn.
            Expression G = Mna.DeclareParameter(Name, "G", 0.5, conductance, low, high);
            AnalyzeConductance(Mna, Name, Anode, Cathode, G);
        }

        public static void Draw(SymbolLayout Sym, double x, double y1, double y2, int N, double Scale)
        {
            double h = y2 - y1;

            Sym.DrawFunction(
                EdgeType.Black,
                (t) => x - Scale * (Math.Abs((t + 0.5) % 2 - 1) * 2 - 1),
                (t) => t * h / N + y1,
                0, N, N * 2);
        }
        public static void Draw(SymbolLayout Sym, double x, double y1, double y2, int N) { Draw(Sym, x, y1, y2, N, (y2 - y1) / (N + 1)); }

        protected internal override void LayoutSymbol(SymbolLayout Sym)
        {
            base.LayoutSymbol(Sym);

            Sym.AddWire(Anode, new Coord(0, 16));
            Sym.AddWire(Cathode, new Coord(0, -16));
            Sym.InBounds(new Coord(-10, 0), new Coord(10, 0));

            Draw(Sym, 0, -16, 16, 7);

            Sym.DrawText(() => Name, new Coord(6, 0), Alignment.Near, Alignment.Center);
            Sym.DrawText(() => resistance.ToString(), new Coord(-6, 0), Alignment.Far, Alignment.Center);
        }
    }
}
