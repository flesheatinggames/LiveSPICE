using ComputerAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Circuit
{
    /// <summary>
    /// Terminals reference connections to nodes.
    /// </summary>
    public class Terminal
    {
        protected Component owner;
        protected string name;
        public string Name { get { return name != null ? name : owner.Name; } set { name = value; } }
        public string Description { get { return name != null ? owner.Name + "." + name : owner.Name; } }

        // A unique function of t to use when this node isn't connected.
        protected Expression unconnected;
        private static long count = 0;

        public Terminal(Component Owner)
        {
            unconnected = Component.DependentVariable("_v" + Interlocked.Increment(ref count), Component.t);
            owner = Owner;
        }
        public Terminal(Component Owner, string Name) : this(Owner) { name = Name; }

        protected Node connectedTo;
        /// <summary>
        /// The node this terminal is connected to.
        /// </summary>
        public Node ConnectedTo
        {
            get { return connectedTo; }
            set { ConnectTo(value); }
        }

        public bool IsConnected { get { return connectedTo != null; } }

        /// <summary>
        /// Whether this terminal reaches anything other than itself.
        /// </summary>
        /// <remarks>
        /// <b>Not the same question as <see cref="IsConnected"/>, and a model that asks the wrong one
        /// gets the wrong answer every time.</b> Schematic.Build gives every unconnected terminal a
        /// node of its own before analysis begins — that is how it can carry on after warning about
        /// one — so by the time a component's Analyze runs, IsConnected is true for every terminal
        /// in the drawing, including the ones nobody wired. A model that contributes a branch only
        /// "when the pin is connected" therefore contributes it always, and the branch it adds hangs
        /// off a node with nothing else on it.
        ///
        /// That is not a cosmetic difference. An exponential between two such nodes is a subsystem
        /// whose only constraint is on the difference of two voltages, so the steady-state solve is
        /// singular in them; found in milestone C11, where an LM13700's unused output buffer — a
        /// junction and a current gain of 5500 — stopped a whole circuit from finding an operating
        /// point, and the warning said only that Newton had failed.
        ///
        /// The test is that the node has some other terminal on it. It gives the same answer before
        /// the build, when the terminal has no node at all, and after it, when it has one to itself.
        /// </remarks>
        public bool IsWired
        {
            get { return connectedTo != null && connectedTo.Connected.Skip(1).Any(); }
        }

        public Component Owner { get { return owner; } }

        /// <summary>
        /// Connect this terminal to the node.
        /// </summary>
        /// <param name="n"></param>
        /// <returns>true if the connection was changed, false if not.</returns>
        public bool ConnectTo(Node N)
        {
            if (connectedTo == N)
                return false;

            if (connectedTo != null)
                connectedTo.Disconnect(this);
            connectedTo = N;
            if (connectedTo != null)
                connectedTo.Connect(this);

            foreach (EventHandler i in connectionChanged) i(this, null);
            return true;
        }

        private List<EventHandler> connectionChanged = new List<EventHandler>();
        public event EventHandler ConnectionChanged
        {
            add { connectionChanged.Add(value); }
            remove { connectionChanged.Remove(value); }
        }

        /// <summary>
        /// Terminals can be implicitly converted to the node they are connected to.
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        public static implicit operator Node(Terminal x) { return x.ConnectedTo; }

        /// <summary>
        /// Get the voltage expression of the connected node.
        /// </summary>
        public Expression V { get { return ConnectedTo != null ? ConnectedTo.V : unconnected; } }

        public override string ToString() { return Description; }
    }
}
