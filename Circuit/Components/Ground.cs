using System.ComponentModel;

namespace Circuit
{
    /// <summary>
    /// Ground component, V = 0.
    /// </summary>
    [Category("Generic")]
    [DisplayName("Ground")]
    public class Ground : NamedWire
    {
        public Ground()
        {
            Name = "GND1";
            WireName = "GND";
        }

        public static void Analyze(Analysis Mna, Node G)
        {
            // Nodes connected to ground have V = 0.
            Mna.AddEquation(G.V, 0);
            // Ground doesn't care about current.
            Mna.AddTerminal(G, null);
        }

        public override void Analyze(Analysis Mna) { Analyze(Mna, Terminal); }

        /// <summary>
        /// Three bars of decreasing length, which is what a ground looks like.
        /// </summary>
        /// <remarks>
        /// This differs from upstream, which draws a hollow triangle pointing down. The triangle is
        /// a legitimate earth symbol and it is not the one most schematics use for a signal ground,
        /// and it read as wrong to the first person who saw a circuit drawn from these layouts.
        ///
        /// Changed here rather than overridden by whatever is drawing, so that the shape a ground
        /// has is a fact about the component and not a special case each renderer has to know. The
        /// footprint is unchanged — twenty units across and ten below the terminal, exactly what the
        /// triangle occupied — so nothing that was laid out around a ground has moved.
        ///
        /// Nothing about the analysis depends on this. A layout is drawing and the solver never
        /// reads one.
        /// </remarks>
        protected internal override void LayoutSymbol(SymbolLayout Sym)
        {
            Sym.AddTerminal(Terminal, new Coord(0, 0));
            Sym.AddWire(new Coord(0, 0), new Coord(0, -2));

            Sym.AddLine(EdgeType.Black, new Coord(-10, -2), new Coord(10, -2));
            Sym.AddLine(EdgeType.Black, new Coord(-6, -6), new Coord(6, -6));
            Sym.AddLine(EdgeType.Black, new Coord(-2, -10), new Coord(2, -10));
        }
    }
}
