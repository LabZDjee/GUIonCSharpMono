using System;
using System.Collections.Generic;
using System.Text;

namespace QiHe.Yaml.Grammar
{
    public partial class YamlParser
    {
        int position;

        public int Position
        {
            get { return position; }
            set { position = value; }
        }

        ParserInput<char> Input;

        public List<Pair<int, string>> Errors = new List<Pair<int, string>>();
        private Stack<int> ErrorStatck = new Stack<int>();

        public YamlParser() { }

        private void SetInput(ParserInput<char> input)
        {
            Input = input;
            position = 0;
        }

        private bool TerminalMatch(char terminal)
        {
            if (Input.HasInput(position))
            {
                char symbol = Input.GetInputSymbol(position);
                return terminal == symbol;
            }
            return false;
        }

        private bool TerminalMatch(char terminal, int pos)
        {
            if (Input.HasInput(pos))
            {
                char symbol = Input.GetInputSymbol(pos);
                return terminal == symbol;
            }
            return false;
        }

        private char MatchTerminal(char terminal, out bool success)
        {
            success = false;
            if (Input.HasInput(position))
            {
                char symbol = Input.GetInputSymbol(position);
                if (terminal == symbol)
                {
                    position++;
                    success = true;
                }
                return symbol;
            }
            return default(char);
        }

        private char MatchTerminalRange(char start, char end, out bool success)
        {
            success = false;
            if (Input.HasInput(position))
            {
                char symbol = Input.GetInputSymbol(position);
                if (start <= symbol && symbol <= end)
                {
                    position++;
                    success = true;
                }
                return symbol;
            }
            return default(char);
        }

        private char MatchTerminalSet(string terminalSet, bool isComplement, out bool success)
        {
            success = false;
            if (Input.HasInput(position))
            {
                char symbol = Input.GetInputSymbol(position);
                bool match = isComplement ? terminalSet.IndexOf(symbol) == -1 : terminalSet.IndexOf(symbol) > -1;
                if (match)
                {
                    position++;
                    success = true;
                }
                return symbol;
            }
            return default(char);
        }

        private string MatchTerminalString(string terminalString, out bool success)
        {
            int currrent_position = position;
            foreach (char terminal in terminalString)
            {
                MatchTerminal(terminal, out success);
                if (!success)
                {
                    position = currrent_position;
                    return null;
                }
            }
            success = true;
            return terminalString;
        }

        private int Error(string message)
        {
            Errors.Add(new Pair<int, string>(position, message));
            return Errors.Count;
        }

        private void ClearError(int count)
        {
            Errors.RemoveRange(count, Errors.Count - count);
        }

        public string GetEorrorMessages()
        {
            StringBuilder text = new StringBuilder();
            foreach (Pair<int, string> msg in Errors)
            {
                text.Append(Input.FormErrorMessage(msg.Left, msg.Right));
                text.AppendLine();
            }
            return text.ToString();
        }

        public int GetProbableErrorLine()
        {
            int maxLeft = 0;
            foreach (Pair<int, string> msg in Errors)
            {
                if (msg.Left > maxLeft)
                {
                    maxLeft = msg.Left;
                }
            }
            ((TextInput)Input).GetLineColumnNumber(maxLeft, out int line, out _);
            return line;
        }

    }

    /// <summary>
    /// Pair
    /// </summary>
    /// <typeparam name="TLeft">The type of the left.</typeparam>
    /// <typeparam name="TRight">The type of the right.</typeparam>
    public class Pair<TLeft, TRight> : IEquatable<Pair<TLeft, TRight>>
    {
        /// <summary>
        /// 
        /// </summary>
        public TLeft Left;
        /// <summary>
        /// 
        /// </summary>
        public TRight Right;

        /// <summary>
        /// Initializes a new instance of the <see cref="Pair&lt;TLeft, TRight&gt;"/> class.
        /// </summary>
        /// <param name="left">The left.</param>
        /// <param name="right">The right.</param>
        public Pair(TLeft left, TRight right)
        {
            Left = left;
            Right = right;
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"></see> that represents the current <see cref="T:System.Object"></see>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"></see> that represents the current <see cref="T:System.Object"></see>.
        /// </returns>
        public override string ToString()
        {
            return string.Format("({0},{1})", Left, Right);
        }

        /// <summary>
        /// Serves as a hash function for a particular type. <see cref="M:System.Object.GetHashCode"></see> is suitable for use in hashing algorithms and data structures like a hash table.
        /// </summary>
        /// <returns>
        /// A hash code for the current <see cref="T:System.Object"></see>.
        /// </returns>
        public override int GetHashCode()
        {
            return Left.GetHashCode() + Right.GetHashCode();
        }

        /// <summary>
        /// Determines whether the specified <see cref="T:System.Object"></see> is equal to the current <see cref="T:System.Object"></see>.
        /// </summary>
        /// <param name="obj">The <see cref="T:System.Object"></see> to compare with the current <see cref="T:System.Object"></see>.</param>
        /// <returns>
        /// true if the specified <see cref="T:System.Object"></see> is equal to the current <see cref="T:System.Object"></see>; otherwise, false.
        /// </returns>
        public override bool Equals(object obj)
        {
            if (obj is Pair<TLeft, TRight>)
            {
                return this.Equals((Pair<TLeft, TRight>)obj);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>
        /// true if the current object is equal to the other parameter; otherwise, false.
        /// </returns>
        public bool Equals(Pair<TLeft, TRight> other)
        {
            return this.Left.Equals(other.Left) && this.Right.Equals(other.Right);
        }

        /// <summary>
        /// Implements the operator ==.
        /// </summary>
        /// <param name="one">The one.</param>
        /// <param name="other">The other.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator ==(Pair<TLeft, TRight> one, Pair<TLeft, TRight> other)
        {
            return one.Equals(other);
        }

        /// <summary>
        /// Implements the operator !=.
        /// </summary>
        /// <param name="one">The one.</param>
        /// <param name="other">The other.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator !=(Pair<TLeft, TRight> one, Pair<TLeft, TRight> other)
        {
            return !(one == other);
        }
    }
}
