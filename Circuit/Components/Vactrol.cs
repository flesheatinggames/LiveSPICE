using ComputerAlgebra;
using System.Collections.Generic;
using System.ComponentModel;

namespace Circuit
{
    /// <summary>
    /// An optocoupler: a light-emitting diode bonded to a photoresistor in one sealed package.
    /// </summary>
    /// <remarks>
    /// Electrically two isolated ports — a diode you drive and a resistance you put in a signal path
    /// — joined only by light. What makes it a musical part rather than a switch is that the
    /// photoresistor does not follow the light: it lags, and it lags <em>asymmetrically</em>, taking
    /// milliseconds to brighten and tens or hundreds of milliseconds to return. That asymmetry is
    /// why an optical compressor sounds the way it does, and modelling one time constant for both
    /// directions would ship the device with its character removed.
    ///
    /// <b>The state is a conductance, not a resistance, and that is not a detail.</b> Modified nodal
    /// analysis is linear in conductance: a resistor contributes G to a node's Kirchhoff sum, so a
    /// conductance that happens to be an unknown enters the matrix exactly where a constant one
    /// would. A resistance would enter as 1/R, and the row reduction would be over rational
    /// functions of a state variable rather than polynomials in it. Resistor.cs says the same thing
    /// about why it stores conductance.
    ///
    /// <b>The lag is blended rather than switched, and Newton is the reason.</b> The obvious way to
    /// write "one time constant going up and another coming down" is a conditional on the sign of
    /// the error, and ComputerAlgebra would differentiate that correctly — Differentiate rewrites
    /// D[If[c,t,f],x] into If[c, D[t,x], D[f,x]], holding the condition fixed, which is the right
    /// one-sided derivative. The trouble is not the algebra but the iteration: exactly at the
    /// crossing the two branches disagree about the slope, and a Newton step that lands on the far
    /// side comes back, which is chatter rather than convergence. So the two rates are mixed through
    /// a smooth step over a narrow band around the crossing, the way <see cref="Component.LinExpm1"/>
    /// straightens an exponential past its knee and the Triode rounds its own. The band is
    /// <see cref="Blend"/> and it is a property rather than a hidden constant, because it is a
    /// number that can be heard.
    ///
    /// The smooth step is algebraic — x/sqrt(x² + w²) — rather than a hyperbolic tangent, which is
    /// deliberate: this repository's native emitter lowers Sqrt and does not lower Tanh, and a model
    /// that could only run on the managed path would be a model that cannot be played.
    /// </remarks>
    [Category("Optical")]
    [DisplayName("Vactrol")]
    [DefaultProperty("Ron")]
    [Description("Optocoupler: an LED bonded to a photoresistor, whose resistance lags the light asymmetrically.")]
    public class Vactrol : Component
    {
        private Terminal anode, cathode, cell1, cell2;

        public override IEnumerable<Terminal> Terminals
        {
            get
            {
                yield return anode;
                yield return cathode;
                yield return cell1;
                yield return cell2;
            }
        }

        [Browsable(false)]
        public Terminal Anode { get { return anode; } }
        [Browsable(false)]
        public Terminal Cathode { get { return cathode; } }
        [Browsable(false)]
        public Terminal Cell1 { get { return cell1; } }
        [Browsable(false)]
        public Terminal Cell2 { get { return cell2; } }

        private Quantity ron = new Quantity(4000m, Units.Ohm);
        [Serialize, Description("Cell resistance at the reference LED current.")]
        public Quantity Ron { get { return ron; } set { if (ron.Set(value)) NotifyChanged(nameof(Ron)); } }

        private Quantity roff = new Quantity(5e6m, Units.Ohm);
        [Serialize, Description("Cell resistance with the LED dark.")]
        public Quantity Roff { get { return roff; } set { if (roff.Set(value)) NotifyChanged(nameof(Roff)); } }

        private Quantity iref = new Quantity(1e-3m, Units.A);
        [Serialize, Description("The LED current Ron is quoted at.")]
        public Quantity IRef { get { return iref; } set { if (iref.Set(value)) NotifyChanged(nameof(IRef)); } }

        private double gamma = 0.9;
        [Serialize, Description("How steeply conductance follows light. A real cell is a little under one.")]
        public double Gamma { get { return gamma; } set { gamma = value; NotifyChanged(nameof(Gamma)); } }

        private Quantity rise = new Quantity(8e-3m, Units.s);
        [Serialize, Description("Time constant while the cell is brightening.")]
        public Quantity Rise { get { return rise; } set { if (rise.Set(value)) NotifyChanged(nameof(Rise)); } }

