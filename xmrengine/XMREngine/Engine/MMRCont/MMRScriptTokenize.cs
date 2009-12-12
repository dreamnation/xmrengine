/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;

/**
 * @brief Parse raw source file string into token list.
 *
 * Usage:
 *
 *    emsg = some function to output error messages to
 *    source = string containing entire source file
 *
 *    TokenBegin tokenBegin = TokenBegin.Construct (emsg, source);
 *
 *    tokenBegin = null: tokenizing error
 *                 else: first (dummy) token in file
 *                       the rest are chained by nextToken,prevToken
 *                       final token is always a (dummy) TokenEnd
 */

using System;
using System.Collections.Generic;

namespace MMR {

	public delegate void TokenErrorMessage (Token token, string message);
	public delegate void TokenOutputString (string message);

	/**
	 * @brief base class for all tokens
	 */
	public class Token {
		public static readonly int MAX_NAME_LEN = 255;
		public static readonly int MAX_STRING_LEN = 4096;

		public Token nextToken;
		public Token prevToken;

		// used for error message printing
		public TokenErrorMessage emsg;
		public int line;
		public int posn;

		/**
		 * @brief construct a token coming directly from a source file
		 * @param emsg = object that error messages get sent to
		 * @param line = source file line number
		 * @param posn = token's position within that source line
		 */
		public Token (TokenErrorMessage emsg, int line, int posn)
		{
			this.emsg = emsg;
			this.line = line;
			this.posn = posn;
		}

		/**
		 * @brief construct a token with same error message parameters
		 * @param original = original token to create from
		 */
		public Token (Token original)
		{
			if (original != null) {
				this.emsg = original.emsg;
				this.line = original.line;
				this.posn = original.posn;
			}
		}

		/**
		 * @brief output an error message associated with this token
		 *        sends the message to the token's error object
		 * @param message = error message string
		 */
		public void ErrorMsg (string message)
		{
			if (emsg != null) {
				emsg (this, message);
			}
		}

		/**
		 * @brief output token as a string.
		 * @param writer = routine to do the writing
		 */
		public const int INDENTSPACES = 3;
		public void OutputStr (TokenOutputString output, int indent)
		{
			System.Text.StringBuilder s = new System.Text.StringBuilder ("".PadRight (indent));
			s.Append (this.line.ToString ().PadLeft (4));
			s.Append (".");
			s.Append (this.posn.ToString ().PadLeft (4));
			s.Append (this.GetType ().ToString ().PadLeft (18));
			s.Append ("  ");
			s.Append (this.ToString ());
			output (s.ToString ());
		}
	}


	/**
	 * @brief token that begins a source file
	 *        Along with TokenEnd, it keeps insertion/removal of intermediate tokens
	 *        simple as the intermediate tokens always have non-null nextToken,prevToken.
	 */
	public class TokenBegin : Token {

		private bool youveAnError;  // there was some error tokenizing
		private int eolIdx;         // index in 'source' at end of previous line
		private int lineNo;         // current line in source file, starting at 1
		private string source;      // the whole script source code
		private Token lastToken;    // last token created so far
		private bool optionArrays;  // has seen 'XMROption arrays;'

		/**
		 * @brief convert a source file in the form of a string
		 *        to a list of raw tokens
		 * @param emsg   = where to output messages to
		 * @param source = whole source file contents
		 * @returns null: conversion error, message already output
		 *          else: list of tokens, starting with TokenBegin, ending with TokenEnd.
		 */
		public static TokenBegin Construct (TokenErrorMessage emsg, string source)
		{
			BuildDelimeters();
			BuildKeywords();

			TokenBegin tokenBegin = new TokenBegin (emsg, 0, 0);
			tokenBegin.lastToken  = tokenBegin;
			tokenBegin.source     = source;
			tokenBegin.Tokenize ();
			if (tokenBegin.youveAnError) return null;
			tokenBegin.AppendToken (new TokenEnd (emsg, ++ tokenBegin.lineNo, 0));
			return tokenBegin;
		}

