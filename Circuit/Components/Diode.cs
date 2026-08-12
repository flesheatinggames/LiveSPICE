using ComputerAlgebra;
using System;
using System.ComponentModel;

namespace Circuit
{
    public enum DiodeType
    {
        Diode,
        LED,
        Zener,
    }

    /// <summary>
    /// Shockley diode model: http://en.wikipedia.org/wiki/Diode_modelling#Shockley_diode_model
    /// </summary>
    [Category("Diodes")]
    [DisplayName("Diode")]
    public class Diode : TwoTerminal
    {
        protected Quantity _is = new Quantity(1e-12m, Units.A);
        [Serialize, Description("Saturation current.")]
        public Quantity IS { get { return _is; } set { if (_is.Set(value)) NotifyChanged(nameof(IS)); } }

        protected Quantity _n = new Quantity(1, Units.None);
        [Serialize, Description("Gate emission coefficient.")]
        public Quantity n { get { return _n; } set { if (_n.Set(value)) NotifyChanged(nameof(n)); } }

        protected DiodeType type = DiodeType.Diode;
        [Serialize, Description("Type of this diode. This property only affects the schematic symbol, it does not affect the simulation.")]
        public DiodeType Type { get { return type; } set { type = value; NotifyChanged(nameof(Type)); } }

        public Diode() { Name = "D1"; }

        public static Expression Analyze(Analysis Mna, string Name, Node Anode, Node Cathode, Expression IS, Expression n)
        {
            // V = Va - Vc
            Expression Vac = Anode.V - Cathode.V;
            Vac = Mna.AddUnknownEqualTo("V" + Name, Vac);

            // Evaluate the model.
            Expression i = IS * LinExpm1(Vac / (n * VT));
            i = Mna.AddUnknownEqualTo("i" + Name, i);

            Mna.AddPassiveComponent(Anode, Cathode, i);

            return i;
        }
        public static Expression Analyze(Analysis Mna, Node Anode, Node Cathode, Expression IS, Expression n) { return Analyze(Mna, Mna.AnonymousName(), Anode, Cathode, IS, n); }

        /// <summary>
        /// The range a live saturation current sweeps, in amperes, and the range a live emission
        /// coefficient sweeps.
        /// </summary>
        /// <remarks>
        /// A live parameter is a control position from 0 to 1 rather than a raw value, because the
        /// thing on the other end of it is a knob or a preset and because a range is what makes the
        /// clamp meaningful. The two ranges below are chosen to span the diodes a guitar pedal
        /// actually contains: 1e-15 A is a small-signal silicon diode at the quiet end, 1e-12 A is
        /// the 1N4148 this library defaults to, 1e-6 A is germanium, and 1e-5 A reaches a Schottky.
        /// Logarithmic, because saturation current is a logarithmic quantity — the forward voltage
        /// moves by about sixty millivolts per decade of it — so a linear control would spend nine
        /// tenths of its travel between germanium and Schottky and none between the silicons.
        ///
        /// The emission coefficient is linear from 1 to 2, which is the whole physical range: 1 is
        /// ideal diffusion and 2 is recombination-dominated, and a real diode sits between them.
        ///
        /// Specification 3.3 names this pair as the mechanism behind "switch silicon to germanium
        /// while a chord rings": both types share one equation and differ only in these numbers, so
        /// the swap is a parameter write rather than a structural re-solve.
        /// </remarks>
        public const double LiveISMinimum = 1e-15;
        public const double LiveISMaximum = 1e-5;
        public const double LiveEmissionMinimum = 1;
        public const double LiveEmissionMaximum = 2;

        /// <summary>Where on the 0-to-1 control a given saturation current sits.</summary>
        public static double PositionOfIS(double IS)
        {
            double low = Math.Log10(LiveISMinimum), high = Math.Log10(LiveISMaximum);
            double at = (Math.Log10(Math.Max(LiveISMinimum, Math.Min(LiveISMaximum, IS))) - low) / (high - low);
            return Math.Max(0, Math.Min(1, at));
        }

