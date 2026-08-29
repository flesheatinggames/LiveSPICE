using ComputerAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;
using Util;

namespace Circuit
{
    /// <summary>
    /// Represents the solutions of a system of equations derived from a Circuit for transient analysis.
    /// </summary>
    public class TransientSolution
    {
        public static readonly Variable t = Component.t;
        public static readonly Expression T = Component.T;

        private Expression h;
        /// <summary>
        /// The length of a timestep given by this solution.
        /// </summary>
        public Expression TimeStep { get { return h; } }

        private IEnumerable<SolutionSet> solutions;
        /// <summary>
        /// Ordered list of SolutionSet objects that describe the overall solution. If SolutionSet
        /// a follows SolutionSet b in this enumeration, b's solution may depend on a's solutions.
        /// </summary>
        public IEnumerable<SolutionSet> Solutions { get { return solutions; } }

        private IEnumerable<Arrow> initialConditions;
        /// <summary>
        /// Set of expressions describing the initial conditions of the variables in this solution.
        /// </summary>
        public IEnumerable<Arrow> InitialConditions { get { return initialConditions; } }

        private IEnumerable<LiveParameter> parameters;
        /// <summary>
        /// The component values this solution left symbolic, which a caller may change without
        /// solving again. Empty for a circuit with nothing a player would turn, and empty when the
        /// analysis was told to bake them.
        /// </summary>
        /// <remarks>
        /// Carried on the solution rather than looked up from the analysis again because these are
        /// the parameters this particular solution was built with. The two can differ: an analysis
        /// can be solved more than once, and the steady-state solve below substitutes their values
        /// while the transient system keeps their symbols, so a consumer needs to know which symbols
        /// are in the equations it is about to run and what each of them started at.
        /// </remarks>
        public IEnumerable<LiveParameter> Parameters { get { return parameters; } }

        /// <summary>
        ///
        /// </summary>
        /// <param name="TimeStep">Describes the timestep of the solution.</param>
        /// <param name="Solutions">Enumeration of SolutionSets describing the unknowns solved by this solution.</param>
        /// <param name="InitialConditions">Initial conditions for which the solution is valid.</param>
        /// <param name="Parameters">Description of the parameters in the solution.</param>
        public TransientSolution(
            Expression TimeStep,
            IEnumerable<SolutionSet> Solutions,
            IEnumerable<Arrow> InitialConditions,
            IEnumerable<LiveParameter> Parameters)
        {
            h = TimeStep;
            solutions = Solutions.Buffer();
            initialConditions = InitialConditions.Buffer();
            parameters = Parameters.Buffer();
        }

        public TransientSolution(
            Expression TimeStep,
            IEnumerable<SolutionSet> Solutions,
            IEnumerable<Arrow> InitialConditions)
            : this(TimeStep, Solutions, InitialConditions, new LiveParameter[] { })
        { }

        /// <summary>
        /// Check if any of the SolutionSets in this solution depend on x.
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        public bool DependsOn(Expression x) { return solutions.Any(i => i.DependsOn(x)); }

        /// <summary>
        /// Convergence threshold for the steady-state solve that produces the initial conditions.
        /// </summary>
        /// <remarks>
        /// Set here rather than in NSolve, whose own default this replaces, so that the choice is
        /// scoped to circuit solving and cannot surprise another caller of a general-purpose
        /// numerical routine.
        ///
        /// NSolve stops when the sum of the squares of the residuals falls below Epsilon squared
        /// times the number of unknowns, so this is an absolute threshold on a residual vector whose
        /// entries are a mixture of amperes and volts. That mixture is why the value has to be
        /// measured rather than reasoned about: a microamp is a generous residual on a node carrying
        /// milliamps and a meaningless one on a transistor base carrying a few microamps, where it
        /// permits an error of a fifth of the signal. NSolve's own default of 1e-6 is that second
        /// case, and it left the common emitter's bias point 1.4e-4 V from the answer.
        ///
        /// A picoamp, or a picovolt. Chosen from a sweep from 1e-6 to 1e-14 across all four
        /// reference circuits and all 53 schematics in the repository: the reference circuits stop
        /// improving at 1e-9, where the common emitter's base voltage reaches the independently
        /// computed forty-digit answer to 2.3e-12 V, and 1e-14 is the tightest value at which
        /// everything still solves. This sits three decades past the first and two above the second,
        /// so it is on neither cliff. Nothing anywhere in the repository failed to solve that did
        /// not already fail at 1e-6, and no solve measurably slowed down. See
        /// docs/stompbench-a2.5-result.md.
        /// </remarks>
        public const double SteadyStateEpsilon = 1e-12;