		private TokenBegin (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { }

		public override string ToString() { return "BOF"; }

		/*
		 * Produces raw token stream: names, numbers, strings, keywords/delimeters.
		 * @param this.source = whole source file in one string
		 * @returns this.nextToken = filled in with tokens
		 *          this.youveAnError = true: some tokenizing error
		 *                             false: successful
		 */
		private void Tokenize ()
		{
			youveAnError = false;
			eolIdx = -1;
			lineNo =  0;
			for (int i = 0; i < source.Length; i ++) {
				char c = source[i];
				if (c == '\n') {
					lineNo ++;
					eolIdx = i;
					continue;
				}

				/*
				 * Skip over whitespace.
				 */
				if (c <= ' ') continue;

				/*
				 * Skip over comments.
				 */
				if ((i + 2 <= source.Length) && source.Substring (i, 2).Equals ("//")) {
					while ((i < source.Length) && (source[i] != '\n')) i ++;
					lineNo ++;
					eolIdx = i;
					continue;
				}
				if ((i + 2 <= source.Length) && (source.Substring (i, 2).Equals ("/*"))) {
					while ((i + 1 < source.Length) && (((c = source[i]) != '*') || (source[i+1] != '/'))) {
						if (c == '\n') {
							lineNo ++;
							eolIdx = i;
						}
						i ++;
					}
					i ++;
					continue;
				}

				/*
				 * Check for numbers.
				 */
				if ((c >= '0') && (c <= '9')) {
					int j = TryParseFloat (i);
					if (j == 0) j = TryParseInt (i);
					i = -- j;
					continue;
				}

				/*
				 * Check for quoted strings.
				 */
				if (c == '"') {
					bool backslash;
					int j;

					backslash = false;
					for (j = i; ++ j < source.Length;) {
						c = source[j];
						if (c == '\\') {
							backslash = true;
						} else if (c == '\n') {
							TokenError (i, "string runs off end of line");
							lineNo ++;
							eolIdx = j;
							break;
						} else {
							if (!backslash && (c == '"')) break;
							backslash = false;
						}
					}
					if (j - i > MAX_STRING_LEN) {
						TokenError (i, "string too long, max " + MAX_STRING_LEN);
					} else {
						AppendToken (new TokenStr (emsg, lineNo, i - eolIdx, source.Substring (i + 1, j - i - 1)));
					}
					i = j;
					continue;
				}

				/*
				 * Check for keywords/names.
				 */
				if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c == '_')) {
					int j;

					for (j = i; ++ j < source.Length;) {
						c = source[j];
						if (c >= 'a' && c <= 'z') continue;
						if (c >= 'A' && c <= 'Z') continue;
						if (c >= '0' && c <= '9') continue;
						if (c != '_') break;
					}
					if (j - i > MAX_NAME_LEN) {
						TokenError (i, "name too long, max " + MAX_NAME_LEN);
					} else {
						string name = source.Substring (i, j - i);
						if (keywords.ContainsKey (name)) {
							Object[] args = new Object[] { emsg, lineNo, i - eolIdx };
							AppendToken ((Token)keywords[name].Invoke (args));
						} else if (optionArrays && arrayKeywords.ContainsKey (name)) {
							Object[] args = new Object[] { emsg, lineNo, i - eolIdx };
							AppendToken ((Token)arrayKeywords[name].Invoke (args));
						} else {
							AppendToken (new TokenName (emsg, lineNo, i - eolIdx, name));
						}
					}
					i = -- j;
					continue;
				}

				/*
				 * Check for option enables.
				 */
				if ((c == ';') && (lastToken is TokenName) && 
				                  (((TokenName)lastToken).val == "arrays") &&
				                  (lastToken.prevToken is TokenName) && 
				                  (((TokenName)lastToken.prevToken).val == "XMROption")) {
					optionArrays = true;
					lastToken = lastToken.prevToken.prevToken;
					lastToken.nextToken = null;
					continue;
				}

				/*
				 * Lastly, check for delimeters.
				 */
				{
					int j;
					int len = 0;

					for (j = 0; j < delims.Length; j ++) {
						len = delims[j].str.Length;
						if ((i + len <= source.Length) && (source.Substring (i, len).Equals (delims[j].str))) break;
					}
					if (j < delims.Length) {
						Object[] args = { emsg, lineNo, i - eolIdx };
						Token kwToken = (Token)delims[j].ctorInfo.Invoke (args);
						AppendToken (kwToken);
						i += -- len;
						continue;
					}
				}

				/*
				 * Don't know what it is!
				 */
				TokenError (i, "unknown character '" + c + "'");
			}
		}

