using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using Util;

namespace Circuit.Spice
{
    public class ParameterAlias : Attribute
    {
        private string alias;
        public string Alias { get { return alias; } }

        public ParameterAlias(string Alias) { alias = Alias.ToUpper(); }
    }

    /// <summary>
    /// Represents the .MODEL SPICE statement.
    /// </summary>
    public class Model : Statement
    {
        private Component component;
        /// <summary>
        /// Component representing the model.
        /// </summary>
        public Component Component { get { return component; } }

        private string desc;
        /// <summary>
        /// Log of information resulting from the import of this model.
        /// </summary>
        public string Description { get { return desc; } }

        public Model(Component Component, string Description) { component = new Specialization(Component); desc = Description; }
        public Model(Component Component) : this(Component, "") { }

        /// <summary>
        /// Mapping of SPICE model types to component templates.
        /// </summary>
        private static Dictionary<string, Component> ModelTemplates = new Dictionary<string, Component>()
        {
            ["D"] = new Diode(),
            ["NPN"] = new BipolarJunctionTransistor() { Type = BjtType.NPN },
            ["PNP"] = new BipolarJunctionTransistor() { Type = BjtType.PNP },
            ["NJF"] = new JunctionFieldEffectTransistor() { Type = JfetType.N },
            ["PJF"] = new JunctionFieldEffectTransistor() { Type = JfetType.P },
        };

        private List<string> ignored = new List<string>();
        /// <summary>
        /// The parameters this model states that the component has nowhere to put.
        /// </summary>
        /// <remarks>
        /// <b>Silence is how an imported model becomes quietly wrong.</b> A published diode carries a
        /// dozen parameters — RS, CJO, TT, BV, EG and the rest — and the component here has places for
        /// two of them. Dropping the other ten is the right thing to do, because there is nothing else
        /// to do with them; dropping them without saying so leaves somebody believing they imported a
        /// 1N4148 when what they imported is its saturation current and its emission coefficient.
        /// </remarks>
        public IEnumerable<string> Ignored { get { return ignored; } }

        /// <summary>
        /// Reads one .MODEL statement: a name, a type, and parameters in name/value pairs.
        /// </summary>
        /// <remarks>
        /// <b>Walked in pairs, where it used to step by one and read one past itself.</b> The old loop
        /// advanced a token at a time and read Tokens[i + 1] whenever Tokens[i] was a recognised
        /// parameter name, so a model whose last token was a parameter name — a truncated line, a file
        /// cut off mid-statement — indexed off the end of the list and threw. Pairs are also what the
        /// grammar actually is: the tokenizer splits on "=" as whitespace, so ".MODEL D1 D (IS=2.5N
        /// N=1.75)" arrives as name, value, name, value, and reading it that way is what makes the
        /// list of ignored parameters below correct rather than approximate.
        /// </remarks>
        public static Model Parse(TokenList Tokens)
        {
            string name = Tokens[1];
            string type = Tokens[2];

            if (!ModelTemplates.TryGetValue(type, out Component template))
                throw new NotSupportedException("Model type '" + type + "' not supported.");

            Component impl = template.Clone();
            impl.PartNumber = name;

            List<string> skipped = new List<string>();
            for (int i = 3; i < Tokens.Count; i += 2)
            {
                if (i + 1 >= Tokens.Count)
                    throw new Exception("Parameter '" + Tokens[i] + "' has no value.");

                PropertyInfo p = FindTemplateProperty(template, Tokens[i]);
                if (p == null)
                {
                    skipped.Add(Tokens[i]);
                    continue;
                }

                // A parameter this component has a place for but whose value it cannot read is
                // ignored like any other, rather than sinking the whole model. Published files
                // really do this: the 1N4148 as ON Semiconductor publishes it ends "mfg=OnSemi
                // type=silicon", and "silicon" is not a quantity — but Type is a property here, so
                // the name matches and the value does not. Losing forty transistors because one of
                // them names its manufacturer is not a trade anybody would make.
                // Read out of the list before the try, so that the try covers reading the value and
                // nothing else. Inside it, an index off the end of the list would be caught here and
                // reported as a parameter this build ignored — which is how a truncated statement
                // would go from a crash to a silent shrug without ever being right.
                string value = Tokens[i + 1];
                try
                {
                    TypeConverter tc = TypeDescriptor.GetConverter(p.PropertyType);
                    p.SetValue(impl, tc.ConvertFrom(ParseValue(value).ToString()), null);
                }
                catch (Exception)
                {
                    skipped.Add(Tokens[i]);
                }
            }

            string said = "Imported " + name + " from a SPICE model";
            if (skipped.Count > 0)
                said += "; ignored " + string.Join(", ", skipped);
            return new Model(impl, said + ".") { ignored = skipped };
        }

        private static PropertyInfo FindTemplateProperty(Component Template, string Name)
        {
            Name = Name.ToUpper();

            foreach (PropertyInfo i in Template.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                // Check all the parameter aliases for this parameter.
                foreach (ParameterAlias j in i.CustomAttributes<ParameterAlias>())
                    if (Name == j.Alias)
                        return i;

                // Check the name itself.
                if (Name == i.Name.ToUpper())
                    return i;
            }
            return null;
        }
    }
}
