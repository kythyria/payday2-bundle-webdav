using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using IEnumerator = System.Collections.IEnumerator;
using IEnumerable = System.Collections.IEnumerable;

namespace PD2BundleDavServer.Steam
{

    public class VdfNode : IEnumerable<VdfNode>
    {
        public string Name { get; set; } = "";

        public string? Condition { get; set; }
        public string? Value { get; set; }
        public IList<VdfNode> Children { get; set; } = new List<VdfNode>();

        public IEnumerator<VdfNode> GetEnumerator() => Children.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => (Children as IEnumerable).GetEnumerator();
    }

    public class VdfReader
    {
        Func<string, string>? GetOtherFile;

        string input = "";
        string currentFile = "<unknown>";
        int line = 1;
        int col = 1;
        int pos = 0;

        private bool NextChar()
        {
            if(input[pos] == '\n')
            {
                line++;
                col = 1;
            }
            pos++;
            return pos < input.Length;
        }

        public VdfReader(Func<string, string>? getOtherFile = null)
        {
            this.GetOtherFile = getOtherFile;
        }

        public VdfNode LoadString(string filename, string input)
        {
            this.input = input;
            currentFile = filename;
            line = 1;
            col = 1;

            var rootNode = new VdfNode();
            rootNode.Children = new List<VdfNode>();

            var tokenEnumerator = Tokenise().GetEnumerator();

            while(tokenEnumerator.MoveNext())
            {
                var tok = tokenEnumerator.Current;
                if (tok == null) throw new Exception("Enumerator behaving nonsensically");

                if (tok.Type == TokenType.Unquoted && (tok.Payload == "#include" || tok.Payload == "#base"))
                {
                    Throw("VDF inclusions are unimplemented");
                }
                else
                {
                    rootNode.Children.Add(LoadStringNode(tokenEnumerator));
                }
            }

            return rootNode;
        }

        VdfNode LoadStringNode(IEnumerator<Token> tokenEnumerator)
        {
            var node = new VdfNode();
            if(tokenEnumerator.Current.Type == TokenType.Conditional)
            {
                node.Condition = tokenEnumerator.Current.Payload.TrimStart('[').TrimEnd(']');
                if(!tokenEnumerator.MoveNext())
                {
                    Throw("Unexpected EOF after conditional");
                }
            }

            if(tokenEnumerator.Current.Type != TokenType.Quoted && tokenEnumerator.Current.Type != TokenType.Unquoted)
            {
                Throw("Expected a key");
            }

            node.Name = tokenEnumerator.Current.Payload;

            if (!tokenEnumerator.MoveNext())
            {
                Throw("Unexpected EOF after key");
            }

            if(tokenEnumerator.Current.Type == TokenType.Quoted || tokenEnumerator.Current.Type == TokenType.Unquoted)
            {
                node.Value = tokenEnumerator.Current.Payload;
            }
            else if(tokenEnumerator.Current.Type == TokenType.LeftBrace)
            {
                while (true)
                {
                    if (!tokenEnumerator.MoveNext())
                    {
                        Throw("Unexpected EOF inside section");
                    }
                    else if (tokenEnumerator.Current.Type == TokenType.RightBrace)
                    {
                        return node;
                    }
                    else
                    {
                        node.Children.Add(LoadStringNode(tokenEnumerator));
                    }
                }
            }
            else
            {
                Throw("Unexpected token type");
            }
            return node;
        }

        void Throw(string message) => throw new VdfParseException(message, currentFile, line, col);

        enum TokenType
        {
            SpaceComment,
            Unquoted,
            Quoted,
            Conditional,
            LeftBrace,
            RightBrace
        }

        record Token(TokenType Type, int Line, int Col, string Payload);

        private IEnumerable<Token> Tokenise()
        {
            while(pos < input.Length)
            {
                if(char.IsWhiteSpace(input[pos]))
                {
                    NextChar();
                    continue;
                }
                else if(input[pos] == '/' && input[pos+1] == '/')
                {
                    while (input[pos] != '\n' && pos < input.Length) NextChar();
                    continue;
                }
                else if(input[pos] == '{')
                {
                    yield return new Token(TokenType.LeftBrace, line, col, "{");
                    NextChar();
                    continue;
                }
                else if(input[pos] == '}')
                {
                    yield return new Token(TokenType.RightBrace, line, col, "}");
                    NextChar();
                    continue;
                }
                else if(input[pos] == '"')
                {
                    var startcol = col;
                    NextChar();
                    var sb = new StringBuilder();
                    while(true)
                    {
                        if(pos >= input.Length)
                        {
                            Throw("EOF inside quoted string");
                        }
                        else if(input[pos] == '"')
                        {
                            yield return new Token(TokenType.Quoted, line, startcol, sb.ToString());
                            NextChar();
                            break;
                        }
                        else if(input[pos] == '\\')
                        {
                            NextChar();

                            var unescaped = Unescape(input[pos]);
                            if (unescaped.HasValue) sb.Append(unescaped.Value);
                            else Throw("Unknown escape sequence");

                            NextChar();
                        }
                        else
                        {
                            sb.Append(input[pos]);
                            NextChar();
                        }
                    }
                }
                else
                {
                    var sb = new StringBuilder();
                    while(pos < input.Length)
                    {
                        if (char.IsWhiteSpace(input[pos]) || "{}\"\n".Contains(input[pos])) break;
                        sb.Append(input[pos]);
                        NextChar();
                    }
                    var tt = TokenType.Unquoted;
                    var p = sb.ToString();
                    if (p.Contains('[') && p.Contains(']')) tt = TokenType.Conditional;
                    yield return new Token(tt, line, col, p);
                }
            }
        }

        private char? Unescape(char what) => what switch
        {
            'n' => '\n',
            't' => '\t',
            'v' => '\v',
            'b' => '\b',
            'r' => '\r',
            'f' => '\f',
            'a' => '\a',
            '\\' => '\\',
            '?' => '?',
            '\'' => '\'',
            '\"' => '\"',
            _ => null
        };
    }

    public class VdfParseException : Exception
    {
        public string File { get; set; }
        public int Line { get; set; }
        public int Col { get; set; }
        public VdfParseException(string message, string file, int line, int col) : base($"{file}:{line}:{col} {message}")
        {
            File = file;
            Line = line;
            Col = col;
        }

    }
}