		/**
		 * @brief try to parse a floating-point number from the source
		 * @param i = starting position within this.source of number
		 * @returns 0: not a floating point number, try something else
		 *       else: position in this.source of terminating character, ie, past number
		 *             TokenFloat appended to token list
		 *             or error message has been output
		 */
		private int TryParseFloat (int i)
		{
			bool decimals, error, negexp, nulexp;
			char c;
			double f, f10;
			int exponent, j, x, y;
			ulong m, mantissa;

			decimals = false;
			error    = false;
			exponent = 0;
			mantissa = 0;
			for (j = i; j < source.Length; j ++) {
				c = source[j];
				if ((c >= '0') && (c <= '9')) {
					m = mantissa * 10 + (ulong)(c - '0');
					if (m / 10 != mantissa) {
						if (!decimals) exponent ++;
					} else {
						mantissa = m;
						if (decimals) exponent --;
					}
					continue;
				}
				if (c == '.') {
					if (decimals) return j;
					decimals = true;
					continue;
				}
				if ((c == 'E') || (c == 'e')) {
					if (++ j >= source.Length) {
						TokenError (i, "floating exponent off end of source");
						return j;
					}
					c = source[j];
					negexp = (c == '-');
					if (negexp || (c == '+')) j ++;
					y = 0;
					nulexp = true;
					for (; j < source.Length; j ++) {
						c = source[j];
						if ((c < '0') || (c > '9')) break;
						x = y * 10 + (c - '0');
						if (x / 10 != y) {
							if (!error) TokenError (i, "floating exponent overflow");
							error = true;
						}
						y = x;
						nulexp = false;
					}
					if (nulexp) {
						TokenError (i, "bad or missing floating exponent");
						return j;
					}
					if (negexp) {
						x = exponent - y;
						if (x > exponent) {
							if (!error) TokenError (i, "floating exponent overflow");
							error = true;
						}
					} else {
						x = exponent + y;
						if (x < exponent) {
							if (!error) TokenError (i, "floating exponent overflow");
							error = true;
						}
					}
					exponent = x;
				}
				break;
			}
			if (!decimals) {
				return 0;
			}

			f = mantissa;
			if ((exponent != 0) && (mantissa != 0) && !error) {
				f10 = 10.0;
				if (exponent < 0) {
					exponent = -exponent;
					while (exponent > 0) {
						if ((exponent & 1) != 0) {
							f /= f10;
						}
						exponent /= 2;
						f10 *= f10;
					}
				} else {
					while (exponent > 0) {
						if ((exponent & 1) != 0) {
							f *= f10;
						}
						exponent /= 2;
						f10 *= f10;
					}
				}
			}
			if (!error) {
				AppendToken (new TokenFloat (emsg, lineNo, i - eolIdx, f));
			}
			return j;
		}

		/**
		 * @brief try to parse an integer number from the source
		 * @param i = starting position within this.source of number
		 * @returns 0: not an integer number, try something else
		 *       else: position in this.source of terminating character, ie, past number
		 *             TokenInt appended to token list
		 *             or error message has been output
		 */
		private int TryParseInt (int i)
		{
			bool error;
			char c;
			int j, m, mantissa;

			error    = false;
			mantissa = 0;
			for (j = i; j < source.Length; j ++) {
				c = source[j];
				if ((c >= '0') && (c <= '9')) {
					m = mantissa * 10 + (c - '0');
					if (m / 10 != mantissa) {
						if (!error) TokenError (i, "integer overflow");
						error = true;
					}
					mantissa = m;
					continue;
				}
				break;
			}
			if (!error) {
				AppendToken (new TokenInt (emsg, lineNo, i - eolIdx, mantissa));
			}
			return j;
		}

