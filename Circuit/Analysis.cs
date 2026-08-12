using ComputerAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Circuit
{
    /// <summary>
    /// Exception for problems analyzing a component.
    /// </summary>
    public class AnalysisException : Exception
    {
        public AnalysisException(string Message) : base(Message) { }
    }

    /// <summary>
    /// What an analysis is allowed to leave symbolic.
    /// </summary>
    /// <remarks>
    /// Added for Stompbench milestone A4. Specification 3.3 names a per-circuit opt-out back to baked
    /// constants as the escape hatch for a circuit that cannot afford live parameters; A4 measured
    /// the cost and found it is the other way round, so this is opt-in rather than opt-out.
    ///
    /// What the measurement found, in full in docs/stompbench-a4-result.md: leaving a wiper symbolic
    /// costs between nothing and a hundred and twenty-four times the solve, and two circuits in the
    /// repository do not merely cost more but stop working. Until that is understood, a caller has to
    /// ask for a live parameter, and the answer to "what does this circuit do" is the same as it was
    /// before the milestone.
    /// </remarks>
    public sealed class AnalysisOptions
    {
        /// <summary>
        /// Whether component values a player might change while playing stay symbolic. False, the
        /// default, bakes each one into the equations at the value it currently has, which is what
        /// the library did before A4 and is the behaviour every solve is compared against.
        /// </summary>
        public bool LiveParameters = false;

        /// <summary>
        /// Which components stay symbolic when <see cref="LiveParameters"/> is on. Empty, the
        /// default, means all of them. Everything not named is baked at the value it currently has,
        /// exactly as if <see cref="LiveParameters"/> were off for that component alone.
        /// </summary>
        /// <remarks>
        /// The cost of a live parameter is not linear in how many there are. Each symbol that
        /// survives the elimination lands in the denominator of every entry below its pivot and the
        /// next step puts the next one inside that, so what the solve costs is closer to compounding
        /// with the count than to adding up — and a player turns two knobs on a six-knob preamp, not
        /// six. Choosing which two is a much cheaper lever than making six affordable.
        ///
        /// A name matches a component either fully qualified ("X1.Tone") or by its own name alone
        /// ("Tone", which then matches the component wherever in the subcircuit hierarchy it sits).
        /// Matching is ordinal and case-sensitive, because these are the names in the schematic file.
        ///
        /// Components rather than individual values, deliberately. A potentiometer declares two —
        /// the conductance either side of its wiper — and they are one knob: leaving one symbolic
        /// and baking the other would describe a component that does not exist. Nothing a player can
        /// turn is finer-grained than a component.
        /// </remarks>
        public List<string> LiveComponents = new List<string>();

        /// <summary>
        /// Whether a particular component's values stay symbolic, given its name with whatever
        /// subcircuit prefix it sits under.
        /// </summary>
        public bool IsLive(string Component)
        {
            if (!LiveParameters)
                return false;
            if (LiveComponents.Count == 0)
                return true;

            return IsNamed(Component);
        }

        /// <summary>
        /// Whether this component was named explicitly, as opposed to being covered by the default
        /// of leaving everything live.
        /// </summary>
        /// <remarks>
        /// What a component value costs depends on where in the equations it lands, and there are
        /// two quite different places. A potentiometer's wiper is a linear coefficient, so it goes
        /// through the symbolic elimination and its cost is the growth that produces. A diode's
        /// saturation current is inside an exponential, so it lands in the Newton Jacobian, which
        /// is not eliminated symbolically at all — the executor fills that matrix and solves it
        /// numerically every step, and a symbol there costs a memory read instead of a folded
        /// constant.
        ///
        /// The second is cheap and it is also a much larger change to what a circuit is: every
        /// diode in the repository would acquire two live values, where a knob is something the
        /// schematic already draws as adjustable. So device parameters are live only when a caller
        /// names the component, and knobs are live unless a caller narrows the list.
        /// </remarks>
        public bool IsNamed(string Component)
        {
            foreach (string i in LiveComponents)
            {
                if (i == Component)
                    return true;
                // An unqualified name matches the component wherever it sits, so a caller does not
                // have to know the subcircuit hierarchy to name a knob. Compared on a boundary so
                // that "Tone" does not match "MasterTone".
                if (Component.EndsWith("." + i, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public static AnalysisOptions Default { get { return new AnalysisOptions(); } }

        /// <summary>Everything baked, which is the pre-A4 behaviour and the default.</summary>
        public static AnalysisOptions Baked { get { return new AnalysisOptions { LiveParameters = false }; } }

        /// <summary>Potentiometer and variable resistor values left symbolic.</summary>
        public static AnalysisOptions Live { get { return new AnalysisOptions { LiveParameters = true }; } }

        /// <summary>Only the named components left symbolic; everything else baked.</summary>
        public static AnalysisOptions LiveOnly(IEnumerable<string> Components)
        {
            return new AnalysisOptions { LiveParameters = true, LiveComponents = Components.ToList() };
        }
    }

    /// <summary>
    /// Helper class for building a system of MNA equations and unknowns.
    /// </summary>
    public class Analysis : DynamicNamespace
    {
        private List<Equal> equations = new List<Equal>();
        private List<Expression> unknowns = new List<Expression>();
        private Dictionary<Expression, Expression> kcl = new Dictionary<Expression, Expression>();
        private List<Arrow> initialConditions = new List<Arrow>();
        private List<LiveParameter> parameters = new List<LiveParameter>();

        private AnalysisOptions options = AnalysisOptions.Default;
        /// <summary>What this analysis is allowed to leave symbolic. See <see cref="AnalysisOptions"/>.</summary>
        public AnalysisOptions Options { get { return options; } set { options = value ?? AnalysisOptions.Default; } }

        /// <summary>
        /// Whether component values a player might change stay symbolic in this system.
        /// </summary>
        /// <remarks>
        /// A component reads this when the two cases want different algebra rather than the same
        /// algebra over a different symbol — which is the potentiometer's situation, and is why this
        /// is exposed at all rather than being hidden entirely behind
        /// <see cref="DeclareParameter"/>. Anything that merely needs a value should call that and
        /// not ask.
        /// </remarks>
        public bool LiveParameters { get { return options.LiveParameters; } }

        /// <summary>
        /// Whether this particular component's values stay symbolic. A component that needs
        /// different algebra for the two cases asks this rather than <see cref="LiveParameters"/>.
        /// </summary>
        /// <remarks>
        /// Per component, because liveness is per component: a caller may leave the two knobs a
        /// player reaches for symbolic and bake the four they do not, and a component that is baked
        /// has to be indistinguishable from the same component analyzed with live parameters off
        /// entirely. Asking the circuit-wide flag instead would give a baked potentiometer the live
        /// branch's conductance formulation with a number in it — algebraically the same circuit,
        /// and not the same floating-point arithmetic, so the render would differ in its last bits
        /// from one that never asked for a live parameter at all.
        /// </remarks>
        public bool IsLive(string Name) { return options.IsLive(context.Prefix + Name); }

        /// <summary>
        /// Whether this component was named explicitly rather than covered by the default. What a
        /// device model parameter asks, for the reason in <see cref="AnalysisOptions.IsNamed"/>.
        /// </summary>
        public bool IsNamedLive(string Name)
        {
            return options.LiveParameters && options.IsNamed(context.Prefix + Name);
        }

        public Analysis() { }
        public Analysis(AnalysisOptions Options) { options = Options ?? AnalysisOptions.Default; }

        // Describes the analysis of a subcircuit.
        protected class Circuit
        {
            private Circuit parent = null;
            public Circuit Parent { get { return parent; } }

            private string name = null;
            public string Name { get { return name; } }

            public string Prefix
            {
                get
                {
                    string prefix = "";
                    if (parent != null)
                        prefix = parent.Prefix;
                    if (name != null)
                        prefix = prefix + name + ".";
                    return prefix;
                }
            }

            private int anon = 0;
            public string AnonymousName() { return "_" + (++anon).ToString(); }

            public Dictionary<Expression, Expression> Definitions = new Dictionary<Expression, Expression>();
            public List<Equal> Equations = new List<Equal>();
            public Dictionary<Expression, Expression> Kcl = new Dictionary<Expression, Expression>();
            public NodeCollection Nodes = new NodeCollection();
            public List<Arrow> InitialConditions = new List<Arrow>();

            public Circuit() { }
            public Circuit(Circuit Parent, string Name) { parent = Parent; name = Name; }
        }

        private Circuit context = new Circuit();

        /// <summary>
        /// Begin analysis of a new context with the given nodes.
        /// </summary>
        /// <param name="Name"></param>
        /// <param name="Nodes"></param>
        public void PushContext(string Name, IEnumerable<Node> Nodes)
        {
            PushContext(Name);
            DeclNodes(Nodes);
        }
        public void PushContext(string Name, params Node[] Nodes) { PushContext(Name, Nodes.AsEnumerable()); }

        /// <summary>
        /// Begin analysis of a new context.
        /// </summary>
        /// <param name="Name"></param>
        public void PushContext(string Name) { context = new Circuit(context, Name); }
        /// <summary>
        /// End analysis of the current context.
        /// </summary>
        public void PopContext()
        {
            // Evaluate the definitions from the context for the equations and add the results to the analysis.
            foreach (Equal i in context.Equations)
            {
                Equal ei = (Equal)Evaluate(i, context.Definitions);
                if (!equations.Contains(ei))
                    equations.Add(ei);
            }
            // And the KCL equations.
            foreach (KeyValuePair<Expression, Expression> i in context.Kcl)
                AddKcl(kcl, i.Key, Evaluate(i.Value, context.Definitions));
            // And the initial conditions.
            initialConditions.AddRange(context.InitialConditions.Evaluate(context.Definitions).OfType<Arrow>());

            foreach (Node i in context.Nodes)
                i.EndAnalysis();

            context = context.Parent;
        }

        /// <summary>
        /// Add Nodes to the current context.
        /// </summary>
        /// <param name="Nodes"></param>
        public void DeclNodes(IEnumerable<Node> Nodes)
        {
            context.Nodes.AddRange(Nodes);

            string prefix = context.Prefix;
            foreach (Node i in Nodes)
                i.BeginAnalysis(prefix);
        }
        public void DeclNodes(params Node[] Nodes) { DeclNodes(Nodes.AsEnumerable()); }

        /// <summary>
        /// Get the KCL expressions for this analysis.
        /// </summary>
        public IEnumerable<KeyValuePair<Expression, Expression>> Kcl { get { return kcl.Where(i => i.Value is object); } }

        /// <summary>
        /// Enumerates the equations in the system.
        /// </summary>
        public IEnumerable<Equal> Equations { get { return equations.Concat(Kcl.Select(i => Equal.New(i.Value, 0))); } }
        /// <summary>
        /// Enumerates the unknowns in the system.
        /// </summary>
        public IEnumerable<Expression> Unknowns { get { return kcl.Keys.Concat(unknowns); } }

        /// <summary>
        /// Enumerates the inputs
        /// </summary>
        public IEnumerable<Arrow> InitialConditions { get { return initialConditions; } }

        /// <summary>
        /// The component values left symbolic in this system, in the order they were declared.
        /// Empty when <see cref="AnalysisOptions.LiveParameters"/> is off, and empty for a circuit
        /// that has nothing a player would turn.
        /// </summary>
        public IEnumerable<LiveParameter> Parameters { get { return parameters; } }

        /// <summary>
        /// Substitutions putting every live parameter back to the value it was analyzed at. Applying
        /// these to a system recovers exactly the system that would have been built with the
        /// parameters baked, which is what the steady-state solve needs and what makes a
        /// live-against-baked comparison a comparison of one thing.
        /// </summary>
        public IEnumerable<Arrow> BakedParameters
        {
            get { return parameters.Select(i => i.Baked); }
        }

        /// <summary>
        /// Declares a component value that stays symbolic, and returns what the component should put
        /// in its equations in place of the number: the symbol when parameters are live, and the
        /// number itself when they are not.
        /// </summary>
        /// <param name="Name">The component's name. The current context's prefix is added to it.</param>
        /// <param name="Quantity">Which of the component's values this is, such as "Wipe".</param>
        /// <param name="Position">Where the control is, which for a wiper is 0 to 1.</param>
        /// <param name="Map">
        /// Turns a control position into the value the equations want, applying whatever curve and
        /// clamp the component defines. It must land inside [Minimum, Maximum] for every input,
        /// because the equations are only valid there and nothing downstream re-checks.
        /// </param>
        /// <param name="Value">
        /// The value the equations are built with, when the component knows it to more precision
        /// than <paramref name="Map"/> of <paramref name="Position"/> reproduces. Null, the usual
        /// case, means the mapping is the definition.
        /// </param>
        /// <param name="Live">
        /// Whether this value is live, overriding the per-component decision. A device model
        /// parameter passes false unless its component was named, because it lands in a different
        /// part of the system from a knob and is a different question — see
        /// <see cref="AnalysisOptions.IsNamed"/>.
        /// </param>
        /// <remarks>
        /// A component calls this instead of computing a number, so the decision about whether the
        /// value is live belongs to the analysis rather than to the component.
        ///
        /// The exact value matters more than it looks. A control mapping does not have to be
        /// invertible in floating point, and the saturation current's is not: taking a base-ten
        /// logarithm and then a power of ten returns a number a few bits away from the one it
        /// started at. Without <paramref name="Value"/> every diode in the repository would move by
        /// those few bits the moment this was wired up, which would re-bless every golden file for
        /// no change to any circuit.
        /// </remarks>
        public Expression DeclareParameter(
            string Name, string Quantity, double Position, Func<double, double> Map,
            double Minimum, double Maximum, double? Value = null, bool? Live = null)
        {
            // Asked per component rather than once for the circuit, so that a caller can leave the
            // two knobs a player reaches for symbolic and bake the four they do not. A component
            // that is refused here is indistinguishable from the same component analyzed with live
            // parameters off: it puts a number in its equations and declares nothing, so it never
            // reaches the parameter list, the pivot conditions, or either executor's slot table.
            if (!(Live ?? options.IsLive(context.Prefix + Name)))
                return Value ?? Map(Position);

            string full = context.Prefix + Name + "." + Quantity;
            if (parameters.Any(i => i.Name == full))
            {
                throw new AnalysisException(
                    "Two components in the same circuit are both called '" + context.Prefix + Name +
                    "', so their '" + Quantity + "' parameters would share the name '" + full +
                    "' and a write to one would move both.");
            }

            LiveParameter parameter = new LiveParameter(
                full, context.Prefix + Name, Quantity, Position, Map, Minimum, Maximum, Value);
            parameters.Add(parameter);
            return parameter.Symbol;
        }

        /// <summary>
        /// Add a current to the given node.
        /// </summary>
        /// <param name="Node"></param>
        /// <param name="i"></param>
        public void AddTerminal(Node Terminal, Expression i) { AddKcl(context.Kcl, Terminal.V, i); }

        /// <summary>
        /// Add the current for a passive component with the given terminals.
        /// </summary>
        /// <param name="Anode"></param>
        /// <param name="Cathode"></param>
        /// <param name="i"></param>
        public void AddPassiveComponent(Node Anode, Node Cathode, Expression i)
        {
            AddTerminal(Anode, i);
            AddTerminal(Cathode, -i);
        }

        /// <summary>
        /// Define the value of an expression in the current context. 
        /// </summary>
        /// <param name="Key"></param>
        /// <param name="Value"></param>
        public void Define(Expression Key, Expression Value)
        {
            Expression value;
            if (!context.Definitions.TryGetValue(Key, out value))
                context.Definitions.Add(Key, Value);
            else if (!value.Equals(Value))
                throw new ArgumentException("Redefinition of '" + Key.ToString() + "'.");
        }
        public void Define(Arrow x) { Define(x.Left, x.Right); }

        /// <summary>
        /// Add equations to the system.
        /// </summary>
        /// <param name="Eq"></param>
        public void AddEquations(IEnumerable<Equal> Eq)
        {
            foreach (Equal i in Eq)
                if (!equations.Contains(i))
                    context.Equations.Add(i);
        }
        public void AddEquations(params Equal[] Eq) { AddEquations(Eq.AsEnumerable()); }
        public void AddEquation(Expression a, Expression b) { AddEquations(Equal.New(a, b)); }

        /// <summary>
        /// Add Unknowns to the system.
        /// </summary>
        /// <param name="Unknowns"></param>
        public void AddUnknowns(IEnumerable<Expression> Unknowns)
        {
            foreach (Expression i in Unknowns)
                if (!unknowns.Contains(i))
                    unknowns.Add(i);
        }
        public void AddUnknowns(params Expression[] Unknowns) { AddUnknowns(Unknowns.AsEnumerable()); }

        /// <summary>
        /// Add a new named unknown to the system.
        /// </summary>
        /// <param name="Name"></param>
        /// <returns></returns>
        public Expression AddUnknown(string Name)
        {
            Expression x = Component.DependentVariable(context.Prefix + Name, Component.t);
            AddUnknowns(x);
            return x;
        }
        /// <summary>
        /// Add an anonymous unknown to the system.
        /// </summary>
        /// <returns></returns>
        public Expression AddUnknown() { return AddUnknown(AnonymousName()); }

        /// <summary>
        /// Add a new named unknown to the system with a known equation.
        /// </summary>
        /// <param name="Name"></param>
        /// <param name="Eq"></param>
        /// <returns></returns>
        public Expression AddUnknownEqualTo(string Name, Expression Eq)
        {
            // Find an existing unknown that may just be a constant factor of this one.
            IEnumerable<Equal> eqs = equations.Concat(context.Equations);
            foreach (Equal i in eqs.Where(j => !j.Right.EqualsZero() && Component.IsDependentVariable(j.Left, Component.t)))
            {
                Expression factor = Eq / i.Right;
                if (factor is Constant)
                {
                    // Existing unknown is a constant factor of this new unknown.
                    return i.Left * factor;
                }
            }
            Expression x = AddUnknown(Name);
            AddEquation(x, Eq);
            return x;
        }
        /// <summary>
        /// Add an anonymous unknown to the system with a known equation.
        /// </summary>
        /// <param name="Eq"></param>
        /// <returns></returns>
        public Expression AddUnknownEqualTo(Expression Eq) { return AddUnknownEqualTo(AnonymousName(), Eq); }

        /// <summary>
        /// Add initial conditions to the system.
        /// </summary>
        /// <param name="InitialCondition"></param>
        public void AddInitialConditions(IEnumerable<Arrow> InitialConditions) { context.InitialConditions.AddRange(InitialConditions); }
        public void AddInitialConditions(params Arrow[] InitialConditions) { context.InitialConditions.AddRange(InitialConditions); }

        /// <summary>
        /// Get an anonymous variable name. It will be uniqued later.
        /// </summary>
        /// <returns></returns>
        public string AnonymousName() { return context.AnonymousName(); }

        private void AddKcl(Dictionary<Expression, Expression> kcl, Expression V, Expression i)
        {
            if (kcl.TryGetValue(V, out var sumi))
            {
                // preserve null (arbitrary current).
                if (i is null)
                    kcl[V] = null;
                else if (sumi != null)
                    kcl[V] = sumi + i;
            }
            else
            {
                kcl[V] = i;
            }
        }

        // Helper for evaluating typical analysis expressions.
        private static Expression Evaluate(Expression x, IDictionary<Expression, Expression> At)
        {
            if (x is null)
                return null;
            else if (At.Any())
                return x.Evaluate(At);
            else
                return x;
        }
    }
}
