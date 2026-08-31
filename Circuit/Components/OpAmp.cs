using ComputerAlgebra;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Circuit
{
    /// <summary>
    /// Implements an op-amp model based on techniques from the following sources:
    /// http://www.ecircuitcenter.com/OpModels/OpampModels.htm
    /// http://qucs.sourceforge.net/docs/opamp.pdf
    /// </summary>
    [Category("Op-Amps")]
    [DisplayName("Op-Amp")]
    [Description("Generic op-amp model. Model includes a single pole frequency response and saturation.")]
    public class OpAmp : IdealOpAmp
    {
        protected Terminal vcc, vee;

        public override IEnumerable<Terminal> Terminals { get { return base.Terminals.Append(vcc, vee); } }

        public OpAmp()
        {
            vcc = new Terminal(this, "Vcc+");
            vee = new Terminal(this, "Vcc-");
        }

        protected Quantity rin = new Quantity(1e6, Units.Ohm);
        [Serialize, Description("Input resistance.")]
        public Quantity Rin { get { return rin; } set { if (rin.Set(value)) NotifyChanged(nameof(Rin)); } }

        protected Quantity rout = new Quantity(100m, Units.Ohm);
        [Serialize, Description("Output resistance.")]
        public Quantity Rout { get { return rout; } set { if (rout.Set(value)) NotifyChanged(nameof(Rout)); } }

        protected Quantity gain = new Quantity(1e6m, Units.None);
        [Serialize, Description("Open-loop gain.")]
        public Quantity Aol { get { return gain; } set { if (gain.Set(value)) NotifyChanged(nameof(Aol)); } }

        protected Quantity gbp = new Quantity(1e6m, Units.Hz);
        [Serialize, Description("Gain-bandwidth product, equivalent to the unity gain bandwidth.")]
        public Quantity GBP { get { return gbp; } set { if (gbp.Set(value)) NotifyChanged(nameof(GBP)); } }

        public override void Analyze(Analysis Mna)
        {
            // Implement Voltage gain.
            Node pp1 = new Node() { Name = "pp1" };
            Node np1 = new Node() { Name = "np1" };
            Mna.PushContext(Name, pp1, np1);

            // The input terminals are connected by a resistor Rin.
            Resistor.Analyze(Mna, Negative, Positive, Rin);
            Expression VRin = Negative.V - Positive.V;

            Expression Rp1 = 1000;

            CurrentSource.Analyze(Mna, pp1, np1, VRin * Aol / Rp1);
            Resistor.Analyze(Mna, pp1, np1, Rp1);
            Capacitor.Analyze(Mna, pp1, np1, 1 / (2 * Math.PI * Rp1 * GBP / Aol));
            Ground.Analyze(Mna, np1);

            // <b>Wired rather than connected, which are different questions.</b> Schematic.Build
            // gives every unconnected terminal a node of its own before analysis, so IsConnected is
            // true for every pin in the drawing and this read as "always". An op-amp drawn without
            // its rails therefore got a limiter clamping its pole node to two floating nodes, which
            // is a subsystem the steady-state solve cannot determine. See Terminal.IsWired.
            if (vcc.IsWired && vee.IsWired)
            {
                Node ncc = new Node() { Name = "ncc" };
                Node nee = new Node() { Name = "nee" };
                Mna.DeclNodes(ncc, nee);

                VoltageSource.Analyze(Mna, vcc, ncc, 2);
                Diode.Analyze(Mna, pp1, ncc, 8e-16, 1);

                VoltageSource.Analyze(Mna, vee, nee, -2);
                Diode.Analyze(Mna, nee, pp1, 8e-16, 1);
            }

            // Output current is buffered: a Thevenin source of open-circuit voltage pp1 behind a
            // series Rout, with the buffer rather than the pole node supplying the current, which is
            // why nothing is added back at pp1.
            //
            // The sign is the one Analysis uses everywhere else. AddTerminal adds its current to the
            // node's Kirchhoff sum, and that sum is of currents *leaving* the node: Resistor.Analyze
            // adds (Anode.V - Cathode.V)/R at the anode, and AddPassiveComponent negates it at the
            // cathode. A source of voltage pp1 behind Rout therefore draws (Out - pp1)/Rout out of
            // Out. This stood the other way round until Stompbench's referee measured it, which made
            // the stage present a *negative* output resistance of -Rout. With Aol = 1e6 the feedback
            // swamps it at DC and in any level measurement, so it hid as a phase error of about a
            // part in ten thousand at 1 kHz; the clipping in a distortion circuit then converts that
            // into a shift in when the output flips, worth 15 per cent of peak on the diode clipper.
            // See docs/stompbench-a2.75-result.md in the Stompbench repository.
            Mna.AddTerminal(Out, (Out.V - pp1.V) / Rout);

            Mna.PopContext();
        }

        public static void LayoutSymbol(SymbolLayout Sym, Terminal p, Terminal n, Terminal o, Terminal vp, Terminal vn, Func<string> Name, Func<string> Part)
        {
            IdealOpAmp.LayoutSymbol(Sym, p, n, o, Name);

            Sym.AddTerminal(vn, new Coord(0, -20), new Coord(0, -10));
            Sym.DrawNegative(EdgeType.Black, new Coord(5, -13));

            Sym.AddTerminal(vp, new Coord(0, 20), new Coord(0, 10));
            Sym.DrawPositive(EdgeType.Black, new Coord(5, 13));

            if (Part != null)
                Sym.DrawText(Part, new Coord(12, 4), Alignment.Near, Alignment.Near);
        }

        protected internal override void LayoutSymbol(SymbolLayout Sym) { LayoutSymbol(Sym, Positive, Negative, Out, vcc, vee, () => Name, () => PartNumber); }
    }
}