		/**
		 * @brief append token on to end of list
		 * @param newToken = token to append
		 * @returns with token appended onto this.lastToken
		 */
		private void AppendToken (Token newToken)
		{
			newToken.nextToken  = null;
			newToken.prevToken  = lastToken;
			lastToken.nextToken = newToken;
			lastToken           = newToken;
		}

		/**
		 * @brief print tokenizing error message
		 *        and remember that we've an error
		 * @param i = position within source file of the error
		 * @param message = error message text
		 * @returns with this.youveAnError set
		 */
		private void TokenError (int i, string message)
		{
			Token temp = new Token (this.emsg, this.lineNo, i - this.eolIdx);
			temp.ErrorMsg (message);
			youveAnError = true;
		}

		/**
		 * @brief get a token's constructor
		 * @param tokenType = token's type
		 * @returns token's constructor
		 */
		private static Type[] constrTypes = new Type[] {
			typeof (TokenErrorMessage), typeof (int), typeof (int)
		};

		private static System.Reflection.ConstructorInfo GetTokenCtor (Type tokenType)
		{
			return tokenType.GetConstructor (constrTypes);
		}

		/**
		 * @brief delimeter table
		 */
		private static void BuildDelimeters () { }

		private class Delim {
			public string str;
			public System.Reflection.ConstructorInfo ctorInfo;
			public Delim (string str, Type type)
			{
				this.str = str;
				ctorInfo = GetTokenCtor (type);
			}
		}

		private static Delim[] delims = new Delim[] {
			new Delim ("<<=", typeof (TokenKwAsnLSh)),
			new Delim (">>=", typeof (TokenKwAsnRSh)),
			new Delim ("<=",  typeof (TokenKwCmpLE)),
			new Delim (">=",  typeof (TokenKwCmpGE)),
			new Delim ("==",  typeof (TokenKwCmpEQ)),
			new Delim ("!=",  typeof (TokenKwCmpNE)),
			new Delim ("++",  typeof (TokenKwIncr)),
			new Delim ("--",  typeof (TokenKwDecr)),
			new Delim ("&&",  typeof (TokenKwAndAnd)),
			new Delim ("||",  typeof (TokenKwOrOr)),
			new Delim ("+=",  typeof (TokenKwAsnAdd)),
			new Delim ("&=",  typeof (TokenKwAsnAnd)),
			new Delim ("-=",  typeof (TokenKwAsnSub)),
			new Delim ("*=",  typeof (TokenKwAsnMul)),
			new Delim ("/=",  typeof (TokenKwAsnDiv)),
			new Delim ("%=",  typeof (TokenKwAsnMod)),
			new Delim ("|=",  typeof (TokenKwAsnOr)),
			new Delim ("^=",  typeof (TokenKwAsnXor)),
			new Delim ("<<",  typeof (TokenKwLSh)),
			new Delim (">>",  typeof (TokenKwRSh)),
			new Delim ("~",   typeof (TokenKwTilde)),
			new Delim ("!",   typeof (TokenKwExclam)),
			new Delim ("@",   typeof (TokenKwAt)),
			new Delim ("%",   typeof (TokenKwMod)),
			new Delim ("^",   typeof (TokenKwXor)),
			new Delim ("&",   typeof (TokenKwAnd)),
			new Delim ("*",   typeof (TokenKwMul)),
			new Delim ("(",   typeof (TokenKwParOpen)),
			new Delim (")",   typeof (TokenKwParClose)),
			new Delim ("-",   typeof (TokenKwSub)),
			new Delim ("+",   typeof (TokenKwAdd)),
			new Delim ("=",   typeof (TokenKwAssign)),
			new Delim ("{",   typeof (TokenKwBrcOpen)),
			new Delim ("}",   typeof (TokenKwBrcClose)),
			new Delim ("[",   typeof (TokenKwBrkOpen)),
			new Delim ("]",   typeof (TokenKwBrkClose)),
			new Delim (";",   typeof (TokenKwSemi)),
			new Delim (":",   typeof (TokenKwColon)),
			new Delim ("<",   typeof (TokenKwCmpLT)),
			new Delim (">",   typeof (TokenKwCmpGT)),
			new Delim (",",   typeof (TokenKwComma)),
			new Delim (".",   typeof (TokenKwDot)),
			new Delim ("?",   typeof (TokenKwQMark)),
			new Delim ("/",   typeof (TokenKwDiv)),
			new Delim ("|",   typeof (TokenKwOr))
		};

