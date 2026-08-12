using ComputerAlgebra;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Circuit
{
    /// <summary>
    /// Resistor is a linear component with V = R*i.
    /// </summary>
    [Category("Generic")]
    [DisplayName("Potentiometer")]
    [DefaultProperty("Resistance")]
    [Description("Represents a potentiometer. When Wipe is 0, the wiper is at the cathode.")]
    public class Potentiometer : Component, IPotControl
    {
        private Terminal anode, cathode, wiper;
        [Browsable(false)] public Terminal Anode => anode;
        [Browsable(false)] public Terminal Cathode => cathode;
        [Browsable(false)] public Terminal Wiper => wiper;

        public override IEnumerable<Terminal> Terminals
        {
            get
            {
                yield return anode;
                yield return cathode;
                yield return wiper;
            }
        }

        public Potentiometer()
        {
            anode = new Terminal(this, "Anode");
            cathode = new Terminal(this, "Cathode");
            wiper = new Terminal(this, "Wiper");
            Name = "R1";
        }

        protected Quantity resistance = new Quantity(100, Units.Ohm);
        [Serialize, Description("Resistance of this potentiometer.")]
        public Quantity Resistance { get { return resistance; } set { if (resistance.Set(value)) NotifyChanged(nameof(Resistance)); } }

        protected double wipe = 0.5;
        [Serialize, Description("Position of the wiper, between 0 and 1.")]
        public double Wipe { get { return wipe; } set { wipe = value; NotifyChanged(nameof(Wipe)); NotifyChanged(nameof(IPotControl.PotValue)); } }
        // IPotControl
        double IPotControl.PotValue { get { return Wipe; } set { Wipe = value; } }

        protected SweepType sweep = SweepType.Linear;
        [Serialize, Description("Sweep progression of this potentiometer.")]
        public SweepType Sweep { get { return sweep; } set { sweep = value; NotifyChanged(nameof(Sweep)); } }

        private string group = "";
        [Serialize, Description("Potentiometer group this potentiometer is a section of.")]
        public string Group { get { return group; } set { group = value; NotifyChanged(nameof(Group)); } }

        public void ConnectTo(Node A, Node C, Node W)
        {
            Anode.ConnectTo(A);
            Cathode.ConnectTo(C);
            Wiper.ConnectTo(W);
        }

        public override void Analyze(Analysis Mna)
        {
            if (!Mna.IsLive(Name))
            {
                // Exactly what this component did before Stompbench milestone A4: the wiper is a
                // number, so both resistances are numbers and the algebra folds them exactly.
                Expression P = VariableResistor.AdjustWipe(wipe, sweep);
                Resistor.Analyze(Mna, Cathode, Wiper, Resistance * P);
                Resistor.Analyze(Mna, Anode, Wiper, Resistance * (1 - P));
                return;
            }

            // The wiper position is the thing a player turns while playing, so it stays symbolic
            // through the solve rather than being multiplied into the two resistances here.
            //
            // The two halves are declared as conductances rather than as one wiper position, and the
            // reason is measured rather than aesthetic: a wiper position appears in the equations as
            // 1/(R*P) and 1/(R*(1-P)), which puts it in a denominator and makes the row reduction
            // produce rational functions of it. Written this way each half is a plain coefficient.
            // On the Big Muff Pi, which has three of these, the difference is the milestone: see
            // Resistor.AnalyzeConductance and docs/stompbench-a4-result.md.
            //
            // Both halves come from one knob, so a control surface writes them together. They can
            // tear against each other for one buffer, which specification 3.3 allows explicitly, and
            // the visible consequence is that the two halves briefly describe wiper positions a
            // fraction apart on a 1.3 ms buffer.
            SweepType s = sweep;
            double resistance = (double)Resistance;

            Func<double, double> cathode = x => 1.0 / (resistance * VariableResistor.AdjustWipe(x, s));
            Func<double, double> anode = x => 1.0 / (resistance * (1.0 - VariableResistor.AdjustWipe(x, s)));

            LiveParameter.RangeOf(cathode, out double cathodeLow, out double cathodeHigh);
            LiveParameter.RangeOf(anode, out double anodeLow, out double anodeHigh);

            Expression GCathode = Mna.DeclareParameter(
                Name, "GCathode", wipe, cathode, cathodeLow, cathodeHigh);
            Expression GAnode = Mna.DeclareParameter(
                Name, "GAnode", wipe, anode, anodeLow, anodeHigh);

            Resistor.AnalyzeConductance(Mna, Name, Cathode, Wiper, GCathode);
            Resistor.AnalyzeConductance(Mna, Name, Anode, Wiper, GAnode);
        }

        protected internal override sealed void LayoutSymbol(SymbolLayout Sym)
        {
            Sym.InBounds(new Coord(-20, -20), new Coord(10, 20));

            Sym.AddTerminal(Anode, new Coord(-10, 20), new Coord(-10, 16));
            Sym.DrawPositive(EdgeType.Black, new Coord(-16, 16));

            Sym.AddTerminal(Cathode, new Coord(-10, -20), new Coord(-10, -16));
            Sym.DrawNegative(EdgeType.Black, new Coord(-16, -16));

            Sym.AddTerminal(Wiper, new Coord(10, 0));
            Sym.DrawArrow(EdgeType.Black, new Coord(10, 0), new Coord(-6, 0), 0.2);

            Resistor.Draw(Sym, -10, -16, 16, 7);

            Sym.DrawText(() => Sweep.GetCode()+Resistance.ToString(), new Coord(-17, 0), Alignment.Far, Alignment.Center);
            Sym.DrawText(() => Wipe.ToString("G3"), new Coord(-4, 4), Alignment.Near, Alignment.Near);
            Sym.DrawText(() => Name, new Coord(-4, -4), Alignment.Near, Alignment.Far);
        }
    }
}
