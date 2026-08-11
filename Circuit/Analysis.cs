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

        public static AnalysisOptions Default { get { return new AnalysisOptions(); } }

        /// <summary>Everything baked, which is the pre-A4 behaviour and the default.</summary>
        public static AnalysisOptions Baked { get { return new AnalysisOptions { LiveParameters = false }; } }

        /// <summary>Potentiometer and variable resistor values left symbolic.</summary>
        public static AnalysisOptions Live { get { return new AnalysisOptions { LiveParameters = true }; } }
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
        /// <remarks>
        /// A component calls this instead of computing a number, so the decision about whether the
        /// value is live belongs to the analysis rather than to the component. That matters because
        /// the opt-out has to reach every component at once: a circuit half baked and half symbolic
        /// would be neither the thing that was measured nor the thing that was compared against.
        /// </remarks>
        public Expression DeclareParameter(
            string Name, string Quantity, double Position, Func<double, double> Map,
            double Minimum, double Maximum)
        {
            if (!options.LiveParameters)
                return Map(Position);

            string full = context.Prefix + Name + "." + Quantity;
            if (parameters.Any(i => i.Name == full))
            {
                throw new AnalysisException(
                    "Two components in the same circuit are both called '" + context.Prefix + Name +
                    "', so their '" + Quantity + "' parameters would share the name '" + full +
                    "' and a write to one would move both.");
            }

            LiveParameter parameter = new LiveParameter(
                full, context.Prefix + Name, Quantity, Position, Map, Minimum, Maximum);
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
