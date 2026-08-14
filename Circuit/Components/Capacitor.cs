using ComputerAlgebra;
using System;
using System.ComponentModel;

namespace Circuit
{
    /// <summary>
    /// Capacitor is a passive linear component with i = C*dV/dt.
    /// </summary>
    [Category("Generic")]
    [DisplayName("Capacitor")]
    [DefaultProperty("Capacitance")]
    [Description("Standard capacitor component")]
    public class Capacitor : TwoTerminal
    {
        private Quantity capacitance = new Quantity(100e-6m, Units.F);
        [Serialize, Description("Capacitance of this capacitor.")]
        public Quantity Capacitance { get { return capacitance; } set { if (capacitance.Set(value)) NotifyChanged(nameof(Capacitance)); } }

        public Capacitor() { Name = "C1"; }

        public static Expression Analyze(Analysis Mna, string Name, Node Anode, Node Cathode, Expression C)
        {
            // Ensure that V is not multiple variables.
            Expression V = Anode.V - Cathode.V;
            V = Mna.AddUnknownEqualTo("V" + Name, V);
            // i = C*dV/dt
            Expression i = C * D(V, t);
            //i = Mna.AddUnknownEqualTo("i" + Name, i);
            Mna.AddPassiveComponent(Anode, Cathode, i);
            return i;
        }
        public static Expression Analyze(Analysis Mna, Node Anode, Node Cathode, Expression C) { return Analyze(Mna, Mna.AnonymousName(), Anode, Cathode, C); }

        public override void Analyze(Analysis Mna)
        {
            // Named only, for the reason given at Resistor.Analyze: a circuit has dozens of these
            // and what a live value costs compounds with how many there are.
            if (!Mna.IsNamedLive(Name))
            {
                Analyze(Mna, Name, Anode, Cathode, Capacitance);
                return;
            }

            // The capacitance is a plain coefficient on a derivative, so unlike a resistance it
            // needs no reformulation to stay cheap symbolically — i = C*dV/dt is already linear in
            // C. What it does need is saying out loud: the discretisation puts C on the stored
            // previous voltages as well as on this sample's, so a value that changes between two
            // samples changes the weight given to charge that is already there. Physically the
            // stored quantity is Q = CV, and swapping a capacitor for a different one while it holds
            // charge is not something a circuit can do — you would be unsoldering it. A sweep is
            // therefore an editing gesture rather than a physical one, and a large jump may thump.
            // Milestone A5's crossfade is the fallback if it does, and the sweep is the thing to
            // measure before deciding which path a capacitor edit should take.
            double nominal = (double)Capacitance;
            Func<double, double> capacitance = x => Resistor.Sweep(x, nominal);
            LiveParameter.RangeOf(capacitance, out double low, out double high);

            Expression C = Mna.DeclareParameter(Name, "C", 0.5, capacitance, low, high);
            Analyze(Mna, Name, Anode, Cathode, C);
        }

        protected internal override void LayoutSymbol(SymbolLayout Sym)
        {
            base.LayoutSymbol(Sym);

            Sym.AddWire(Anode, new Coord(0, 2));
            Sym.AddWire(Cathode, new Coord(0, -2));

            Sym.AddLine(EdgeType.Black, new Coord(-10, 2), new Coord(10, 2));
            Sym.AddLine(EdgeType.Black, new Coord(-10, -2), new Coord(10, -2));

            Sym.DrawText(() => Name, new Coord(12, 0), Alignment.Near, Alignment.Center);
            Sym.DrawText(() => capacitance.ToString(), new Coord(-12, 0), Alignment.Far, Alignment.Center);
        }
    }
}