		/**
		 * @brief keyword table
		 *        The keyword table translates a keyword string
		 *        to the corresponding token constructor.
		 */
		private static void BuildKeywords ()
		{
			if (keywords == null) {
				Dictionary<string, System.Reflection.ConstructorInfo> kws = new Dictionary<string, System.Reflection.ConstructorInfo> ();

				kws.Add ("default",  GetTokenCtor (typeof (TokenKwDefault)));
				kws.Add ("do",       GetTokenCtor (typeof (TokenKwDo)));
				kws.Add ("else",     GetTokenCtor (typeof (TokenKwElse)));
				kws.Add ("float",    GetTokenCtor (typeof (TokenTypeFloat)));
				kws.Add ("for",      GetTokenCtor (typeof (TokenKwFor)));
				kws.Add ("if",       GetTokenCtor (typeof (TokenKwIf)));
				kws.Add ("integer",  GetTokenCtor (typeof (TokenTypeInt)));
				kws.Add ("list",     GetTokenCtor (typeof (TokenTypeList)));
				kws.Add ("jump",     GetTokenCtor (typeof (TokenKwJump)));
				kws.Add ("key",      GetTokenCtor (typeof (TokenTypeKey)));
				kws.Add ("return",   GetTokenCtor (typeof (TokenKwRet)));
				kws.Add ("rotation", GetTokenCtor (typeof (TokenTypeRot)));
				kws.Add ("state",    GetTokenCtor (typeof (TokenKwState)));
				kws.Add ("string",   GetTokenCtor (typeof (TokenTypeStr)));
				kws.Add ("vector",   GetTokenCtor (typeof (TokenTypeVec)));
				kws.Add ("while",    GetTokenCtor (typeof (TokenKwWhile)));

				//MB();
				keywords = kws;
			}

			if (arrayKeywords == null) {
				Dictionary<string, System.Reflection.ConstructorInfo> kws = new Dictionary<string, System.Reflection.ConstructorInfo> ();

				kws.Add ("array",   GetTokenCtor (typeof (TokenTypeArray)));
				kws.Add ("foreach", GetTokenCtor (typeof (TokenKwForEach)));
				kws.Add ("in",      GetTokenCtor (typeof (TokenKwIn)));
				kws.Add ("is",      GetTokenCtor (typeof (TokenKwIs)));
				kws.Add ("object",  GetTokenCtor (typeof (TokenTypeObject)));
				kws.Add ("undef",   GetTokenCtor (typeof (TokenKwUndef)));

				//MB();
				arrayKeywords = kws;
			}
			//MB();
		}