        /// <summary>What a 0-to-1 control position means as a saturation current.</summary>
        public static double ISAt(double Position)
        {
            double low = Math.Log10(LiveISMinimum), high = Math.Log10(LiveISMaximum);
            return Math.Pow(10, low + (high - low) * Math.Max(0, Math.Min(1, Position)));
        }

        public static double PositionOfEmission(double n) =>
            Math.Max(0, Math.Min(1, (n - LiveEmissionMinimum) / (LiveEmissionMaximum - LiveEmissionMinimum)));

        public static double EmissionAt(double Position) =>
            LiveEmissionMinimum + (LiveEmissionMaximum - LiveEmissionMinimum) * Math.Max(0, Math.Min(1, Position));

        public override void Analyze(Analysis Mna)
        {
            // Both values reach the equations inside an exponential rather than as a linear
            // coefficient, so they land in the Newton Jacobian and not in the rows the symbolic
            // elimination reduces. That is a different case from a potentiometer and it is measured
            // separately — see docs/stompbench-a4-result.md.
            //
            // Live only when a caller names this diode, rather than whenever live parameters are on
            // at all. A knob is drawn on the schematic as adjustable and a diode's model constants
            // are not, so making every diode in the repository carry two live values by default
            // would be a much larger change to what a circuit is than the flag implies.
            //
            // The two are declared together or not at all. Nothing here needs different algebra for
            // the two cases, unlike a potentiometer, so this is only a pair of DeclareParameter
            // calls and the baked path is the same code with numbers coming back out of it — the
            // component's own numbers, exactly, which is what the Value argument is for.
            bool live = Mna.IsNamedLive(Name);
            Expression saturation = Mna.DeclareParameter(
                Name, "IS", PositionOfIS((double)IS), ISAt, LiveISMinimum, LiveISMaximum,
                Value: (double)IS, Live: live);
            Expression emission = Mna.DeclareParameter(
                Name, "n", PositionOfEmission((double)n), EmissionAt,
                LiveEmissionMinimum, LiveEmissionMaximum,
                Value: (double)n, Live: live);

            Analyze(Mna, Name, Anode, Cathode, saturation, emission);
        }

        public static void LayoutSymbol(SymbolLayout Sym, Terminal A, Terminal C, DiodeType Type, Func<string> Name, Func<string> Part)
        {
            Sym.AddTerminal(A, new Coord(0, 20));
            Sym.AddWire(A, new Coord(0, 10));

            Sym.AddTerminal(C, new Coord(0, -20));
            Sym.AddWire(C, new Coord(0, -10));

            Sym.AddLoop(EdgeType.Black,
                new Coord(-10, 10),
                new Coord(10, 10),
                new Coord(0, -10));
            Sym.AddLine(EdgeType.Black, new Coord(-10, -10), new Coord(10, -10));

            switch (Type)
            {
                case DiodeType.LED:
                    Sym.DrawArrow(EdgeType.Black, new Coord(-12, 5), new Coord(-20, -3), 0.2);
                    Sym.DrawArrow(EdgeType.Black, new Coord(-8, -2), new Coord(-16, -10), 0.2);
                    break;
                case DiodeType.Zener:
                    Sym.AddLine(EdgeType.Black, new Coord(-10, -10), new Coord(-10, -5));
                    Sym.AddLine(EdgeType.Black, new Coord(10, -10), new Coord(10, -15));
                    break;
                default:
                    break;
            }

            if (Part != null)
                Sym.DrawText(Part, new Coord(12, 4), Alignment.Near, Alignment.Near);
            Sym.DrawText(Name, new Coord(12, -4), Alignment.Near, Alignment.Far);
        }

        protected internal override void LayoutSymbol(SymbolLayout Sym) { LayoutSymbol(Sym, Anode, Cathode, Type, () => Name, () => PartNumber); }
    }
}
