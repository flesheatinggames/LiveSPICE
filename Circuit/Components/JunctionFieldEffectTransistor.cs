using ComputerAlgebra;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Circuit
{
    public enum JfetType
    {
        N,
        P,
    };

    /// <summary>
    /// Implementation of the Ebers-Moll transistor model: http://people.seas.harvard.edu/~jones/es154/lectures/lecture_3/bjt_models/ebers_moll/ebers_moll.html
    /// </summary>
    [Category("Transistors")]
    [DisplayName("JFET")]
    public class JunctionFieldEffectTransistor : Component, INotifyPropertyChanged
    {
        private Terminal s, g, d;
        public override IEnumerable<Terminal> Terminals
        {
            get
            {
                yield return s;
                yield return g;
                yield return d;
            }
        }
        [Browsable(false)]
        public Terminal Source { get { return s; } }
        [Browsable(false)]
        public Terminal Gate { get { return g; } }
        [Browsable(false)]
        public Terminal Drain { get { return d; } }

        private JfetType type = JfetType.N;
        [Serialize, Description("JFET structure.")]
        public JfetType Type { get { return type; } set { type = value; NotifyChanged(nameof(Type)); } }

        protected Quantity _is = new Quantity(1e-14m, Units.A);
        [Serialize, Description("Saturation current.")]
        public Quantity IS { get { return _is; } set { if (_is.Set(value)) NotifyChanged(nameof(IS)); } }

        protected Quantity _n = new Quantity(1, Units.None);
        [Serialize, Description("Gate emission coefficient.")]
        public Quantity n { get { return _n; } set { if (_n.Set(value)) NotifyChanged(nameof(n)); } }

        protected Quantity vt0 = new Quantity(-2, Units.V);
        [Spice.ParameterAlias("VTO")]
        [Serialize, Description("Threshold voltage.")]
        public Quantity Vt0 { get { return vt0; } set { if (vt0.Set(value)) NotifyChanged(nameof(Vt0)); } }

        private Quantity beta = new Quantity(1e-4m, Units.None);// Units.A / Units.V ^ 2);
        [Serialize, Description("Transconductance.")]
        public Quantity Beta { get { return beta; } set { if (beta.Set(value)) NotifyChanged(nameof(Beta)); } }

        private Quantity lambda = new Quantity(0, Units.None);// Units.V ^ -1);
        [Serialize, Description("Channel length modulation.")]
        public Quantity Lambda { get { return lambda; } set { if (lambda.Set(value)) NotifyChanged(nameof(Lambda)); } }

        public JunctionFieldEffectTransistor()
        {
            s = new Terminal(this, "S");
            g = new Terminal(this, "G");
            d = new Terminal(this, "D");
            Name = "J1";
        }

        public override void Analyze(Analysis Mna)
        {
            Diode.Analyze(Mna, Gate, Source, IS, n);
            Diode.Analyze(Mna, Gate, Drain, IS, n);

            // The drain and source terminals are reversible in the JFET model, this 
            // formulation is simpler than explicitly identifying normal/inverted mode.
            Expression Vgds = Gate.V - Call.Min(Source.V, Drain.V);
            Expression Vds = Drain.V - Source.V;
            Expression AbsVds = Call.Abs(Vds);

            //Vgds = Mna.AddUnknownEqualTo(Name + "gds", Vgds);

            Expression Vgds_t0 = Vgds - Vt0;

            // <b>The two regions have to meet, and with a subtraction of one they do not.</b> This
            // read `AbsVds * (2 * Vgds_t0 - 1)` in the linear branch: a subtraction of one volt
            // where a subtraction of the drain-source voltage belongs. The square law is
            // Vds*(2*Vov - Vds) below the knee and Vov^2 above it, where Vov is the gate overdrive,
            // and the knee is at Vds = Vov, so substituting Vds = Vov into the first gives
            // Vov*(2*Vov - Vov) = Vov^2, which is the second. That is the arithmetic that says the
            // corrected form is right: it is not merely closer, it agrees exactly at every bias.
            //
            // What the shipped form gave, in units of Beta, for the default -2 V threshold:
            //
            //   Vgs      overdrive   shipped linear   saturation   gap
            //    0.0 V     2.00 V         6.000          4.000     +2.000
            //   -0.5 V     1.50 V         3.000          2.250     +0.750
            //   -1.0 V     1.00 V         1.000          1.000      0.000
            //   -1.5 V     0.50 V         0.000          0.250     -0.250
            //
            // So a JFET biased at zero gate-source voltage stepped fifty per cent in drain current
            // as it crossed between regions, and the two branches agreed at exactly one operating
            // point — the one where the overdrive happens to be a volt, which is what makes the
            // slip look like an ordinary expression from far enough away.
            //
            // <b>Below half a volt of overdrive it was worse than a step: the current ran
            // backwards.</b> The factor 2*Vgds_t0 - 1 goes negative once the overdrive falls below
            // one half, so the model had the channel delivering power rather than dissipating it.
            // That is exactly the region a JFET occupies when it is used as a voltage-controlled
            // resistor: circuits/stock/MXR Phase 90.schx holds four of them with a -2.021 V
            // threshold and sweeps the gate below -1.521 V on every oscillator cycle.
            //
            // The slope is continuous too, which is what Newton needs rather than merely what
            // physics needs. Differentiating the linear branch in AbsVds gives 2*Vgds_t0 - 2*AbsVds,
            // which vanishes at the knee, and the saturation branch does not depend on AbsVds at
            // all, so both one-sided slopes are zero there. The channel-length modulation factor
            // multiplies both branches, so it cannot break either agreement.
            // `sbrender selftest jfet` measures both to a stated tolerance.
            Expression id = Call.Sign(Vds) * (Vgds >= Vt0) *
                ChannelCurrent(Beta, Vgds_t0, AbsVds, Lambda);

            id = Mna.AddUnknownEqualTo("i" + Name + "d", id);
            CurrentSource.Analyze(Mna, Drain, Source, id);
        }

        /// <summary>
        /// The square law's magnitude: a channel's current for a given overdrive and a given
        /// drain-source voltage, both taken as positive.
        /// </summary>
        /// <remarks>
        /// <b>Shared rather than copied, so that the two regions go on meeting wherever it is
        /// used.</b> Milestone C11 corrected the linear branch here and the correction is the sort
        /// that gets un-corrected by a second copy: a photoFET's channel is this same equation with
        /// the light setting the overdrive, and C12's MOSFET will be it again with the gate diodes
        /// removed and a body terminal added. One expression means one place for the boundary to be
        /// continuous at, and <c>sbrender selftest jfet</c> measures that boundary.
        ///
        /// The caller supplies the sign and any conduction condition, because those differ: a JFET
        /// stops conducting below its threshold, and a photoFET in the dark does not stop but
        /// becomes three hundred megohms.
        /// </remarks>
        public static Expression ChannelCurrent(
            Expression Beta, Expression Overdrive, Expression AbsVds, Expression Lambda) =>
            Beta * (1 + Lambda * AbsVds) *
                Call.If(AbsVds < Overdrive,
                    // Linear region.
                    AbsVds * (2 * Overdrive - AbsVds),
                    // Saturation region.
                    Overdrive ^ 2);

        public static void LayoutSymbol(SymbolLayout Sym, JfetType Type, Terminal S, Terminal G, Terminal D, Func<string> Name, Func<string> Part)
        {
            int bx = 0;
            Sym.AddTerminal(S, new Coord(10, -20), new Coord(10, -10), new Coord(0, -10));
            Sym.AddTerminal(G, new Coord(-20, 0), new Coord(-10, 0));
            Sym.AddTerminal(D, new Coord(10, 20), new Coord(10, 10), new Coord(0, 10));

            Sym.DrawLine(EdgeType.Black, new Coord(bx, 12), new Coord(bx, -12));
            switch (Type)
            {
                case JfetType.N: Sym.DrawArrow(EdgeType.Black, new Coord(-10, 0), new Coord(0, 0), 0.2, 0.3); break;
                case JfetType.P: Sym.DrawArrow(EdgeType.Black, new Coord(0, 0), new Coord(-10, 0), 0.2, 0.3); break;
                default:
                    throw new NotSupportedException("Unknown JFET type.");
            }

            if (Part != null)
                Sym.DrawText(Part, new Coord(8, 20), Alignment.Far, Alignment.Near);
            Sym.DrawText(Name, new Point(8, -20), Alignment.Far, Alignment.Far);

            Sym.AddCircle(EdgeType.Black, new Coord(0, 0), 20);
        }

        protected internal override void LayoutSymbol(SymbolLayout Sym) { LayoutSymbol(Sym, Type, Source, Gate, Drain, () => Name, () => PartNumber); }
    }
}