        private Quantity fall = new Quantity(80e-3m, Units.s);
        [Serialize, Description("Time constant while the cell is returning. An order of magnitude slower than the rise.")]
        public Quantity Fall { get { return fall; } set { if (fall.Set(value)) NotifyChanged(nameof(Fall)); } }

        private double blend = 1e-3;
        [Serialize, Description("How wide the band is where the two time constants are mixed, as a fraction of the lit conductance.")]
        public double Blend { get { return blend; } set { blend = value; NotifyChanged(nameof(Blend)); } }

        private Quantity _is = new Quantity(93e-12m, Units.A);
        [Serialize, Description("LED saturation current.")]
        public Quantity IS { get { return _is; } set { if (_is.Set(value)) NotifyChanged(nameof(IS)); } }

        private Quantity _n = new Quantity(4.61m, Units.None);
        [Serialize, Description("LED emission coefficient.")]
        public Quantity n { get { return _n; } set { if (_n.Set(value)) NotifyChanged(nameof(n)); } }

        public Vactrol()
        {
            anode = new Terminal(this, "A");
            cathode = new Terminal(this, "K");
            cell1 = new Terminal(this, "1");
            cell2 = new Terminal(this, "2");
            Name = "U1";
        }

        public override void Analyze(Analysis Mna)
        {
            // The LED half is an ordinary diode, which is what it is. Its current is the only thing
            // that crosses to the other half.
            Expression iLed = Diode.Analyze(Mna, Name + "d", Anode, Cathode, IS, n);

            Expression gOn = 1 / (Expression)Ron;
            Expression gDark = 1 / (Expression)Roff;

            // <b>A smooth positive part of the LED current.</b> The power below would otherwise see a
            // negative base when the diode is reverse biased — its current is bounded below by -IS
            // rather than by zero — and a fractional power of a negative number is not a number. A
            // clamp with Max would fix that and put a corner at zero for Newton to trip over; this
            // has no corner, and its floor keeps the power's slope finite where the current vanishes.
            Expression floor = (Expression)IRef * 1e-9;
            Expression lit = (Call.Sqrt(iLed * iLed + floor * floor) + iLed) / 2;

            // What the cell would settle at for the light it is seeing now. A photoresistor's
            // conductance follows illuminance to a power a little under one, and an LED's output
            // follows its current closely enough that the two compose into this.
            Expression target = gDark + gOn * Binary.Power(lit / IRef, Gamma);
            target = Mna.AddUnknownEqualTo("G" + Name + "t", target);

            // The state: where the cell actually is, which is behind where it is going.
            Expression g = Mna.AddUnknown("G" + Name);
            Expression drive = target - g;

            // Rising or falling, mixed smoothly across a band of width w so that the crossing is
            // differentiable. The step is 1 when the cell is brightening and 0 when it is returning.
            Expression w = gOn * Blend;
            Expression rising = (1 + drive / Call.Sqrt(drive * drive + w * w)) / 2;
            Expression rate = (1 - rising) / (Expression)Fall + rising / (Expression)Rise;
            Mna.AddEquation(D(g, t), drive * rate);

            // The cell itself, which is a resistor whose conductance is the state above.
            Mna.AddPassiveComponent(Cell1, Cell2, (Cell1.V - Cell2.V) * g);
        }

        /// <summary>
        /// The package, with a diode and a cell inside it and two arrows of light between them.
        /// </summary>
        /// <remarks>
        /// <b>Sixty units across rather than forty, which is wider than anything else here.</b> Three
        /// things have to fit side by side and be told apart — a diode, the light, and a resistor —
        /// and at the ordinary width they overlapped into a smudge that read as none of them. The
        /// name and the part number go above and below rather than beside, for the same reason: the
        /// sides are where the terminals are.
        /// </remarks>
        protected internal override void LayoutSymbol(SymbolLayout Sym)
        {
            Sym.AddTerminal(anode, new Coord(-30, 20), new Coord(-16, 20));
            Sym.AddTerminal(cathode, new Coord(-30, -20), new Coord(-16, -20));
            Sym.AddTerminal(cell1, new Coord(30, 20), new Coord(16, 20));
            Sym.AddTerminal(cell2, new Coord(30, -20), new Coord(16, -20));

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

            // The cell, drawn as the resistor it is.
            Sym.AddWire(cell1, new Coord(16, 20), new Coord(16, 8));
            Sym.AddWire(cell2, new Coord(16, -20), new Coord(16, -8));
            Sym.AddRectangle(EdgeType.Black, new Coord(10, -8), new Coord(22, 8));

            Sym.DrawText(() => Name, new Coord(0, 30), Alignment.Center, Alignment.Near);
            Sym.DrawText(() => PartNumber, new Coord(0, -30), Alignment.Center, Alignment.Far);
        }
    }
}