        /// <summary>
        /// Solve the circuit for transient simulation.
        /// </summary>
        /// <param name="Analysis">Analysis from the circuit to solve.</param>
        /// <param name="TimeStep">Discretization timestep.</param>
        /// <param name="Log">Where to send output.</param>
        /// <param name="Epsilon">Convergence threshold for the steady-state solve. See <see cref="SteadyStateEpsilon"/>.</param>
        /// <returns>TransientSolution describing the solution of the circuit.</returns>
        public static TransientSolution Solve(Analysis Analysis, Expression TimeStep, IEnumerable<Arrow> InitialConditions, ILog Log, double Epsilon = SteadyStateEpsilon)
        {
            Expression h = TimeStep;

            Log.WriteLine(MessageType.Info, "Building solution for h={0}", TimeStep.ToString());

            // Analyze the circuit to get the MNA system and unknowns.
            List<Equal> mna = Analysis.Equations.ToList();
            List<Expression> y = Analysis.Unknowns.ToList();
            LogExpressions(Log, MessageType.Verbose, "System of " + mna.Count + " equations and " + y.Count + " unknowns = {{ " + String.Join(", ", y) + " }}", mna);

            // Evaluate for simulation functions.
            // Define T = step size.
            DynamicNamespace globals = new DynamicNamespace();
            globals.Add("T", h);
            // Define d[t] = delta function.
            // TODO: This should probably be centered around 0, and also have an integral of 1 (i.e. a height of 1 / h).
            globals.Add(ExprFunction.New("d", Call.If((0 <= t) & (t < h), 1, 0), t));
            // Define u[t] = step function.
            globals.Add(ExprFunction.New("u", Call.If(t >= 0, 1, 0), t));
            mna = mna.Resolve(Analysis).Resolve(globals).OfType<Equal>().ToList();

            // Find out what variables have differential relationships.
            List<Expression> dy_dt = y.Where(i => mna.Any(j => j.DependsOn(D(i, t)))).Select(i => D(i, t)).ToList();
            Log.WriteLine(MessageType.Verbose, "Differential unknowns: {0}", String.Join(", ", dy_dt));

            // Find steady state solution for initial conditions.
            List<Arrow> initial = InitialConditions.ToList();
            Log.WriteLine(MessageType.Info, "Performing steady state analysis...");
            LogExpressions(Log, MessageType.Verbose, "Initial conditions for solve:", initial);
            LogExpressions(Log, MessageType.Verbose, "Initial conditions from analysis:", Analysis.InitialConditions);

            SystemOfEquations dc = new SystemOfEquations(mna
                // Live parameters are numbers here and symbols in the transient system below. That
                // asymmetry is deliberate and is what makes a live parameter affordable at all. The
                // steady state is found by NSolve, a numerical routine, so a free symbol in this
                // system is not something it can carry — it would have to solve for the operating
                // point as a function of every knob, which is neither cheap nor what is wanted. The
                // operating point a simulation starts from is the one belonging to the circuit as it
                // was loaded, which is the state it is in, and turning a knob afterwards moves the
                // circuit away from that point through the transient equations exactly as turning a
                // real one does.
                .Substitute(Analysis.BakedParameters)
                // Derivatives, t, and T are zero in the steady state.
                .Substitute(dy_dt.Select(i => Arrow.New(i, 0)).Append(Arrow.New(t, 0), Arrow.New(T, 0), SinglePoleSwitch.IncludeOpen))
                // Use the initial conditions from analysis.
                .Substitute(Analysis.InitialConditions)
                // Evaluate variables at t=0.
                .OfType<Equal>(), y.Select(j => j.Substitute(t, 0)));

            // A parameter that survived the substitution above would reach NSolve as an unknown it
            // was never given, and what NSolve does then is find a root of a system that is missing
            // an equation — a plausible number that is not the circuit's operating point. Caught
            // here, where the cause is one line away, rather than left to be discovered as a bias
            // point that is wrong in the fourth digit.
            Expression[] symbols = Analysis.Parameters.Select(i => (Expression)i.Symbol).ToArray();
            if (symbols.Any() && dc.DependsOn(symbols))
            {
                throw new Exception(
                    "The steady-state system still depends on a live parameter after substituting " +
                    "every one of them. It cannot be solved numerically in that state.");
            }

            // Solve partitions independently.
            foreach (SystemOfEquations i in dc.Partition())
            {
                LogExpressions(Log, MessageType.Verbose, "Steady state system for partition:", i.Select(j => Equal.New(j, 0)));
                try
                {
                    List<Arrow> part = i.Equations.Select(j => Equal.New(j, 0)).NSolve(i.Unknowns.Select(j => Arrow.New(j, 0)), Epsilon, 64);
                    initial.AddRange(part);
                    LogExpressions(Log, MessageType.Verbose, "Initial conditions:", part); 
                }
                catch (Exception Ex)
                {
                    // <b>The message, rather than only the fact.</b> This used to say a partition had
                    // failed and nothing about why, which makes the one circuit somebody is looking
                    // at the one circuit the log cannot help with: "did not converge" and "the
                    // matrix is singular" are different problems with different fixes, and telling
                    // them apart meant editing the core and rebuilding.
                    Log.WriteLine(MessageType.Warning,
                        "Failed to find partition initial conditions, simulation may be unstable: " + Ex.Message);
                }
            }

            // Transient analysis of the system.
            Log.WriteLine(MessageType.Info, "Performing transient analysis...");

            // What the row reduction should use to judge the size of a pivot it cannot evaluate.
            //
            // SystemOfEquations picks pivots by magnitude, and scores anything that is not a number
            // as zero. That is a sound default for a Newton system, whose entries depend on the
            // unknowns and have no size until the simulation runs. It is badly wrong for a live
            // parameter, which has a perfectly good size — the value the circuit was analyzed at —
            // and it does not merely mean the pivots are chosen arbitrarily: a numeric entry of
            // 1e-30 outscores a symbolic entry worth 1e6, so the elimination actively prefers the
            // worse pivot and then divides by it. Milestone A4 found every circuit with a
            // potentiometer diverging within one block for this reason, including passive tone
            // stacks whose behaviour is a linear recurrence and cannot go unstable on its own.
            //
            // PivotConditions is the mechanism SystemOfEquations already provides for exactly this,
            // and nothing had ever passed it. Given the parameters' analyzed values, a symbolic
            // entry scores its true magnitude at that operating point, which is the same number the
            // baked solve scores, so the two choose the same pivots.
            //
            // Null rather than an empty list when there is nothing to substitute, so that a circuit
            // with no live parameters takes a code path that provably cannot differ from the one it
            // took before this milestone.
            List<Arrow> pivotConditions = Analysis.BakedParameters.ToList();
            IEnumerable<Arrow> pivots = pivotConditions.Count > 0 ? pivotConditions : null;

            SystemOfEquations system = new SystemOfEquations(mna.Substitute(SinglePoleSwitch.ExcludeOpen).OfType<Equal>(), dy_dt.Concat(y));

            // Solve the diff eq for dy/dt and integrate the results.
            system.RowReduce(dy_dt, PivotConditions: pivots);
            system.BackSubstitute(dy_dt);
            LogExpressions(Log, MessageType.Verbose, "Differential equations:", system.Where(i => i.DependsOn(dy_dt)).Select(i => Equal.New(i, 0)));
            IEnumerable<Equal> integrated = system.Solve(dy_dt)
                .NDIntegrate(t, h, IntegrationMethod.BackwardDifferenceFormula2)
                .Select(i => Equal.New(i.Left, i.Right)).Buffer();
            system.AddRange(integrated);
            LogExpressions(Log, MessageType.Verbose, "Integrated solutions:", integrated);

            LogExpressions(Log, MessageType.Verbose, "Discretized system:", system.Select(i => Equal.New(i, 0)));

            if (system.DependsOn(dy_dt))
                throw new Exception("Failed to eliminate differentials from system of equations.");

            // Solving the system...
            List<SolutionSet> solutions = new List<SolutionSet>();

            // Partition the system into independent systems of equations.
            foreach (SystemOfEquations F in system.Partition())
            {
                Log.WriteLine(MessageType.Verbose, "Partition unknowns: {0}", String.Join(", ", F.Unknowns));
                // Find linear solutions for y. Linear systems should be completely solved here.
                F.RowReduce(PivotConditions: pivots);
                IEnumerable<Arrow> linear = F.Solve();
                if (linear.Any())
                {
                    linear = Factor(linear);
                    solutions.Add(new LinearSolutions(linear));
                    LogExpressions(Log, MessageType.Verbose, "Linear solutions:", linear);
                }

                // If there are any variables left, there are some non-linear equations requiring numerical techniques to solve.
                if (F.Unknowns.Any())
                {
                    // The variables of this system are the newton iteration updates.
                    List<Expression> dy = F.Unknowns.Select(i => NewtonIteration.Delta(i)).ToList();

                    // Compute JxF*dy + F(y0) == 0.
                    SystemOfEquations nonlinear = new SystemOfEquations(
                        F.Select(i => i.Gradient(F.Unknowns).Select(j => new KeyValuePair<Expression, Expression>(NewtonIteration.Delta(j.Key), j.Value))
                            .Append(new KeyValuePair<Expression, Expression>(1, i))),
                        dy);

                    // ly is the subset of y that can be found linearly.
                    List<Expression> ly = dy.Where(j => !nonlinear.Any(i => i[j].DependsOn(NewtonIteration.DeltaOf(j)))).ToList();

                    // Find linear solutions for dy.
                    nonlinear.RowReduce(ly, PivotConditions: pivots);
                    IEnumerable<Arrow> solved = nonlinear.Solve(ly);
                    solved = Factor(solved);

                    // Initial guess for y[t] = y[t - h].
                    IEnumerable<Arrow> guess = F.Unknowns.Select(i => Arrow.New(i, i.Substitute(t, t - h))).ToList();
                    guess = Factor(guess);

                    // Newton system equations.
                    IEnumerable<LinearCombination> equations = nonlinear.Equations.Buffer();
                    equations = Factor(equations);

                    solutions.Add(new NewtonIteration(solved, equations, nonlinear.Unknowns, guess));
                    LogExpressions(Log, MessageType.Verbose, String.Format("Non-linear Newton's method updates ({0}):", String.Join(", ", nonlinear.Unknowns)), equations.Select(i => Equal.New(i, 0)));
                    LogExpressions(Log, MessageType.Verbose, "Linear Newton's method updates:", solved);
                }
            }

            Log.WriteLine(MessageType.Info, "System solved, {0} solution sets for {1} unknowns.",
                solutions.Count,
                solutions.Sum(i => i.Unknowns.Count()));

            // Solutions from `Solve` might depend on previous solutions, so we need to make sure to emit the solutions in the order that satisifies such dependencies.
            solutions.Reverse();

            return new TransientSolution(
                h,
                solutions,
                initial,
                Analysis.Parameters);
        }
        public static TransientSolution Solve(Analysis Analysis, Expression TimeStep, ILog Log) { return Solve(Analysis, TimeStep, new Arrow[] { }, Log); }
        public static TransientSolution Solve(Analysis Analysis, Expression TimeStep) { return Solve(Analysis, TimeStep, new Arrow[] { }, new NullLog()); }

