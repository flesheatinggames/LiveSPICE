using ComputerAlgebra;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Circuit
{
    /// <summary>
    /// A photoFET optocoupler: a light-emitting diode bonded to a photosensitive channel.
    /// </summary>
    /// <remarks>
    /// <b>Read from Fairchild's H11F1/H11F2/H11F3 data sheet of 19 March 2003.</b> Every figure below
    /// is that document's, with the row or figure it comes from named beside it, and the two that are
    /// derived rather than printed say what they were derived from.
    ///
    /// <b>The same two isolated ports a vactrol has, and a completely different device between
    /// them.</b> A vactrol's photoresistor lags the light by milliseconds going up and tens of
    /// milliseconds coming down, and that asymmetry is why an optical compressor sounds as it does.
    /// This part's detector is a field-effect channel rather than a photoresistor: the datasheet's
    /// description calls it "a symmetrical bilateral silicon photo-detector" that "performs like an
    /// ideal isolated FET designed for distortion-free control of low level AC and DC analog
    /// signals", it switches in tens of microseconds, and it switches at the same speed in both
    /// directions. Put one where a vactrol was and the circuit stops being a compressor and becomes
    /// something that follows the waveform, which is the contrast
    /// <c>sbrender selftest h11f1</c> measures.
    ///
    /// <b>The channel is the square law's linear region, and it is the JFET's own expression.</b>
    /// <see cref="JunctionFieldEffectTransistor.ChannelCurrent"/> is called rather than copied, so
    /// the correction milestone C11 made to that equation — a subtraction of the drain-source voltage
    /// where a subtraction of one volt had stood — is shared rather than reproduced. The light sets
    /// the overdrive: the conductance at zero volts across the channel is what the datasheet's
    /// Figure 1 plots, and the overdrive that produces it is that conductance over twice the
    /// transconductance coefficient.
    ///
    /// <b>Figure 1 is a straight line on logarithmic axes and its slope is minus one.</b> The plot is
    /// normalised resistance against input current, running from a normalised ten at one milliamp to
    /// a normalised one tenth at a hundred, which is two decades of resistance across two decades of
    /// current. So the channel resistance is inversely proportional to the light, and
    /// <see cref="Gamma"/> is one. That exponent is the model's central claim and it is the reason
    /// this part gets a bench measurement rather than only a datasheet check: the document publishes
    /// one on-resistance figure, a maximum of 200 Ω at 16 mA, with no typical and no tolerance.
    ///
    /// <b>What the two derived constants were derived from.</b> The pinch-off voltage is from the
    /// non-linearity row: 0.1 per cent at 16 mA with 25 µA RMS through the channel, which is 5 mV
    /// RMS or 7.1 mV peak across 200 Ω. The square law's fractional departure from a straight line is
    /// the drain-source voltage over twice the pinch-off voltage, so 0.1 per cent at 7.07 mV gives
    /// 3.54 V. The LED's saturation current is from the one forward-voltage point the document
    /// publishes, 1.3 V typical at 16 mA: one point cannot fix both a saturation current and an
    /// emission coefficient, so the coefficient is taken as two, which is an ordinary light-emitting
    /// diode and gives 117 mV per decade, and the saturation current is solved from the point. That
    /// choice moves the operating point a little and cannot move the resistance law at all, because
    /// the law is written against the LED <em>current</em>, which is what Figure 1 plots and what an
    /// external resistor sets.
    ///
    /// <b>It has a lag, and the milestone's plan said it should not.</b> The plan's reason was that
    /// the part follows the light in microseconds where a vactrol takes milliseconds, and that is
    /// exactly what is modelled: the transfer characteristics give a turn-on and a turn-off time both
    /// bounded at 25 µs, which is about ten of this project's timesteps and so is representable
    /// rather than instantaneous. Modelling it makes the response time a published number this model
    /// can be measured against instead of an assertion, and it makes the two optocouplers
    /// structurally alike so that the only thing separating them is their constants — which is what
    /// makes the compressor comparison a comparison. The two times are equal, and that symmetry is
    /// itself the contrast with the vactrol.
    /// </remarks>
    [Category("Optical")]
    [DisplayName("PhotoFET")]
    [DefaultProperty("Ron")]
    [Description("Optocoupler: an LED bonded to a field-effect channel, whose resistance follows the light in microseconds.")]
    public class PhotoFet : Component
    {
        private Terminal anode, cathode, channel1, channel2;

        public override IEnumerable<Terminal> Terminals
        {
            get
            {
                yield return anode;
                yield return cathode;
                yield return channel1;
                yield return channel2;
            }
        }

        [Browsable(false)] public Terminal Anode => anode;
        [Browsable(false)] public Terminal Cathode => cathode;
        [Browsable(false)] public Terminal Channel1 => channel1;
        [Browsable(false)] public Terminal Channel2 => channel2;

        private Quantity ron = new Quantity(200m, Units.Ohm);
        [Serialize, NoPreferredSeries, Description("Channel resistance at the reference LED current.")]
        public Quantity Ron { get { return ron; } set { if (ron.Set(value)) NotifyChanged(nameof(Ron)); } }

        private Quantity roff = new Quantity(3e8m, Units.Ohm);
        [Serialize, NoPreferredSeries, Description("Channel resistance with the LED dark.")]
        public Quantity Roff { get { return roff; } set { if (roff.Set(value)) NotifyChanged(nameof(Roff)); } }

        private Quantity iref = new Quantity(16e-3m, Units.A);
        [Serialize, Description("The LED current Ron is quoted at.")]
        public Quantity IRef { get { return iref; } set { if (iref.Set(value)) NotifyChanged(nameof(IRef)); } }

        private double gamma = 1.0;
        [Serialize, Description("How steeply conductance follows light. One, from Figure 1's slope.")]
        public double Gamma { get { return gamma; } set { gamma = value; NotifyChanged(nameof(Gamma)); } }

        private Quantity pinch = new Quantity(3.5355m, Units.V);
        [Serialize, Description("Where the channel pinches off, which is what bounds its linearity.")]
        public Quantity PinchOff { get { return pinch; } set { if (pinch.Set(value)) NotifyChanged(nameof(PinchOff)); } }

        private Quantity response = new Quantity(25e-6m, Units.s);
        [Serialize, Description("Time constant of the channel's response to the light, the same in both directions.")]
        public Quantity Response { get { return response; } set { if (response.Set(value)) NotifyChanged(nameof(Response)); } }

        private Quantity _is = new Quantity(1.17e-13m, Units.A);
        [Serialize, Description("LED saturation current.")]
        public Quantity IS { get { return _is; } set { if (_is.Set(value)) NotifyChanged(nameof(IS)); } }

        private Quantity _n = new Quantity(2m, Units.None);
        [Serialize, Description("LED emission coefficient.")]
        public Quantity n { get { return _n; } set { if (_n.Set(value)) NotifyChanged(nameof(n)); } }

        public PhotoFet()
        {
            anode = new Terminal(this, "A");
            cathode = new Terminal(this, "K");
            channel1 = new Terminal(this, "1");
            channel2 = new Terminal(this, "2");
            Name = "U1";
        }

        /// <summary>
        /// The ranges the four live figures sweep, and why each is logarithmic.
        /// </summary>
        /// <remarks>
        /// The same four questions the vactrol's live figures answer, asked of a part whose answers
        /// are three orders of magnitude away: an H11F1 is bounded at 200 Ω where a VTL5C3 is 4 kΩ,
        /// and it responds in tens of microseconds where the vactrol takes tens of milliseconds. The
        /// H11F2 and H11F3 in the same datasheet are 330 Ω and 470 Ω, so the lit range spans them
        /// with room either side.
        ///
        /// Every one is logarithmic, for the reason the diode's saturation current is: a resistance
        /// and a time are ratio quantities, where what matters is how many times larger one is than
        /// another.
        /// </remarks>
        public const double LiveRonMinimum = 20;
        public const double LiveRonMaximum = 2000;
        public const double LiveRoffMinimum = 1e6;
        public const double LiveRoffMaximum = 1e9;
        public const double LiveResponseMinimum = 1e-6;
        public const double LiveResponseMaximum = 1e-3;
        public const double LivePinchMinimum = 0.5;
        public const double LivePinchMaximum = 20;

        public override void Analyze(Analysis Mna)
        {
            bool live = Mna.IsNamedLive(Name);
            Expression ron = Mna.DeclareParameter(
                Name, "Ron", Vactrol.PositionOf((double)Ron, LiveRonMinimum, LiveRonMaximum),
                p => Vactrol.ValueAt(p, LiveRonMinimum, LiveRonMaximum),
                LiveRonMinimum, LiveRonMaximum, Value: (double)Ron, Live: live);
            Expression roff = Mna.DeclareParameter(
                Name, "Roff", Vactrol.PositionOf((double)Roff, LiveRoffMinimum, LiveRoffMaximum),
                p => Vactrol.ValueAt(p, LiveRoffMinimum, LiveRoffMaximum),
                LiveRoffMinimum, LiveRoffMaximum, Value: (double)Roff, Live: live);
            Expression settle = Mna.DeclareParameter(
                Name, "Response", Vactrol.PositionOf((double)Response, LiveResponseMinimum, LiveResponseMaximum),
                p => Vactrol.ValueAt(p, LiveResponseMinimum, LiveResponseMaximum),
                LiveResponseMinimum, LiveResponseMaximum, Value: (double)Response, Live: live);
            Expression pinchOff = Mna.DeclareParameter(
                Name, "PinchOff", Vactrol.PositionOf((double)PinchOff, LivePinchMinimum, LivePinchMaximum),
                p => Vactrol.ValueAt(p, LivePinchMinimum, LivePinchMaximum),
                LivePinchMinimum, LivePinchMaximum, Value: (double)PinchOff, Live: live);

            // The LED half is an ordinary diode, and its current is the only thing that crosses.
            Expression iLed = Diode.Analyze(Mna, Name + "d", Anode, Cathode, IS, n);

            // <b>A smooth positive part of the LED current</b>, for the reason Vactrol.cs gives: the
            // power below would otherwise see a negative base when the diode is reverse biased, and
            // a fractional power of a negative number is not a number. A clamp with Max would fix
            // that and put a corner at zero for Newton to trip over; this has none.
            Expression floor = (Expression)IRef * 1e-9;
            Expression lit = (Call.Sqrt(iLed * iLed + floor * floor) + iLed) / 2;

            // What the channel's conductance would be for the light it is seeing now. Figure 1's
            // straight line on logarithmic axes, with the dark conductance underneath it.
            Expression target = 1 / roff + Binary.Power(lit / IRef, Gamma) / ron;
            target = Mna.AddUnknownEqualTo("G" + Name + "t", target);

            // The state, which is where the channel actually is. One time constant rather than two,
            // because the datasheet gives one figure for turning on and the same figure for turning
            // off — and that symmetry is the whole difference from a vactrol, which needs two.
            Expression g = Mna.AddUnknown("G" + Name);
            Mna.AddEquation(D(g, t), (target - g) / settle);

            // The channel: the square law's linear region, with the light setting the overdrive. The
            // conductance at zero volts across the channel is 2·Beta·overdrive, so writing the
            // overdrive as the pinch-off voltage makes Beta the conductance over twice it, and the
            // shared expression then reproduces the datasheet's own 0.1 per cent non-linearity.
            //
            // No conduction condition and no channel-length modulation: a photoFET in the dark does
            // not cut off, it becomes three hundred megohms, which the dark conductance above
            // already says; and the datasheet publishes no output-conductance figure in saturation.
            Expression vds = Channel1.V - Channel2.V;
            Expression absVds = Call.Abs(vds);
            Expression beta = g / (2 * pinchOff);
            Expression current = Call.Sign(vds) *
                JunctionFieldEffectTransistor.ChannelCurrent(beta, pinchOff, absVds, 0);
            Mna.AddPassiveComponent(Channel1, Channel2, current);
        }

        /// <summary>
        /// The package, with a diode and a channel inside it and two arrows of light between them.
        /// </summary>
        /// <remarks>
        /// The vactrol's symbol with its photoresistor replaced by a field-effect channel, and sixty
        /// units across for the reason that one records: three things have to fit side by side and be
        /// told apart, and at the ordinary width they overlap into a smudge that reads as none of
        /// them. Drawing the two differently is the point — a schematic where a photoFET and a
        /// vactrol look alike is a schematic that will put the wrong part on the breadboard.
        /// </remarks>
        protected internal override void LayoutSymbol(SymbolLayout Sym)
        {
            Sym.AddTerminal(anode, new Coord(-30, 20), new Coord(-16, 20));
            Sym.AddTerminal(cathode, new Coord(-30, -20), new Coord(-16, -20));
            Sym.AddTerminal(channel1, new Coord(30, 20), new Coord(16, 20));
            Sym.AddTerminal(channel2, new Coord(30, -20), new Coord(16, -20));

            Sym.AddRectangle(EdgeType.Black, new Coord(-24, -26), new Coord(24, 26));

            // The diode: a triangle from the anode side onto its bar.
            Sym.AddWire(anode, new Coord(-16, 20), new Coord(-16, 6));
            Sym.AddWire(cathode, new Coord(-16, -20), new Coord(-16, -6));
            Sym.DrawLine(EdgeType.Black, new Coord(-22, 6), new Coord(-10, 6));
            Sym.DrawLine(EdgeType.Black, new Coord(-22, 6), new Coord(-16, -6));
            Sym.DrawLine(EdgeType.Black, new Coord(-10, 6), new Coord(-16, -6));
            Sym.DrawLine(EdgeType.Black, new Coord(-22, -6), new Coord(-10, -6));

            // The light, which is the only thing crossing between the two halves.
            Sym.DrawArrow(EdgeType.Black, new Coord(-6, 4), new Coord(4, 4), 0.3, 0.25);
            Sym.DrawArrow(EdgeType.Black, new Coord(-6, -4), new Coord(4, -4), 0.3, 0.25);

            // The channel, drawn as a field-effect device: a bar with the two terminals on it and no
            // gate, because the light is the gate.
            Sym.AddWire(channel1, new Coord(16, 20), new Coord(16, 10));
            Sym.AddWire(channel2, new Coord(16, -20), new Coord(16, -10));
            Sym.DrawLine(EdgeType.Black, new Coord(16, 10), new Coord(16, -10));
            Sym.DrawLine(EdgeType.Black, new Coord(10, 10), new Coord(10, -10));
            Sym.DrawLine(EdgeType.Black, new Coord(10, 10), new Coord(16, 10));
            Sym.DrawLine(EdgeType.Black, new Coord(10, -10), new Coord(16, -10));

            Sym.DrawText(() => Name, new Coord(0, 30), Alignment.Center, Alignment.Near);
            Sym.DrawText(() => PartNumber, new Coord(0, -30), Alignment.Center, Alignment.Far);
        }
    }
}