		private static Dictionary<string, System.Reflection.ConstructorInfo> keywords = null;
		private static Dictionary<string, System.Reflection.ConstructorInfo> arrayKeywords = null;
	}



	/**
	 * @brief All output token types in addition to TokenBegin.
	 *        They are all sub-types of Token.
	 */

	public class TokenFloat : Token {
		public double val;
		public TokenFloat (TokenErrorMessage emsg, int line, int posn, double val) : base (emsg, line, posn)
		{
			this.val = val;
		}
		public override string ToString ()
		{
			string s;
			s  = this.val.ToString ();
			s += 'f';
			return s;
		}
	}

	public class TokenInt : Token {
		public int val;
		public TokenInt (TokenErrorMessage emsg, int line, int posn, int val) : base (emsg, line, posn)
		{
			this.val = val;
		}
		public override string ToString()
		{
			return this.val.ToString();
		}
	}

	public class TokenName : Token {
		public string val;
		public TokenName (TokenErrorMessage emsg, int line, int posn, string val) : base (emsg, line, posn)
		{
			this.val = val;
		}
		public TokenName (Token original, string val) : base (original)
		{
			this.val = val;
		}
		public override string ToString()
		{
			return this.val.ToString();
		}
	}

	public class TokenStr : Token {
		public string val;
		public TokenStr (TokenErrorMessage emsg, int line, int posn, string val) : base (emsg, line, posn)
		{
			this.val = val;
		}
		public override string ToString()
		{
			int i, j;
			System.Text.StringBuilder s = new System.Text.StringBuilder ("\"");
			j = 0;
			while ((i = val.IndexOf ('"', j)) >= 0) {
				s.Append (val.Substring (j, i - j));
				s.Append ("\\\"");
				j = ++ i;
			}
			s.Append (val.Substring (j));
			s.Append ("\"");
			return s.ToString();
		}
	}

	/*
	 * This one marks the end-of-file.
	 */
	public class TokenEnd : Token { public TokenEnd(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "EOF"; } }

	/*
	 * Various keywords and delimeters.
	 */
	public class TokenKwAsnLSh : Token { public TokenKwAsnLSh(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "<<="; } }
	public class TokenKwAsnRSh : Token { public TokenKwAsnRSh(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return ">>="; } }
	public class TokenKwCmpLE : Token { public TokenKwCmpLE(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "<="; } }
	public class TokenKwCmpGE : Token { public TokenKwCmpGE(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return ">="; } }
	public class TokenKwCmpEQ : Token { public TokenKwCmpEQ (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "=="; } }
	public class TokenKwCmpNE : Token { public TokenKwCmpNE (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "!="; } }
	public class TokenKwIncr : Token { public TokenKwIncr (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "++"; } }
	public class TokenKwDecr : Token { public TokenKwDecr(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "--"; } }
	public class TokenKwAndAnd : Token { public TokenKwAndAnd(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "&&"; } }
	public class TokenKwOrOr : Token { public TokenKwOrOr(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "||"; } }
	public class TokenKwAsnAdd : Token { public TokenKwAsnAdd(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "+="; } }
	public class TokenKwAsnAnd : Token { public TokenKwAsnAnd (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "&="; } }
	public class TokenKwAsnSub : Token { public TokenKwAsnSub (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "-="; } }
	public class TokenKwAsnMul : Token { public TokenKwAsnMul(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "*="; } }
	public class TokenKwAsnDiv : Token { public TokenKwAsnDiv(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "/="; } }
	public class TokenKwAsnMod : Token { public TokenKwAsnMod(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "%="; } }
	public class TokenKwAsnOr : Token { public TokenKwAsnOr (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "|="; } }
	public class TokenKwAsnXor : Token { public TokenKwAsnXor (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "^="; } }
	public class TokenKwLSh : Token { public TokenKwLSh (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "<<"; } }
	public class TokenKwRSh : Token { public TokenKwRSh(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return ">>"; } }
	public class TokenKwTilde : Token { public TokenKwTilde(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "~"; } }
	public class TokenKwExclam : Token { public TokenKwExclam(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "!"; } }
	public class TokenKwAt : Token { public TokenKwAt(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "@"; } }
	public class TokenKwMod : Token { public TokenKwMod(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "%"; } }
	public class TokenKwXor : Token { public TokenKwXor(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "^"; } }
	public class TokenKwAnd : Token { public TokenKwAnd(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "&"; } }
	public class TokenKwMul : Token { public TokenKwMul(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "*"; } }
	public class TokenKwParOpen : Token { public TokenKwParOpen(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "("; } }
	public class TokenKwParClose : Token { public TokenKwParClose(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return ")"; } }
	public class TokenKwSub : Token { public TokenKwSub(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "-"; } }
	public class TokenKwAdd : Token { public TokenKwAdd(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "+"; } }
	public class TokenKwAssign : Token { public TokenKwAssign(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "="; } }
	public class TokenKwBrcOpen : Token { public TokenKwBrcOpen(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "{"; } }
	public class TokenKwBrcClose : Token { public TokenKwBrcClose(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "}"; } }
	public class TokenKwBrkOpen : Token { public TokenKwBrkOpen(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "["; } }
	public class TokenKwBrkClose : Token { public TokenKwBrkClose(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "]"; } }
	public class TokenKwSemi : Token { public TokenKwSemi(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return ";"; } }
	public class TokenKwColon : Token { public TokenKwColon(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return ":"; } }
	public class TokenKwCmpLT : Token { public TokenKwCmpLT(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "<"; } }
	public class TokenKwCmpGT : Token { public TokenKwCmpGT(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return ">"; } }
	public class TokenKwComma : Token { public TokenKwComma(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return ","; } }
	public class TokenKwDot : Token { public TokenKwDot(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "."; } }
	public class TokenKwQMark : Token { public TokenKwQMark(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "?"; } }
	public class TokenKwDiv : Token { public TokenKwDiv(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "/"; } }
	public class TokenKwOr : Token { public TokenKwOr(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "|"; } }

	public class TokenKwContains : Token { public TokenKwContains (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "continue"; } }
	public class TokenKwDefault : Token { public TokenKwDefault(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "default"; } }
	public class TokenKwDo : Token { public TokenKwDo(TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "do"; } }
	public class TokenKwElse : Token { public TokenKwElse (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "else"; } }
	public class TokenKwFor : Token { public TokenKwFor (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "for"; } }
	public class TokenKwForEach : Token { public TokenKwForEach (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "for"; } }
	public class TokenKwIf : Token { public TokenKwIf (TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "if"; } }
	public class TokenKwIn : Token { public TokenKwIn (TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "if"; } }
	public class TokenKwIs : Token { public TokenKwIs (TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "if"; } }
	public class TokenKwJump : Token { public TokenKwJump (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "jump"; } }
	public class TokenKwRet : Token { public TokenKwRet (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "return"; } }
	public class TokenKwState : Token { public TokenKwState (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "state"; } }
	public class TokenKwUndef : Token { public TokenKwUndef (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn) { } public override string ToString () { return "undef"; } }
	public class TokenKwWhile : Token { public TokenKwWhile (TokenErrorMessage emsg, int line, int posn) : base(emsg, line, posn) { } public override string ToString() { return "while"; } }

	/*
	 * Various datatypes.
	 */
	public class TokenType : Token {
		public System.Type typ;
		public System.Type lslBoxing;  // null: normal
		                               // else: LSL-style boxing, ie, LSL_Integer, LSL_Float
		                               //       typ=System.Int32; lslBoxing=LSL_Integer
		                               //       typ=System.Float; lslBoxing=LSL_Float

		public TokenType (TokenErrorMessage emsg, int line, int posn, System.Type typ) : base (emsg, line, posn)
		{
			this.typ = typ;
		}
		public TokenType (Token original, System.Type typ) : base (original)
		{
			this.typ = typ;
		}
		public static TokenType FromSysType (Token original, System.Type typ)
		{
			if (typ == typeof (LSL_List)) return new TokenTypeList (original);
			if (typ == typeof (LSL_Rotation)) return new TokenTypeRot (original);
			if (typ == typeof (void)) return new TokenTypeVoid (original);
			if (typ == typeof (LSL_Vector)) return new TokenTypeVec (original);
			if (typ == typeof (float)) return new TokenTypeFloat (original);
			if (typ == typeof (int)) return new TokenTypeInt (original);
			if (typ == typeof (LSL_Key)) return new TokenTypeKey (original);
			if (typ == typeof (string)) return new TokenTypeStr (original);
			if (typ == typeof (double)) return new TokenTypeFloat (original);
			if (typ == typeof (bool)) return new TokenTypeBool (original);
			if (typ == typeof (object)) return new TokenTypeObject (original);
			if (typ == typeof (XMR_Array)) return new TokenTypeArray (original);

			if (typ == typeof (LSL_Integer)) {
				TokenType tokenType = new TokenTypeInt (original);
				tokenType.lslBoxing = typ;
				return tokenType;
			}
			if (typ == typeof (LSL_Float)) {
				TokenType tokenType = new TokenTypeFloat (original);
				tokenType.lslBoxing = typ;
				return tokenType;
			}

			throw new Exception ("unknown type " + typ.ToString ());
		}

		/**
		 * @brief Estimate the number of bytes of memory taken by one of these
		 *        objects.  For objects with widely varying size, return the
		 *        smallest it can be.
		 */
		public static int StaticSize (System.Type typ)
		{
			if (typ == typeof (LSL_List))     return  96;
			if (typ == typeof (LSL_Rotation)) return  80;
			if (typ == typeof (void))         return   0;
			if (typ == typeof (LSL_Vector))   return  72;
			if (typ == typeof (float))        return   8;
			if (typ == typeof (int))          return   8;
			if (typ == typeof (string))       return  40;
			if (typ == typeof (double))       return   8;
			if (typ == typeof (bool))         return   8;
			if (typ == typeof (XMR_Array))    return  96;
			if (typ == typeof (object))       return  32;

			if (typ == typeof (LSL_Integer))  return  32;
			if (typ == typeof (LSL_Float))    return  32;

			throw new Exception ("unknown type " + typ.ToString ());
		}
	}

	public class TokenTypeArray : TokenType {
		public TokenTypeArray (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn, typeof (XMR_Array)) { }
		public TokenTypeArray (Token original) : base (original, typeof (XMR_Array)) { }
		public override string ToString () { return "array"; }
	}
	public class TokenTypeBool : TokenType {
		public TokenTypeBool (Token original) : base (original, typeof (bool)) { }
		public override string ToString () { return "bool"; }
	}
	public class TokenTypeFloat : TokenType {
		public TokenTypeFloat (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn, typeof (float)) { }
		public TokenTypeFloat (Token original) : base (original, typeof (float)) { }
		public override string ToString () { return "float"; }
	}
	public class TokenTypeInt : TokenType {
		public TokenTypeInt (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn, typeof (int)) { }
		public TokenTypeInt (Token original) : base (original, typeof (int)) { }
		public override string ToString () { return "integer"; }
	}
	public class TokenTypeKey : TokenType {
		public TokenTypeKey (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn, typeof (string)) { }
		public TokenTypeKey (Token original) : base (original, typeof (string)) { }
		public override string ToString () { return "string"; }
	}
	public class TokenTypeList : TokenType {
		public TokenTypeList (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn, typeof (LSL_List)) { }
		public TokenTypeList (Token original) : base (original, typeof (LSL_List)) { }
		public override string ToString () { return "list"; }
	}
	public class TokenTypeMeth : TokenType {
		public TokenDeclFunc[] funcs;
		public TokenTypeMeth (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn, null) { }
		public TokenTypeMeth (Token original) : base (original, null)
		{
			///??? this.typ = build a type from retType + argTypes ???///
		}
		public override string ToString () { return "method"; }
	}
	public class TokenTypeObject : TokenType {
		public TokenTypeObject (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn, typeof (object)) { }
		public TokenTypeObject (Token original) : base (original, typeof (object)) { }
		public override string ToString () { return "object"; }
	}
	public class TokenTypeRot : TokenType {
		public TokenTypeRot (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn, typeof (LSL_Rotation)) { }
		public TokenTypeRot (Token original) : base (original, typeof (LSL_Rotation)) { }
		public override string ToString () { return "rotation"; }
	}
	public class TokenTypeStr : TokenType {
		public TokenTypeStr (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn, typeof (string)) { }
		public TokenTypeStr (Token original) : base (original, typeof (string)) { }
		public override string ToString () { return "string"; }
	}
	public class TokenTypeVec : TokenType {
		public TokenTypeVec (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn, typeof (LSL_Vector)) { } 
		public TokenTypeVec (Token original) : base (original, typeof (LSL_Vector)) { }
		public override string ToString () { return "vector"; }
	}
	public class TokenTypeVoid : TokenType {
		public TokenTypeVoid (TokenErrorMessage emsg, int line, int posn) : base (emsg, line, posn, typeof (void)) { }
		public TokenTypeVoid (Token original) : base (original, typeof (void)) { }
		public override string ToString () { return "void"; }
	}
}