        private static IEnumerable<Arrow> Factor(IEnumerable<Arrow> x) { return x.Select(i => Arrow.New(i.Left, i.Right.Factor())).Buffer(); }
        private static IEnumerable<LinearCombination> Factor(IEnumerable<LinearCombination> x) { return x.Select(i => LinearCombination.New(i.Select(j => new KeyValuePair<Expression, Expression>(j.Key, j.Value.Factor())))).Buffer(); }

        // Shorthand for df/dx.
        protected static Expression D(Expression f, Expression x) { return Call.D(f, x); }

        // Check if x is a derivative
        protected static bool IsD(Expression f, Expression x)
        {
            if (f is Call C)
                return C.Target.Name == "D" && C.Arguments.ElementAt(1).Equals(x);
            return false;
        }

        // Logging helpers.
        private static void LogList(ILog Log, MessageType Type, string Title, IEnumerable<string> List)
        {
            if (Log is NullLog) return;
            if (List.Any())
            {
                Log.WriteLine(Type, Title);
                Log.WriteLines(Type, List.Select(i => "  " + i));
                Log.WriteLine(Type, "");
            }
        }

        private static void LogExpressions(ILog Log, MessageType Type, string Title, IEnumerable<Expression> Expressions)
        {
            LogList(Log, Type, Title, Expressions.Select(i => i.ToString()));
        }
    }
}
