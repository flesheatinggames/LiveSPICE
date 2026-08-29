using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Circuit.Spice
{
    public class TokenList : List<string>
    {
        private string text;
        public string Text { get { return text; } }

        private int lineCount = 0;
        public int LineCount { get { return lineCount; } }

        // Whitespace characters.
        private static char[] Whitespace = new char[] { ' ', '\t', '(', ')', ',', '=' };

        public TokenList(string Line)
        {
            text = Line.ToString().TrimEnd();

            foreach (string i in text.Split(Whitespace))
            {
                string tok = i.Trim().ToUpper();
                if (tok.Length == 0)
                    continue;

                // * at the beginning of the line is a comment.
                if (tok.StartsWith("*") && Count == 0)
                    return;

                // Truncate tokens at semicolon comments.
                int semi = tok.IndexOf(';');
                if (semi > 0)
                {
                    Add(tok.Substring(0, semi));
                    return;
                }
                else if (semi == 0)
                    return;

                Add(tok);
            }
        }

        /// <summary>
        /// One logical line: a line, plus every continuation line that follows it.
        /// </summary>
        /// <remarks>
        /// <b>ReadLine answers null at the end of the stream, and this used to call TrimEnd on it.</b>
        /// A file whose last line is a "+" continuation reaches exactly that: Peek sees the "+", the
        /// loop goes round, and there is nothing left to read. The null reference that followed came
        /// from here rather than from the statement being parsed, so it escaped through
        /// Statements.Parse's own loop rather than being caught by the try inside it — which meant a
        /// truncated file produced a crash with no line number on it rather than a logged error.
        /// Ending the loop instead lets the statement read so far be parsed and reported like any
        /// other.
        /// </remarks>
        public static TokenList ReadLine(StreamReader Stream)
        {
            int count = 0;
            StringBuilder line = new StringBuilder("");
            do
            {
                string l = Stream.ReadLine();
                if (l == null)
                    break;
                l = l.TrimEnd(Whitespace);
                ++count;
                if (l.StartsWith("+"))
                    l = l.Substring(1);
                line.Append(l + " ");
            } while (Stream.Peek() == '+');

            return new TokenList(line.ToString()) { lineCount = count };
        }
    }
}
