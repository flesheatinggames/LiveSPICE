using ComputerAlgebra;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Circuit
{
    /// <summary>
    /// A four-quadrant analog multiplier with differential inputs and a summing input.
    /// </summary>
    /// <remarks>
    /// <b>Read from Analog Devices' AD633 data sheet, revision E.</b> Every figure below is that
    /// document's, with the row of its specification table it comes from named beside it, and the one
    /// constant it does not publish says so.
    ///
    /// <b>The transfer function is printed at the head of the specification table and is the whole
    /// part:</b> W = (X1 − X2)(Y1 − Y2) / 10 V + Z. Two differential inputs are multiplied, the
    /// product is divided by a laser-trimmed ten-volt reference, and a third input is added at the
    /// output amplifier's summing node. Four-quadrant means both inputs may be either sign and the
    /// output follows, which is what makes it a ring modulator rather than an attenuator.
    ///
    /// <b>A macromodel rather than the die, on the op-amp's precedent.</b> The functional description
    /// says the part is "a translinear core, a buried Zener reference, and a unity gain connected
    /// output amplifier with an accessible summing node"; a translinear core is six transistors whose
    /// individual behaviour nothing outside the package can see, and the useful level of description
    /// is the equation the manufacturer trimmed the part to meet. <see cref="OpAmp"/> is the same
    /// decision made earlier here for the same reason.
    ///
    /// So the model is that equation behind one pole and a saturation, built the way OpAmp builds
    /// its: a current into an internal node with a resistor and a capacitor across it, clamped to the
    /// supplies through diodes and offset voltage sources, and brought out through an output
    /// resistance. What the pole is worth is stated below rather than assumed.
    ///
    /// <b>What this does not model, stated rather than left to be discovered.</b> The offsets and
    /// feedthroughs the specification table bounds — five millivolts of output offset, five of input
    /// offset, three tenths of a per cent of X feedthrough — are the residue of a laser trim, and
    /// modelling them would mean inventing a particular unit rather than describing the part. Total
    /// error is bounded at two per cent of full scale and nonlinearity at one, and this model is
    /// exact, so a render is the middle of the distribution rather than a worst case. The 0.8 µA
    /// input bias currents are modelled, because they are what decides whether a floating input
    /// drifts.
    /// </remarks>
    [Category("Op-Amps")]
    [DisplayName("AD633")]
    [DefaultProperty("ScaleFactor")]
    [Description("Four-quadrant analog multiplier: W = (X1-X2)(Y1-Y2)/10 V + Z.")]
    public class Ad633 : Component
    {
        // Pins 1 to 4 down one side of the eight-pin package and 5 to 8 up the other.
        private Terminal x1, x2, y1, y2, supplyMinus, z, w, supplyPlus;

        public override IEnumerable<Terminal> Terminals
        {
            get
            {
                yield return x1;
                yield return x2;
                yield return y1;
                yield return y2;
                yield return supplyMinus;
                yield return z;
                yield return w;
                yield return supplyPlus;
            }
        }

        [Browsable(false)] public Terminal X1 => x1;
        [Browsable(false)] public Terminal X2 => x2;
        [Browsable(false)] public Terminal Y1 => y1;
        [Browsable(false)] public Terminal Y2 => y2;
        [Browsable(false)] public Terminal SupplyMinus => supplyMinus;
        [Browsable(false)] public Terminal Z => z;
        [Browsable(false)] public Terminal W => w;
        [Browsable(false)] public Terminal SupplyPlus => supplyPlus;

        private Quantity scale = new Quantity(10m, Units.V);
        [Serialize, Description("The scale factor the product is divided by. Laser-trimmed to ten volts.")]
        public Quantity ScaleFactor { get { return scale; } set { if (scale.Set(value)) NotifyChanged(nameof(ScaleFactor)); } }

        private Quantity rin = new Quantity(1e7m, Units.Ohm);
        [Serialize, NoPreferredSeries, Description("Differential input resistance of each input pair.")]
        public Quantity Rin { get { return rin; } set { if (rin.Set(value)) NotifyChanged(nameof(Rin)); } }

        private Quantity rout = new Quantity(100m, Units.Ohm);
        [Serialize, NoPreferredSeries, Description("Output resistance. Not published; see the remarks.")]
        public Quantity Rout { get { return rout; } set { if (rout.Set(value)) NotifyChanged(nameof(Rout)); } }

        private Quantity ibias = new Quantity(0.8e-6m, Units.A);
        [Serialize, Description("Input bias current drawn by each of X1, X2, Y1, Y2 and Z.")]
        public Quantity IB { get { return ibias; } set { if (ibias.Set(value)) NotifyChanged(nameof(IB)); } }

        private Quantity bandwidth = new Quantity(1e6m, Units.Hz);
        [Serialize, Description("Small-signal bandwidth of the output amplifier.")]
        public Quantity Bandwidth { get { return bandwidth; } set { if (bandwidth.Set(value)) NotifyChanged(nameof(Bandwidth)); } }

        private Quantity headroom = new Quantity(4m, Units.V);
        [Serialize, Description("How far inside each supply rail the output can reach.")]
        public Quantity Headroom { get { return headroom; } set { if (headroom.Set(value)) NotifyChanged(nameof(Headroom)); } }

        public Ad633()
        {
            x1 = new Terminal(this, "X1");
            x2 = new Terminal(this, "X2");
            y1 = new Terminal(this, "Y1");
            y2 = new Terminal(this, "Y2");
            supplyMinus = new Terminal(this, "V-");
            z = new Terminal(this, "Z");
            w = new Terminal(this, "W");
            supplyPlus = new Terminal(this, "V+");
            Name = "U1";
        }

        public override void Analyze(Analysis Mna)
        {
            Node pole = new Node() { Name = "w" };
            Node reference = new Node() { Name = "wref" };
            Mna.PushContext(Name, pole, reference);

            // The two input pairs' differential resistance, and the bias current each pin draws.
            // Both are from the input amplifiers' rows of the specification table: 10 MΩ differential
            // and 0.8 µA typical, the latter bounded at 2 µA.
            Resistor.Analyze(Mna, X1, X2, Rin);
            Resistor.Analyze(Mna, Y1, Y2, Rin);
            foreach (Terminal input in new[] { x1, x2, y1, y2 })
                CurrentSource.Analyze(Mna, input, supplyMinus, IB);

            // <b>The summing input contributes only when it is wired, and nothing is assumed when it
            // is not.</b> The data sheet calls Z an optional summing input and the usual connection
            // grounds it; a model that read a floating pin's voltage would be reading an invented
            // node. Terminal.IsWired rather than IsConnected, because the build gives every
            // unconnected pin a node of its own before analysis begins.
            Expression summed = 0;
            if (z.IsWired)
            {
                CurrentSource.Analyze(Mna, z, supplyMinus, IB);
                summed = z.V;
            }

            Expression product =
                (X1.V - X2.V) * (Y1.V - Y2.V) / (Expression)ScaleFactor + summed;

            // <b>One pole, and what it is worth is not what it looks like.</b> The data sheet's
            // small-signal bandwidth is 1 MHz, and this project simulates at 48 kHz times eight,
            // which is 384 kHz — so the pole sits above the simulation's own Nyquist frequency and
            // what it does there is smooth rather than describe. It is here because the part has it
            // and because a circuit driving this output at hundreds of kilohertz would be wrong to
            // model as instantaneous, not because a render can see it. The same one-pole arrangement
            // OpAmp uses: a current into an internal node with a resistor across it, so the node
            // settles at the product, and a capacitor sized to put the corner at the bandwidth.
            Expression rp = 1000;
            // <b>Into the pole node, not out of it.</b> AddPassiveComponent's current leaves its
            // first node, so a source written the other way round would settle the node at minus the
            // product and the whole part would be an inverting multiplier. OpAmp writes its own
            // current source with the input difference negated for the same reason, which is the
            // same sign convention arrived at from the other side.
            CurrentSource.Analyze(Mna, reference, pole, product / rp);
            Resistor.Analyze(Mna, pole, reference, rp);
            Capacitor.Analyze(Mna, pole, reference, 1 / (2 * Math.PI * rp * (Expression)Bandwidth));
            Ground.Analyze(Mna, reference);

            // <b>Saturation four volts short of each rail.</b> The output section guarantees a swing
            // of at least ±11 V on the ±15 V supplies the table is taken at, and that difference is
            // the only headroom figure the document publishes — a minimum rather than a typical, so
            // a render is the pessimistic end of the distribution. Clamped through diodes behind
            // offset sources, which is how OpAmp clamps its own, and only when both supplies are
            // wired, because a clamp to a floating node is a subsystem nothing determines.
            if (supplyPlus.IsWired && supplyMinus.IsWired)
            {
                Node ceiling = new Node() { Name = "ceiling" };
                Node floor = new Node() { Name = "floor" };
                Mna.DeclNodes(ceiling, floor);

                VoltageSource.Analyze(Mna, supplyPlus, ceiling, (Expression)Headroom);
                Diode.Analyze(Mna, pole, ceiling, 8e-16, 1);

                VoltageSource.Analyze(Mna, supplyMinus, floor, -(Expression)Headroom);
                Diode.Analyze(Mna, floor, pole, 8e-16, 1);
            }

            // The output, as a source of the pole node's voltage behind a series resistance. The sign
            // is the one Analysis uses everywhere: the sum at a node is of currents leaving it, so a
            // source of voltage pole behind Rout draws (W − pole)/Rout out of W. OpAmp records what
            // getting this backwards cost, which was a negative output resistance nobody saw.
            //
            // <b>The output resistance is the one number here the data sheet does not publish.</b> It
            // gives an output swing and a short-circuit current of 30 to 40 mA and no resistance at
            // all, so a hundred ohms is the op-amp macromodel's own figure carried across rather than
            // a measurement. It matters only against a load: at the 100 kΩ this project's circuits
            // present it is a part in a thousand.
            Mna.AddTerminal(W, (W.V - pole.V) / Rout);

            Mna.PopContext();
        }

        /// <summary>
        /// The eight-pin package, read the way it is read on a breadboard.
        /// </summary>
        /// <remarks>
        /// Pins 1 to 4 down the left and 5 to 8 up the right, which is an eight-pin dual in-line
        /// package seen from above, for the reason the LM13700's symbol gives: this part's difficulty
        /// on a breadboard is getting eight pins into the right holes, and a symbol in package order
        /// is a wiring diagram where one grouped by function is a puzzle.
        /// </remarks>
        protected internal override void LayoutSymbol(SymbolLayout Sym)
        {
            var left = new(Terminal Pin, string Label)[]
            {
                (x1, "X1"), (x2, "X2"), (y1, "Y1"), (y2, "Y2"),
            };
            var right = new(Terminal Pin, string Label)[]
            {
                (supplyPlus, "V+"), (w, "W"), (z, "Z"), (supplyMinus, "V-"),
            };

            Sym.AddRectangle(EdgeType.Black, new Coord(-30, -40), new Coord(30, 40));

            for (int i = 0; i < left.Length; i++)
            {
                int y = 30 - i * 20;
                Sym.AddTerminal(left[i].Pin, new Coord(-50, y), new Coord(-30, y));
                string label = left[i].Label;
                Sym.DrawText(() => label, new Coord(-26, y), Alignment.Near, Alignment.Center, Size.Small);
            }
            for (int i = 0; i < right.Length; i++)
            {
                int y = 30 - i * 20;
                Sym.AddTerminal(right[i].Pin, new Coord(50, y), new Coord(30, y));
                string label = right[i].Label;
                Sym.DrawText(() => label, new Coord(26, y), Alignment.Far, Alignment.Center, Size.Small);
            }

            Sym.DrawText(() => Name, new Coord(0, 50), Alignment.Center, Alignment.Near);
            Sym.DrawText(() => PartNumber, new Coord(0, -50), Alignment.Center, Alignment.Far);
        }
    }
}
