using ComputerAlgebra;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Circuit
{
    /// <summary>
    /// A dual operational transconductance amplifier with linearizing diodes and output buffers.
    /// </summary>
    /// <remarks>
    /// <b>Read from Texas Instruments SNOSBW2F, November 1999, revised November 2015.</b> Every
    /// figure below is that document's, with the section or table it comes from named beside it, and
    /// the two that are derived rather than printed say what they were derived from and how.
    ///
    /// <b>What an operational transconductance amplifier is.</b> An ordinary amplifier's output is a
    /// voltage. This one's output is a current, and how much current a given input voltage produces
    /// is set by a third input — a current into a bias pin — over about six decades. That is what
    /// makes it the part a swept filter, a voltage-controlled amplifier and a ring modulator are
    /// built from: the thing being controlled is not a gain written in an equation but a current you
    /// can put a resistor on.
    ///
    /// <b>The transfer is a hyperbolic tangent because a bipolar differential pair is one.</b>
    /// Section 7.3.1 gives the ratio of the pair's two collector currents as the exponential of the
    /// differential input over kT/q, and the current mirror around them forces the two to sum to the
    /// bias current. Those two facts together are exactly the hyperbolic tangent: with I4 + I5 = IABC
    /// and I5/I4 = exp(V/VT), the difference I5 − I4 is IABC·tanh(V/(2·VT)). Substituting an
    /// algebraic curve of a similar shape, as milestone C10 correctly did for the vactrol's blending
    /// band, would put a deliberate error in the middle of the part's defining equation — the band
    /// there is an arbitrary smoothing width with no physics in it and this is not.
    ///
    /// <b>The linearizing diodes are not a refinement, and they are why every pin here is real.</b>
    /// The hyperbolic tangent is within a per cent of straight only for a few millivolts, so an OTA
    /// driven by an ordinary signal distorts grossly. Section 7.3.2 shows the cure: put the signal in
    /// as a current through the two diodes on the input pins, and because the diodes and the input
    /// transistors have identical geometries the logarithm the diodes impose is exactly the inverse
    /// of the hyperbolic tangent the pair applies. The datasheet's own words are that "no
    /// approximations have been made and there are no temperature-dependent terms."
    ///
    /// <em>That cancellation is not written anywhere in this file, and that is the point.</em> The
    /// diodes are real diodes on real pins through <see cref="Diode.Analyze"/>, and the differential
    /// pair takes the hyperbolic tangent of the voltage that actually appears across the input
    /// terminals. The identity falls out of the two: for a signal current Is into a diode pair biased
    /// at ID/2 each, the terminal voltage is VT·ln((ID/2 + Is)/(ID/2 − Is)), and tanh of half a
    /// logarithm of a ratio r is (r − 1)/(r + 1), which here is 2·Is/ID. So the output current is
    /// IABC·2·Is/ID, which is the datasheet's equation 7. A model that had wired the diodes without
    /// letting them set the input voltage would reproduce the transconductance and fail this, which
    /// is what <c>sbrender selftest lm13700</c> measures.
    ///
    /// <b>Both halves and the supplies are one component, because that is one chip in one socket.</b>
    /// Sixteen terminals, laid out as the sixteen-pin package's top view: pins 1 to 8 down the left
    /// and 9 to 16 up the right, so that the symbol and the part on the breadboard are read the same
    /// way round.
    ///
    /// <b>Three things a half contributes only when its pins are wired.</b> The linearizing diodes
    /// are contributed only when the diode bias pin is connected — the datasheet's own electrical
    /// characteristics are taken with "pins 2 and 15 open" — the output buffer only when its input is
    /// connected, and the output's compliance limit only when both supplies are. That follows
    /// <see cref="OpAmp"/>, which contributes its saturation only when its rails are connected, and
    /// it is what keeps a four-stage phase shifter's Newton system a size that can be played rather
    /// than only rendered.
    ///
    /// <b>What this does not model, stated rather than left to be discovered.</b> There is no
    /// internal pole: the datasheet's open-loop bandwidth is 2 MHz, which is five times the 384 kHz
    /// this project simulates at, so a pole there is not representable and a pole somewhere else
    /// would be an invention. The 50 V/µs slew rate is the same figure seen from the other side. The
    /// output current is drawn entirely from the positive supply where a real push-pull output takes
    /// its sourcing half from V+ and its sinking half from V−; that changes which rail carries the
    /// signal current and nothing a load can see, because both pins are rails. Crosstalk between the
    /// halves, 100 dB referred to input, is not modelled, and neither is noise.
    /// </remarks>
    [Category("Op-Amps")]
    [DisplayName("LM13700")]
    [DefaultProperty("VT")]
    [Description("Dual operational transconductance amplifier with linearizing diodes and buffers.")]
    public class Lm13700 : Component
    {
        // Pins 1 to 8 down the left of the package, 9 to 16 up the right.
        private Terminal biasA, diodeA, positiveA, negativeA, outputA, supplyMinus, bufferInA, bufferOutA;
        private Terminal bufferOutB, bufferInB, supplyPlus, outputB, negativeB, positiveB, diodeB, biasB;

        public override IEnumerable<Terminal> Terminals
        {
            get
            {
                yield return biasA;
                yield return diodeA;
                yield return positiveA;
                yield return negativeA;
                yield return outputA;
                yield return supplyMinus;
                yield return bufferInA;
                yield return bufferOutA;
                yield return bufferOutB;
                yield return bufferInB;
                yield return supplyPlus;
                yield return outputB;
                yield return negativeB;
                yield return positiveB;
                yield return diodeB;
                yield return biasB;
            }
        }

        [Browsable(false)] public Terminal AmplifierBiasA => biasA;
        [Browsable(false)] public Terminal DiodeBiasA => diodeA;
        [Browsable(false)] public Terminal InputPlusA => positiveA;
        [Browsable(false)] public Terminal InputMinusA => negativeA;
        [Browsable(false)] public Terminal OutputA => outputA;
        [Browsable(false)] public Terminal SupplyMinus => supplyMinus;
        [Browsable(false)] public Terminal BufferInA => bufferInA;
        [Browsable(false)] public Terminal BufferOutA => bufferOutA;
        [Browsable(false)] public Terminal BufferOutB => bufferOutB;
        [Browsable(false)] public Terminal BufferInB => bufferInB;
        [Browsable(false)] public Terminal SupplyPlus => supplyPlus;
        [Browsable(false)] public Terminal OutputB => outputB;
        [Browsable(false)] public Terminal InputMinusB => negativeB;
        [Browsable(false)] public Terminal InputPlusB => positiveB;
        [Browsable(false)] public Terminal DiodeBiasB => diodeB;
        [Browsable(false)] public Terminal AmplifierBiasB => biasB;

        private Quantity vt = new Quantity(0.026m, Units.V);
        [Serialize, Description("Thermal voltage kT/q. The datasheet's own figure, 26 mV at 25 degrees.")]
        public Quantity VT { get { return vt; } set { if (vt.Set(value)) NotifyChanged(nameof(VT)); } }

        private Quantity _is = new Quantity(2.03e-15m, Units.A);
        [Serialize, Description("Junction saturation current, shared by every diode and the input pair.")]
        public Quantity IS { get { return _is; } set { if (_is.Set(value)) NotifyChanged(nameof(IS)); } }

        private Quantity rin = new Quantity(26000m, Units.Ohm);
        [Serialize, NoPreferredSeries, Description("Differential input resistance.")]
        public Quantity Rin { get { return rin; } set { if (rin.Set(value)) NotifyChanged(nameof(Rin)); } }

        private Quantity ibias = new Quantity(0.4e-6m, Units.A);
        [Serialize, Description("Input bias current drawn by each amplifier input.")]
        public Quantity IB { get { return ibias; } set { if (ibias.Set(value)) NotifyChanged(nameof(IB)); } }

        private Quantity headroom = new Quantity(0.8m, Units.V);
        [Serialize, Description("How far inside each supply rail the unbuffered output can reach.")]
        public Quantity Headroom { get { return headroom; } set { if (headroom.Set(value)) NotifyChanged(nameof(Headroom)); } }

        private double buffergain = 5500;
        [Serialize, Description("Darlington current gain of each output buffer.")]
        public double BufferGain { get { return buffergain; } set { buffergain = value; NotifyChanged(nameof(BufferGain)); } }

        public Lm13700()
        {
            biasA = new Terminal(this, "IABC A");
            diodeA = new Terminal(this, "Dbias A");
            positiveA = new Terminal(this, "In+ A");
            negativeA = new Terminal(this, "In- A");
            outputA = new Terminal(this, "Out A");
            supplyMinus = new Terminal(this, "V-");
            bufferInA = new Terminal(this, "Buf in A");
            bufferOutA = new Terminal(this, "Buf out A");
            bufferOutB = new Terminal(this, "Buf out B");
            bufferInB = new Terminal(this, "Buf in B");
            supplyPlus = new Terminal(this, "V+");
            outputB = new Terminal(this, "Out B");
            negativeB = new Terminal(this, "In- B");
            positiveB = new Terminal(this, "In+ B");
            diodeB = new Terminal(this, "Dbias B");
            biasB = new Terminal(this, "IABC B");
            // <b>The bin names a kind of device and the drawing names the chip.</b> Every entry in
            // that bin names a kind — a resistor, a diode, an op-amp — so this one is "Transconductance
            // amplifier", and the part number is where the vactrol already keeps its own. It matters
            // more here than there: for a part like this the pinout is the model, and no value turns
            // an LM13700 into a CA3080, whose pins are different.
            //
            // LM13700 rather than a family name, because the LM13600 shares this pinout and differs
            // in one respect its datasheet names — its output buffers' bias currents depend on the
            // amplifier bias current where these do not — which this model does not carry. Claiming
            // to be both would be claiming a difference it cannot tell.
            PartNumber = "LM13700";
            Name = "U1";
        }

        /// <summary>
        /// The transconductance a bias current buys, which is the whole reason for the part.
        /// </summary>
        /// <remarks>
        /// The differential pair's slope at the origin: differentiating IABC·tanh(V/(2·VT)) at V = 0
        /// gives IABC/(2·VT). At the datasheet's own test bias of 500 µA with its own 26 mV thermal
        /// voltage this is 9615 µS, against a typical of 9600 µS and a guaranteed band of 6700 to
        /// 13000 µS in section 6.4 — which is the arithmetic that says the two agree rather than
        /// merely resemble each other.
        /// </remarks>
        public static double TransconductanceOf(double biasCurrent, double thermalVoltage) =>
            biasCurrent / (2 * thermalVoltage);

        /// <summary>
        /// What to tell <see cref="Diode.Analyze"/> so that its junction uses this chip's thermal
        /// voltage rather than the library's.
        /// </summary>
        /// <remarks>
        /// <b>Every junction on this die is at the same temperature as the differential pair, and the
        /// linearization depends on exactly that.</b> Section 7.3.2's cancellation — the logarithm
        /// the diodes impose being the inverse of the hyperbolic tangent the pair applies — holds
        /// only while both use the same kT/q. The datasheet's own figure is 26 mV and
        /// <c>Component.VT</c> is 25.35 mV, a difference of two and a half per cent, and left alone
        /// it would leave a residual curvature in a part whose selling point is that it has none.
        ///
        /// <c>Diode.Analyze</c> divides by n·VT with the library's VT, so passing the ratio of the
        /// two as the emission coefficient makes it divide by this component's instead. It is the
        /// same equation, written through the only opening the shared helper has, and it keeps the
        /// numerical straightening in <c>Component.LinExpm1</c> that writing the exponential here by
        /// hand would give up.
        /// </remarks>
        private Expression Junctions => (Expression)VT / (Expression)Component.VT;

        public override void Analyze(Analysis Mna)
        {
            Half(Mna, "A", biasA, diodeA, positiveA, negativeA, outputA, bufferInA, bufferOutA);
            Half(Mna, "B", biasB, diodeB, positiveB, negativeB, outputB, bufferInB, bufferOutB);
        }

        /// <summary>One of the two amplifiers, which are identical and share only the supplies.</summary>
        private void Half(
            Analysis Mna, string half,
            Terminal bias, Terminal diodeBias, Terminal plus, Terminal minus, Terminal output,
            Terminal bufferIn, Terminal bufferOut)
        {
            string name = Name + half;

            // <b>The bias pin is two junctions above the negative supply, and that is what sets the
            // gain.</b> Section 8.2.2: "The Bias Input pins (pins 1 or 16), are 2 diode drops above
            // the negative supply, and therefore VBIAS = 2(VBE) + V-". Two identical junctions in
            // series have the same current-voltage relation as one junction of twice the emission
            // coefficient, so this is one Diode.Analyze with n = 2 rather than two with n = 1 — the
            // same equation with one unknown fewer, which matters because a four-stage phase shifter
            // holds four of these halves.
            //
            // The current through it is IABC, and it is a branch current the solver already computes
            // rather than a model parameter. That is what makes the interesting control free: an
            // external resistor from the bias pin to wherever the control voltage is decides the
            // transconductance, and it is a resistor somebody has to buy.
            Expression iabc = Diode.Analyze(Mna, name + "bias", bias, supplyMinus, IS, 2 * Junctions);

            // The input pair's own resistance and bias current, both from section 6.4. The bias
            // current is drawn out of each input into the negative supply, which is where a real
            // NPN pair's base current goes.
            Resistor.Analyze(Mna, name + "rin", plus, minus, Rin);
            
            

            // <b>The linearizing diodes, contributed only when their bias pin is wired.</b> Section
            // 6.4's electrical characteristics are taken with "pins 2 and 15 open", which is a real
            // way to use the part and the one where the transfer is the bare hyperbolic tangent. When
            // the pin is wired, these two carry the current the signal is injected as, and the
            // logarithm they impose on the input terminals is what the pair's hyperbolic tangent
            // undoes. Nothing here arranges that cancellation; it is a consequence of both being
            // written honestly.
            if (diodeBias.IsWired)
            {
                Diode.Analyze(Mna, name + "dp", diodeBias, plus, IS, Junctions);
                Diode.Analyze(Mna, name + "dm", diodeBias, minus, IS, Junctions);
            }

            // The differential pair. Section 7.3.1's two facts — the ratio of the collector currents
            // is the exponential of the input over kT/q, and the mirror forces them to sum to IABC —
            // are together this one line.
            Expression differential = plus.V - minus.V;
            Expression iout = iabc * Call.Tanh(differential / (2 * (Expression)VT));

            // <b>The output cannot reach its rail, and this is how far short it stops.</b> Section
            // 6.4 gives a peak output voltage of 14.2 V positive and -14.4 V negative on 15 V
            // supplies, which is about eight tenths of a volt inside each. A current source with no
            // limit would drive a light load straight past the supply, which no socketed chip does.
            //
            // The limit is on the current rather than on the voltage, and it costs no unknown. As the
            // output approaches a rail the mirror runs out of headroom and stops delivering; the node
            // then settles wherever the load holds it, which is the compliance limit. Written as a
            // smooth window rather than a pair of clamp diodes because two diodes per half is four
            // more Newton unknowns per chip, and because a smooth factor has no corner for the
            // iteration to chatter across — the same reasoning the vactrol's blending band records.
            if (supplyPlus.IsWired && supplyMinus.IsWired)
            {
                Expression room = Headroom;
                Expression above = supplyPlus.V - room - output.V;
                Expression below = output.V - (supplyMinus.V + room);

                // A smooth zero-to-one window, one volt wide, that is one when there is room and zero
                // when there is not. The sourcing half of the output current is limited by the
                // positive rail and the sinking half by the negative one, so the two are separated
                // the way Vactrol.cs separates a diode's forward current from its reverse leakage.
                Expression sourcing = (iout + Call.Sqrt(iout * iout + Floor * Floor)) / 2;
                Expression sinking = iout - sourcing;
                iout = sourcing * Window(above) + sinking * Window(below);
            }

            iout = Mna.AddUnknownEqualTo("i" + name + "o", iout);
            CurrentSource.Analyze(Mna, supplyPlus, output, iout);

            // <b>The output buffer, which is a Darlington emitter follower and not a voltage
            // follower.</b> Section 7.4.1: "a Darlington pair transistor that can drive up to 20mA".
            // Two base-emitter junctions in series carry the base current from the buffer input to
            // the buffer output, which is where the two diode drops between them come from, and the
            // emitter current is the current gain times that, sourced from the positive supply. So it
            // sources and does not sink, and a circuit using one needs a pull-down to V- — which is
            // exactly what section 6.4's own footnote specifies for its test, a 5 kΩ resistor from
            // the buffer output to the negative supply.
            if (bufferIn.IsWired && bufferOut.IsWired)
            {
                Expression ib = Diode.Analyze(Mna, name + "buf", bufferIn, bufferOut, IS, 2 * Junctions);
                CurrentSource.Analyze(Mna, supplyPlus, bufferOut, ib * BufferGain);
            }
        }

        /// <summary>
        /// The floor under the smooth split of a current into its sourcing and sinking halves.
        /// </summary>
        /// <remarks>
        /// A picoamp: small enough to be far below any current this part carries — the smallest bias
        /// current section 6.4 specifies is 5 µA, seven decades above it — and large enough that the
        /// square root's slope stays finite where the output current passes through zero, which is
        /// where a guitar signal spends most of its time.
        /// </remarks>
        private static readonly Expression Floor = 1e-12;

        /// <summary>
        /// One while there is headroom, nought once there is none, with no corner between.
        /// </summary>
        /// <remarks>
        /// x/sqrt(x² + w²) taken to its positive half, which is the algebraic step the vactrol uses
        /// and for the same reason: a conditional would be differentiated correctly and would still
        /// chatter, because the two branches disagree about the slope exactly where the iteration
        /// keeps landing. The width is a volt, which is a little over the eight tenths the datasheet
        /// leaves, so the output softens as it approaches the rail rather than hitting a wall.
        /// </remarks>
        private Expression Window(Expression room)
        {
            // <b>A fifth of the headroom, and the width is not free.</b> This step reaches one only
            // in the limit, so it is short of one by about half the square of the ratio of the width
            // to the room available — with a volt of width and fourteen volts of room that is an
            // eighth of a per cent, which would appear as an eighth of a per cent missing from the
            // transconductance everywhere, in a model whose whole first claim is that the
            // transconductance is the bias current over twice the thermal voltage. At a fifth of the
            // eight-tenths of a volt the datasheet leaves, the same figure is six parts in a hundred
            // thousand, and the knee is still a third of a volt wide, which is smooth enough that
            // Newton walks across it rather than chattering on it.
            Expression width = (Expression)Headroom / 5;
            return (1 + room / Call.Sqrt(room * room + width * width)) / 2;
        }

        /// <summary>
        /// The sixteen-pin package, read the way it is read on a breadboard.
        /// </summary>
        /// <remarks>
        /// <b>A rectangle with the pins in package order rather than a pair of amplifier triangles.</b>
        /// Two triangles would be the schematic convention and would be the wrong picture here: this
        /// part's whole difficulty on a breadboard is that sixteen pins have to go in the right holes,
        /// and a symbol whose pins are in package order is a wiring diagram where one grouped by
        /// function is a puzzle. Pins 1 to 8 run down the left and 9 to 16 up the right, which is a
        /// sixteen-pin dual in-line package seen from above.
        ///
        /// Two hundred units tall, which is taller than anything else in this library. Sixteen
        /// terminals at the twenty-unit spacing the canvas snaps to cannot be closer together, and a
        /// name beside each is what makes the symbol worth having.
        /// </remarks>
        protected internal override void LayoutSymbol(SymbolLayout Sym)
        {
            var left = new(Terminal Pin, string Label)[]
            {
                (biasA, "IABC A"), (diodeA, "Dbias A"), (positiveA, "In+ A"), (negativeA, "In- A"),
                (outputA, "Out A"), (supplyMinus, "V-"), (bufferInA, "Buf in A"), (bufferOutA, "Buf out A"),
            };
            var right = new(Terminal Pin, string Label)[]
            {
                (biasB, "IABC B"), (diodeB, "Dbias B"), (positiveB, "In+ B"), (negativeB, "In- B"),
                (outputB, "Out B"), (supplyPlus, "V+"), (bufferInB, "Buf in B"), (bufferOutB, "Buf out B"),
            };

            Sym.AddRectangle(EdgeType.Black, new Coord(-50, -80), new Coord(50, 80));

            for (int i = 0; i < left.Length; i++)
            {
                int y = 70 - i * 20;
                Sym.AddTerminal(left[i].Pin, new Coord(-70, y), new Coord(-50, y));
                string label = left[i].Label;
                Sym.DrawText(() => label, new Coord(-46, y), Alignment.Near, Alignment.Center, Size.Small);
            }
            for (int i = 0; i < right.Length; i++)
            {
                int y = 70 - i * 20;
                Sym.AddTerminal(right[i].Pin, new Coord(70, y), new Coord(50, y));
                string label = right[i].Label;
                Sym.DrawText(() => label, new Coord(46, y), Alignment.Far, Alignment.Center, Size.Small);
            }

            Sym.DrawText(() => Name, new Coord(0, 90), Alignment.Center, Alignment.Near);
            Sym.DrawText(() => PartNumber, new Coord(0, -90), Alignment.Center, Alignment.Far);
        }
    }
}
