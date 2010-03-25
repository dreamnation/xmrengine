/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

/**
 * @brief Reduce raw tokens to a single script token.
 * 
 * Usage:
 *
 *  tokenBegin = returned by TokenBegin.Analyze ()
 *               representing the whole script source
 *               as a flat list of tokens
 *
 *  TokenScript tokenScript = Reduce.Analyze (TokenBegin tokenBegin);
 *  
 *  tokenScript = represents the whole script source
 *                as a tree of tokens
 *
 * Any of the tokens can be disassembled with ToString ().
 * To get the entire source disassembled, do this:
 *  string scriptSource = tokenScript.toString ();
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace OpenSim.Region.ScriptEngine.XMREngine {

	public class ScriptReduce {

		private static Dictionary<Type, int> precedence = PrecedenceInit ();

		/**
		 * @brief Initialize operator precedence table
		 * @returns with precedence table pointer
		 */
		private static Dictionary<Type, int> PrecedenceInit ()
		{
			Dictionary<Type, int> p = new Dictionary<Type, int> ();

			// http://www.lslwiki.net/lslwiki/wakka.php?wakka=operators

			p.Add (typeof (TokenKwComma),   30);

			p.Add (typeof (TokenKwAsnLSh),  50);
			p.Add (typeof (TokenKwAsnRSh),  50);
			p.Add (typeof (TokenKwAsnAdd),  50);
			p.Add (typeof (TokenKwAsnAnd),  50);
			p.Add (typeof (TokenKwAsnSub),  50);
			p.Add (typeof (TokenKwAsnMul),  50);
			p.Add (typeof (TokenKwAsnDiv),  50);
			p.Add (typeof (TokenKwAsnMod),  50);
			p.Add (typeof (TokenKwAsnOr),   50);
			p.Add (typeof (TokenKwAsnXor),  50);
			p.Add (typeof (TokenKwAssign),  50);

			p.Add (typeof (TokenKwOrOr),   100);

			p.Add (typeof (TokenKwAndAnd), 120);

			p.Add (typeof (TokenKwOr),     140);

			p.Add (typeof (TokenKwXor),    160);

			p.Add (typeof (TokenKwAnd),    180);

			p.Add (typeof (TokenKwCmpEQ),  200);
			p.Add (typeof (TokenKwCmpNE),  200);

			p.Add (typeof (TokenKwCmpLT),  240);
			p.Add (typeof (TokenKwCmpLE),  240);
			p.Add (typeof (TokenKwCmpGT),  240);
			p.Add (typeof (TokenKwCmpGE),  240);

			p.Add (typeof (TokenKwRSh),    260);
			p.Add (typeof (TokenKwLSh),    260);

			p.Add (typeof (TokenKwAdd),    280);
			p.Add (typeof (TokenKwSub),    280);

			p.Add (typeof (TokenKwMul),    320);
			p.Add (typeof (TokenKwDiv),    320);
			p.Add (typeof (TokenKwMod),    320);

			return p;
		}

		/**
		 * @brief Reduce raw token stream to a single script token.
		 *        Performs a little semantic testing, ie, undefined variables, etc.
		 * @param tokenBegin = points to a TokenBegin
		 *                     followed by raw tokens
		 *                     and last token is a TokenEnd
		 * @returns null: not a valid script, error messages have been output
		 *          else: valid script top token
		 */
		public static TokenScript Reduce (TokenBegin tokenBegin)
		{
			return new ScriptReduce (tokenBegin.nextToken).tokenScript;
		}

		/*
		 * Instance variables.
		 */
		private bool errors = false;
		private TokenDeclFunc currentDeclFunc = null;
		private TokenScript tokenScript;
		private TokenStmtBlock currentStmtBlock = null;

		/**
		 * @brief the constructor does all the processing.
		 * @param token = first token of script after the TokenBegin token
		 * @returns tokenScript = null: there were errors
		 *                        else: successful
		 */
		private ScriptReduce (Token token)
		{
			/*
			 * Create a place to put the top-level script components,
			 * eg, state bodies, functions, global variables.
			 */
			tokenScript = new TokenScript (token);

			/*
			 * Scan through the tokens until we reach the end.
			 */
			while (!(token is TokenEnd)) {

				/*
				 * <type> <name> ;
				 * <type> <name> = <rval> ;
				 * <type> <name> <funcargs> <funcbody>
				 */
				if (token is TokenType) {
					TokenType tokenType = (TokenType)token;
					token = token.nextToken;
					if (!(token is TokenName)) {
						ErrorMsg (token, "expecting variable/function name");
						token = SkipPastSemi (token);
						continue;
					}
					TokenName tokenName = (TokenName)token;
					token = token.nextToken;
					if (token is TokenKwSemi) {

						/*
						 * <type> <name> ;
						 * global variable definition, default initialization
						 */
						TokenDeclVar tokenDeclVar = new TokenDeclVar (tokenType);
						tokenDeclVar.type = tokenType;
						tokenDeclVar.name = tokenName;
						token = token.nextToken;
						if (tokenScript.vars.ContainsKey (tokenName.val)) {
							ErrorMsg (tokenName, "duplicate variable name");
							continue;
						}
						tokenScript.vars.Add (tokenName.val, tokenDeclVar);
						continue;
					}
					if (token is TokenKwAssign) {

						/*
						 * <type> <name> =
						 * global variable definition, default initialization
						 */
						TokenDeclVar tokenDeclVar = new TokenDeclVar (tokenName);
						tokenDeclVar.type = tokenType;
						tokenDeclVar.name = tokenName;
						token = token.nextToken;
						tokenDeclVar.init = ParseRVal (ref token, typeof (TokenKwSemi));
						if (tokenDeclVar.init == null) continue;
						if (tokenScript.vars.ContainsKey (tokenName.val)) {
							ErrorMsg (tokenName, "duplicate variable name");
							continue;
						}
						tokenScript.vars.Add (tokenName.val, tokenDeclVar);
						continue;
					}
					if (token is TokenKwParOpen) {

						/*
						 * <type> <name> (
						 * global function definition
						 */
						token = tokenType;
						TokenDeclFunc tokenDeclFunc = ParseDeclFunc (ref token);
						if (tokenDeclFunc == null) continue;
						if (tokenScript.funcs.ContainsKey (tokenName.val)) {
							ErrorMsg (tokenName, "duplicate function name");
							continue;
						}
						tokenScript.funcs.Add (tokenName.val, tokenDeclFunc);
						continue;
					}
					ErrorMsg (token, "<type> <name> must be followed by ; = or (");
					token = SkipPastSemi (token);
					continue;
				}

				/*
				 * <name> <funcargs> <funcbody>
				 * global function returning void
				 */
				if (token is TokenName) {
					TokenName tokenName = (TokenName)token;
					token = token.nextToken;
					if (!(token is TokenKwParOpen)) {
						ErrorMsg (token, "looking for open paren after assuming " + 
						                 tokenName.val + " is a function name");
						token = SkipPastSemi (token);
						continue;
					}
					token = tokenName;
					TokenDeclFunc tokenDeclFunc = ParseDeclFunc (ref token);
					if (tokenDeclFunc == null) continue;
					tokenDeclFunc.retType = new TokenTypeVoid (tokenName);
					if (tokenScript.funcs.ContainsKey (tokenName.val)) {
						ErrorMsg (tokenName, "duplicate function name");
						continue;
					}
					tokenScript.funcs.Add (tokenDeclFunc.funcName.val, tokenDeclFunc);
					continue;
				}

				/*
				 * default <statebody>
				 */
				if (token is TokenKwDefault) {
					TokenDeclState tokenDeclState = new TokenDeclState (token);
					token = token.nextToken;
					tokenDeclState.body = ParseStateBody (ref token);
					if (tokenDeclState.body == null) continue;
					if (tokenScript.defaultState != null) {
						ErrorMsg (tokenDeclState, "default state already declared");
						continue;
					}
					tokenScript.defaultState = tokenDeclState;
					continue;
				}

				/*
				 * state <name> <statebody>
				 */
				if (token is TokenKwState) {
					TokenDeclState tokenDeclState = new TokenDeclState (token);
					token = token.nextToken;
					if (!(token is TokenName)) {
						ErrorMsg (token, "state must be followed by state name");
						token = SkipPastSemi (token);
						continue;
					}
					tokenDeclState.name = (TokenName)token;
					token = token.nextToken;
					tokenDeclState.body = ParseStateBody (ref token);
					if (tokenDeclState.body == null) continue;
					if (tokenScript.states.ContainsKey (tokenDeclState.name.val)) {
						ErrorMsg (tokenDeclState.name, "duplicate state definition");
						continue;
					}
					tokenScript.states.Add (tokenDeclState.name.val, tokenDeclState);
					continue;
				}

				/*
				 * Doesn't fit any of those forms, output message and skip to next statement.
				 */
				ErrorMsg (token, "looking for var name, type, state or default");
				token = SkipPastSemi (token);
				continue;
			}

			/*
			 * Must have a default state to start in.
			 */
			if (!errors && (tokenScript.defaultState == null)) {
				ErrorMsg (tokenScript, "no default state defined");
			}

			/*
			 * If any error messages were written out, set return value to null.
			 */
			if (errors) tokenScript = null;
		}

		/**
		 * @brief parse state body (including all its event handlers)
		 * @param token = points to TokenKwBrcOpen
		 * @returns null: state body parse error
		 *          else: token representing state
		 *          token = points past close brace
		 */
		private TokenStateBody ParseStateBody (ref Token token)
		{
			TokenStateBody tokenStateBody = new TokenStateBody (token);

			if (!(token is TokenKwBrcOpen)) {
				ErrorMsg (token, "expecting { at beg of state");
				token = SkipPastSemi (token);
				return null;
			}

			token = token.nextToken;
			while (!(token is TokenKwBrcClose)) {
				if (token is TokenEnd) {
					ErrorMsg (tokenStateBody, "eof parsing state body");
					return null;
				}
				TokenDeclFunc tokenDeclFunc = ParseDeclFunc (ref token);
				if (tokenDeclFunc == null) return null;
				if (!(tokenDeclFunc.retType is TokenTypeVoid)) {
					ErrorMsg (tokenDeclFunc.retType, "event handlers don't have return types");
					return null;
				}
				tokenDeclFunc.nextToken = tokenStateBody.eventFuncs;
				tokenStateBody.eventFuncs = tokenDeclFunc;
			}
			token = token.nextToken;
			return tokenStateBody;
		}

		/**
		 * @brief parse a function declaration, including its arg list and body
		 * @param token = points to function type token (or function name token if void)
		 * @returns null: error parsing function definition
		 *          else: function declaration
		 *          token = advanced just past function, ie, just past the closing brace
		 */
		private TokenDeclFunc ParseDeclFunc (ref Token token)
		{
			TokenType tokenType;
			if (token is TokenType) {
				tokenType = (TokenType)token;
				token = token.nextToken;
			} else {
				tokenType = new TokenTypeVoid (token);
			}
			if (!(token is TokenName)) {
				ErrorMsg (token, "expecting function name");
				token = SkipPastSemi (token);
				return null;
			}
			TokenName tokenName = (TokenName)token;
			token = token.nextToken;
			TokenDeclFunc tokenDeclFunc = new TokenDeclFunc (tokenName);
			tokenDeclFunc.retType  = tokenType;
			tokenDeclFunc.funcName = tokenName;
			tokenDeclFunc.argDecl  = ParseFuncArgs (ref token);

			TokenDeclFunc saveDeclFunc = currentDeclFunc;
			currentDeclFunc = tokenDeclFunc;
			tokenDeclFunc.body = ParseStmtBlock (ref token);
			currentDeclFunc = saveDeclFunc;

			if ((tokenDeclFunc.argDecl == null) || (tokenDeclFunc.body == null)) return null;
			return tokenDeclFunc;
		}

		/**
		 * @brief Parse statement
		 * @param token = first token of statement
		 * @returns null: parse error
		 *          else: token representing whole statement
		 *          token = points past statement
		 */
		private TokenStmt ParseStmt (ref Token token)
		{
			/*
			 * Statements that begin with a specific keyword.
			 */
			if (token is TokenKwAt)      return ParseStmtLabel   (ref token);
			if (token is TokenKwBrcOpen) return ParseStmtBlock   (ref token);
			if (token is TokenKwDo)      return ParseStmtDo      (ref token);
			if (token is TokenKwFor)     return ParseStmtFor     (ref token);
			if (token is TokenKwForEach) return ParseStmtForEach (ref token);
			if (token is TokenKwIf)      return ParseStmtIf      (ref token);
			if (token is TokenKwJump)    return ParseStmtJump    (ref token);
			if (token is TokenKwRet)     return ParseStmtRet     (ref token);
			if (token is TokenKwSemi)    return ParseStmtNull    (ref token);
			if (token is TokenKwState)   return ParseStmtState   (ref token);
			if (token is TokenKwWhile)   return ParseStmtWhile   (ref token);

			/*
			 * Try to parse anything else as an expression, possibly calling
			 * something and/or writing to a variable.
			 */
			TokenRVal tokenRVal = ParseRVal (ref token, typeof (TokenKwSemi));
			if (tokenRVal != null) {
				TokenStmtRVal tokenStmtRVal = new TokenStmtRVal (tokenRVal);
				tokenStmtRVal.rVal = tokenRVal;
				return tokenStmtRVal;
			}

			/*
			 * Who knows what it is...
			 */
			ErrorMsg (token, "unknown statement");
			token = SkipPastSemi (token);
			return null;
		}

		/**
		 * @brief parse a statement block, ie, group of statements between braces
		 * @param token = points to { token
		 * @returns null: error parsing
		 *          else: statements bundled in this token
		 *          token = advanced just past the } token
		 */
		private TokenStmtBlock ParseStmtBlock (ref Token token)
		{
			if (!(token is TokenKwBrcOpen)) {
				ErrorMsg (token, "function body must begin with a {");
				token = SkipPastSemi (token);
				return null;
			}
			TokenStmtBlock tokenStmtBlock = new TokenStmtBlock (token);
			tokenStmtBlock.function = currentDeclFunc;
			tokenStmtBlock.outerStmtBlock = currentStmtBlock;
			currentStmtBlock = tokenStmtBlock;
			Token prevStmt = null;
			token = token.nextToken;
			while (!(token is TokenKwBrcClose)) {
				if (token is TokenEnd) {
					ErrorMsg (tokenStmtBlock, "missing }");
					currentStmtBlock = tokenStmtBlock.outerStmtBlock;
					return null;
				}
				Token thisStmt;
				if (token is TokenType) {
					thisStmt = ParseDeclVar (ref token);
				} else {
					thisStmt = ParseStmt (ref token);
				}
				if (thisStmt == null) return null;
				if (prevStmt == null) tokenStmtBlock.statements = thisStmt;
				                 else prevStmt.nextToken = thisStmt;
				prevStmt = thisStmt;
			}
			tokenStmtBlock.closebr = token;
			token = token.nextToken;
			currentStmtBlock = tokenStmtBlock.outerStmtBlock;
			return tokenStmtBlock;
		}

		/**
		 * @brief parse a 'do' statement
		 * @params token = points to 'do' keyword token
		 * @returns null: parse error
		 *          else: pointer to token encapsulating the do statement, including body
		 *          token = advanced just past the body statement
		 */
		private TokenStmtDo ParseStmtDo (ref Token token)
		{
			TokenStmtDo tokenStmtDo = new TokenStmtDo (token);
			token = token.nextToken;
			tokenStmtDo.bodyStmt = ParseStmt (ref token);
			if (tokenStmtDo.bodyStmt == null) return null;
			if (!(token is TokenKwWhile)) {
				ErrorMsg (token, "expecting while clause");
				return null;
			}
			token = token.nextToken;
			tokenStmtDo.testRVal = ParseRValParen (ref token);
			if (tokenStmtDo.testRVal == null) return null;
			if (!(token is TokenKwSemi)) {
				ErrorMsg (token, "while clause must terminate on semicolon");
				token = SkipPastSemi (token);
				return null;
			}
			token = token.nextToken;
			return tokenStmtDo;
		}

		/**
		 * @brief parse a for statement
		 * @param token = points to 'for' keyword token
		 * @returns null: parse error
		 *          else: pointer to encapsulated for statement token
		 *          token = advanced just past for body statement
		 */
		private TokenStmt ParseStmtFor (ref Token token)
		{

			/*
			 * Create encapsulating token and skip past 'for ('
			 */
			TokenStmtFor tokenStmtFor = new TokenStmtFor (token);
			token = token.nextToken;
			if (!(token is TokenKwParOpen)) {
				ErrorMsg (token, "for must be followed by (");
				return null;
			}
			token = token.nextToken;

			/*
			 * If a plain for, ie, not declaring a variable, it's straightforward.
			 */
			if (!(token is TokenType)) {
				tokenStmtFor.initStmt = ParseStmt (ref token);
				if (tokenStmtFor.initStmt == null) return null;
				return ParseStmtFor2 (tokenStmtFor, ref token) ? tokenStmtFor : null;
			}

			/*
			 * Initialization declares a variable, so encapsulate it in a block so
			 * variable has scope only in the for statement, including its body.
			 */
			TokenStmtBlock forStmtBlock = new TokenStmtBlock (tokenStmtFor);
			forStmtBlock.outerStmtBlock = currentStmtBlock;
			forStmtBlock.function       = currentDeclFunc;

			TokenDeclVar tokenDeclVar   = ParseDeclVar (ref token);
			if (tokenDeclVar == null) {
				currentStmtBlock    = forStmtBlock.outerStmtBlock;
				return null;
			}

			forStmtBlock.statements     = tokenDeclVar;
			tokenDeclVar.nextToken      = tokenStmtFor;

			bool ok                     = ParseStmtFor2 (tokenStmtFor, ref token);
			currentStmtBlock            = forStmtBlock.outerStmtBlock;
			forStmtBlock.closebr        = token.prevToken;
			return ok ? forStmtBlock : null;
		}

		/**
		 * @brief parse rest of 'for' statement starting with the test expression.
		 * @param tokenStmtFor = token encapsulating the for statement
		 * @param token = points to test expression
		 * @returns false: parse error
		 *           true: successful
		 *          token = points just past body statement
		 */
		private bool ParseStmtFor2 (TokenStmtFor tokenStmtFor, ref Token token)
		{
			if (token is TokenKwSemi) {
				token = token.nextToken;
			} else {
				tokenStmtFor.testRVal = ParseRVal (ref token, typeof (TokenKwSemi));
				if (tokenStmtFor.testRVal == null) return false;
			}
			if (token is TokenKwParClose) {
				token = token.nextToken;
			} else {
				tokenStmtFor.incrRVal = ParseRVal (ref token, typeof (TokenKwParClose));
				if (tokenStmtFor.incrRVal == null) return false;
			}
			tokenStmtFor.bodyStmt = ParseStmt (ref token);
			return tokenStmtFor.bodyStmt != null;
		}

		/**
		 * @brief parse a foreach statement
		 * @param token = points to 'foreach' keyword token
		 * @returns null: parse error
		 *          else: pointer to encapsulated foreach statement token
		 *          token = advanced just past for body statement
		 */
		private TokenStmt ParseStmtForEach (ref Token token)
		{

			/*
			 * Create encapsulating token and skip past 'foreach ('
			 */
			TokenStmtForEach tokenStmtForEach = new TokenStmtForEach (token);
			token = token.nextToken;
			if (!(token is TokenKwParOpen)) {
				ErrorMsg (token, "foreach must be followed by (");
				return null;
			}
			token = token.nextToken;

			if (token is TokenName) {
				tokenStmtForEach.keyLVal = new TokenLValName ((TokenName)token);
				token = token.nextToken;
			}
			if (!(token is TokenKwComma)) {
				ErrorMsg (token, "expecting comma");
				token = SkipPastSemi (token);
				return null;
			}
			token = token.nextToken;
			if (token is TokenName) {
				tokenStmtForEach.valLVal = new TokenLValName ((TokenName)token);
				token = token.nextToken;
			}
			if (!(token is TokenKwIn)) {
				ErrorMsg (token, "expecting 'in'");
				token = SkipPastSemi (token);
				return null;
			}
			token = token.nextToken;
			tokenStmtForEach.arrayLVal = ParseLVal (ref token);
			if (tokenStmtForEach.arrayLVal == null) return null;
			if (!(token is TokenKwParClose)) {
				ErrorMsg (token, "expecting )");
				token = SkipPastSemi (token);
				return null;
			}
			token = token.nextToken;
			tokenStmtForEach.bodyStmt = ParseStmt (ref token);
			if (tokenStmtForEach.bodyStmt == null) return null;
			return tokenStmtForEach;
		}

		private TokenStmtIf ParseStmtIf (ref Token token)
		{
			TokenStmtIf tokenStmtIf = new TokenStmtIf (token);
			token = token.nextToken;
			tokenStmtIf.testRVal = ParseRValParen (ref token);
			if (tokenStmtIf.testRVal == null) return null;
			tokenStmtIf.trueStmt = ParseStmt (ref token);
			if (tokenStmtIf.trueStmt == null) return null;
			if (token is TokenKwElse) {
				token = token.nextToken;
				tokenStmtIf.elseStmt = ParseStmt (ref token);
				if (tokenStmtIf.elseStmt == null) return null;
			}
			return tokenStmtIf;
		}

		private TokenStmtJump ParseStmtJump (ref Token token)
		{

			/*
			 * Create jump statement token to encapsulate the whole statement.
			 */
			TokenStmtJump tokenStmtJump = new TokenStmtJump (token);
			token = token.nextToken;
			if (!(token is TokenName) || !(token.nextToken is TokenKwSemi)) {
				ErrorMsg (token, "expecting label;");
				token = SkipPastSemi (token);
				return null;
			}
			tokenStmtJump.label = (TokenName)token;
			token = token.nextToken.nextToken;

			/*
			 * If label is already defined, it means this is a backward (looping)
			 * jump, so remember the label has backward jump references.
			 */
			if (currentDeclFunc.labels.ContainsKey (tokenStmtJump.label.val)) {
				currentDeclFunc.labels[tokenStmtJump.label.val].hasBkwdRefs = true;
			}

			return tokenStmtJump;
		}

		/**
		 * @brief parse a jump target label statement
		 * @param token = points to the '@' token
		 * @returns null: error parsing
		 *          else: the label
		 *          token = advanced just past the ;
		 */
		private TokenStmtLabel ParseStmtLabel (ref Token token)
		{
			if (!(token.nextToken is TokenName) ||
			    !(token.nextToken.nextToken is TokenKwSemi)) {
				ErrorMsg (token, "invalid label");
				token = SkipPastSemi (token);
				return null;
			}
			TokenStmtLabel stmtLabel = new TokenStmtLabel (token);
			stmtLabel.name  = (TokenName)token.nextToken;
			stmtLabel.block = currentStmtBlock;
			if (currentDeclFunc.labels.ContainsKey (stmtLabel.name.val)) {
				ErrorMsg (token.nextToken, "duplicate label");
				ErrorMsg (currentDeclFunc.labels[stmtLabel.name.val], "previously defined here");
				token = SkipPastSemi (token);
				return null;
			}
			currentDeclFunc.labels.Add (stmtLabel.name.val, stmtLabel);
			token = token.nextToken.nextToken.nextToken;
			return stmtLabel;
		}

		private TokenStmtNull ParseStmtNull (ref Token token)
		{
			TokenStmtNull tokenStmtNull = new TokenStmtNull (token);
			token = token.nextToken;
			return tokenStmtNull;
		}

		private TokenStmtRet ParseStmtRet (ref Token token)
		{
			TokenStmtRet tokenStmtRet = new TokenStmtRet (token);
			token = token.nextToken;
			if (token is TokenKwSemi) {
				token = token.nextToken;
			} else {
				tokenStmtRet.rVal = ParseRVal (ref token, typeof (TokenKwSemi));
				if (tokenStmtRet.rVal == null) return null;
			}
			return tokenStmtRet;
		}

		private TokenStmtState ParseStmtState (ref Token token)
		{
			TokenStmtState tokenStmtState = new TokenStmtState (token);
			token = token.nextToken;
			if ((!(token is TokenName) && !(token is TokenKwDefault)) || !(token.nextToken is TokenKwSemi)) {
				ErrorMsg (token, "expecting state;");
				token = SkipPastSemi (token);
				return null;
			}
			if (token is TokenName) {
				tokenStmtState.state = (TokenName)token;
			}
			currentDeclFunc.changesState = true;
			token = token.nextToken.nextToken;
			return tokenStmtState;
		}

		private TokenStmtWhile ParseStmtWhile (ref Token token)
		{
			TokenStmtWhile tokenStmtWhile = new TokenStmtWhile (token);
			token = token.nextToken;
			tokenStmtWhile.testRVal = ParseRValParen (ref token);
			if (tokenStmtWhile.testRVal == null) return null;
			tokenStmtWhile.bodyStmt = ParseStmt (ref token);
			if (tokenStmtWhile.bodyStmt == null) return null;
			return tokenStmtWhile;
		}

		/**
		 * @brief parse a variable declaration statement, including init value if any.
		 * @param token = points to 'type' token
		 * @returns null: parsing error
		 *          else: variable declaration encapulating token
		 *          token = advanced just past semi-colon
		 */
		private TokenDeclVar ParseDeclVar (ref Token token)
		{

			/*
			 * Build basic encapsulating token with type and name.
			 */
			TokenDeclVar tokenDeclVar = new TokenDeclVar (token);
			tokenDeclVar.type = (TokenType)token;
			token = token.nextToken;
			if (!(token is TokenName)) {
				ErrorMsg (token, "expecting variable name");
				token = SkipPastSemi (token);
				return null;
			}
			tokenDeclVar.name = (TokenName)token;
			token = token.nextToken;

			/*
			 * If just a ;, there is no explicit initialization value.
			 * Otherwise, look for an =RVal; expression that has init value.
			 */
			if (token is TokenKwSemi) {
				token = token.nextToken;
			} else if (token is TokenKwAssign) {
				token = token.nextToken;
				tokenDeclVar.init = ParseRVal (ref token, typeof (TokenKwSemi));
				if (tokenDeclVar.init == null) return null;
			} else {
				ErrorMsg (token, "expecting = or ;");
				token = SkipPastSemi (token);
				return null;
			}

			/*
			 * Can't override any other var out through and including function parameters.
			 */
			for (TokenStmtBlock stmtBlock = currentStmtBlock; stmtBlock != null; stmtBlock = stmtBlock.outerStmtBlock) {
				if (stmtBlock.variables.ContainsKey (tokenDeclVar.name.val)) {
					ErrorMsg (tokenDeclVar, "duplicate variable definition");
					ErrorMsg (stmtBlock.variables[tokenDeclVar.name.val].name, "previously defined here");
					return tokenDeclVar;
				}
			}
			foreach (TokenName name in currentDeclFunc.argDecl.names) {
				if (name.val == tokenDeclVar.name.val) {
					ErrorMsg (tokenDeclVar, "duplicate variable definition");
					ErrorMsg (name, "previously defined here");
					return tokenDeclVar;
				}
			}
			currentStmtBlock.variables.Add (tokenDeclVar.name.val, tokenDeclVar);
			return tokenDeclVar;
		}

		/**
		 * @brief parse function declaration argument list
		 * @param token = points to TokenKwParOpen
		 * @returns null: parse error
		 *          else: points to token with types and names
		 *          token = updated past the TokenKwParClose
		 */
		private TokenArgDecl ParseFuncArgs (ref Token token)
		{
			int nArgs = 0;
			LinkedList<TokenName> nameList = new LinkedList<TokenName> ();
			LinkedList<TokenType> typeList = new LinkedList<TokenType> ();
			TokenArgDecl tokenArgDecl = new TokenArgDecl (token);

			do {
				token = token.nextToken;
				if ((nArgs == 0) && (token is TokenKwParClose)) break;
				if (!(token is TokenType)) {
					ErrorMsg (token, "expecting arg type");
					token = SkipPastSemi (token);
					return null;
				}
				typeList.AddLast ((TokenType)token);

				token = token.nextToken;
				if (!(token is TokenName)) {
					ErrorMsg (token, "expecting arg name");
					token = SkipPastSemi (token);
					return null;
				}
				foreach (TokenName dupCheck in nameList) {
					if (dupCheck.val == ((TokenName)token).val) {
						ErrorMsg (token, "duplicate arg name");
						break;
					}
				}
				nameList.AddLast ((TokenName)token);
				nArgs ++;

				token = token.nextToken;
			} while (token is TokenKwComma);
			if (!(token is TokenKwParClose)) {
				ErrorMsg (token, "expecting comma or close paren");
				token = SkipPastSemi (token);
				return null;
			}
			token = token.nextToken;

			tokenArgDecl.types = System.Linq.Enumerable.ToArray (typeList);
			tokenArgDecl.names = System.Linq.Enumerable.ToArray (nameList);
			return tokenArgDecl;
		}

		/**
		 * @brief parse right-hand value expression
		 *        this is where arithmetic-like expressions are processed
		 * @param token = points to first token expression
		 * @param termTokenType = expression termination token type
		 * @returns null: not an RVal
		 *          else: single token representing whole expression
		 *          token = points past terminating token
		 */
		public TokenRVal ParseRVal (ref Token token, Type termTokenType)
		{
			/*
			 * Start with an empty operator stack and the first operand on operand stack.
			 */
			BinOp binOps = null;
			TokenRVal operands = GetOperand (ref token);
			if (operands == null) return null;

			/*
			 * Keep scanning until we hit the termination token.
			 */
			while (token.GetType () != termTokenType) {

				/*
				 * Special form:
				 *   <operand> is <typeexp>
				 */
				if (token is TokenKwIs) {
					TokenRValIsType tokenRValIsType = new TokenRValIsType (token);
					token = token.nextToken;

					/*
					 * Parse the <typeexp>.
					 */
					tokenRValIsType.typeExp = ParseTypeExp (ref token);
					if (tokenRValIsType.typeExp == null) return null;

					/*
					 * Replace top operand with result of <operand> is <typeexp>
					 */
					tokenRValIsType.rValExp   = operands;
					tokenRValIsType.nextToken = operands.nextToken;
					operands = tokenRValIsType;

					/*
					 * token points just past <typeexp> so see if it is another operator.
					 */
					continue;
				}

				/*
				 * Peek at next operator.
				 */
				BinOp binOp = GetOperator (ref token);
				if (binOp == null) return null;

				/*
				 * If there are stacked operators of higher or same precedence than new one,
				 * perform their computation then push result back on operand stack.
				 *
				 *  higher or same = left-to-right application of operators
				 *                   eg, a - b - c becomes (a - b) - c
				 *
				 *  higher precedence = right-to-left application of operators
				 *                      eg, a - b - c becomes a - (b - c)
				 */
				while ((binOps != null) && (binOps.preced >= binOp.preced)) {
					TokenRValOpBin tokenRValOpBin = new TokenRValOpBin ((TokenRVal)operands.prevToken, binOps.token, operands);
					tokenRValOpBin.prevToken = operands.prevToken.prevToken;
					operands = tokenRValOpBin;
					binOps = binOps.pop;
				}

				/*
				 * Push new operator on its stack.
				 */
				binOp.pop = binOps;
				binOps = binOp;

				/*
				 * Push next operand on its stack.
				 */
				TokenRVal operand = GetOperand (ref token);
				if (operand == null) return null;
				operand.prevToken = operands;
				operands = operand;
			}

			/*
			 * At end of expression, perform any stacked computations.
			 */
			while (binOps != null) {
				TokenRValOpBin tokenRValOpBin = new TokenRValOpBin ((TokenRVal)operands.prevToken, binOps.token, operands);
				tokenRValOpBin.prevToken = operands.prevToken.prevToken;
				operands = tokenRValOpBin;
				binOps = binOps.pop;
			}

			/*
			 * There should be exactly one remaining operand on the stack.
			 */
			if (operands.prevToken != null) throw new Exception ("too many operands");
			token = token.nextToken;
			return operands;
		}

		private TokenTypeExp ParseTypeExp (ref Token token)
		{
			TokenTypeExp leftOperand = GetTypeExp (ref token);
			if (leftOperand == null) return null;

			while ((token is TokenKwAnd) || (token is TokenKwOr)) {
				Token typeBinOp = token;
				token = token.nextToken;
				TokenTypeExp rightOperand = GetTypeExp (ref token);
				if (rightOperand == null) return null;
				TokenTypeExpBinOp typeExpBinOp = new TokenTypeExpBinOp (typeBinOp);
				typeExpBinOp.leftOp  = leftOperand;
				typeExpBinOp.binOp   = typeBinOp;
				typeExpBinOp.rightOp = rightOperand;
				leftOperand = typeExpBinOp;
			}
			return leftOperand;
		}

		private TokenTypeExp GetTypeExp (ref Token token)
		{
			if (token is TokenKwTilde) {
				TokenTypeExpNot typeExpNot = new TokenTypeExpNot (token);
				token = token.nextToken;
				typeExpNot.typeExp = GetTypeExp (ref token);
				if (typeExpNot.typeExp == null) return null;
				return typeExpNot;
			}
			if (token is TokenKwParOpen) {
				TokenTypeExpPar typeExpPar = new TokenTypeExpPar (token);
				token = token.nextToken;
				typeExpPar.typeExp = GetTypeExp (ref token);
				if (typeExpPar.typeExp == null) return null;
				if (!(token is TokenKwParClose)) {
					ErrorMsg (token, "expected close parenthesis");
					token = SkipPastSemi (token);
					return null;
				}
				return typeExpPar;
			}
			if (token is TokenKwUndef) {
				TokenTypeExpUndef typeExpUndef = new TokenTypeExpUndef (token);
				token = token.nextToken;
				return typeExpUndef;
			}
			if (token is TokenType) {
				TokenTypeExpType typeExpType = new TokenTypeExpType (token);
				typeExpType.typeToken = (TokenType)token;
				token = token.nextToken;
				return typeExpType;
			}
			ErrorMsg (token, "expected type");
			token = SkipPastSemi (token);
			return null;
		}

		/**
		 * @brief get a right-hand operand expression token
		 * @param token = first token of operand to parse
		 * @returns null: invalid operand
		 *          else: token that bundles or wraps the operand
		 *          token = points to token following last operand token
		 */
		private TokenRVal GetOperand (ref Token token)
		{
			TokenRVal operand = GetOperandNoCall (ref token);
			if (operand == null) return null;

			/*
			 * If not followed by a (, it isn't a function call.
			 */
			if (!(token is TokenKwParOpen)) return operand;

			/*
			 * Function call, make sure thing before ( is a name or a field.
			 */
			TokenLVal meth = null;
			if (operand is TokenRValLVal) {
				TokenRValLVal op = (TokenRValLVal)operand;
				if (op.lvToken is TokenLValName)  meth = op.lvToken;
				if (op.lvToken is TokenLValField) meth = op.lvToken;
			}
			if (meth == null) {
				ErrorMsg (token, "invalid function reference or missing operator");
				return null;
			}

			/*
			 * Set up basic function call struct with function name.
			 */
			TokenRValCall rValCall = new TokenRValCall (token);
			rValCall.meth = meth;
			token = token.nextToken;

			/*
			 * Parse the call parameters, if any.
			 */
			if (token is TokenKwParClose) {
				token = token.nextToken;
			} else {
				rValCall.args = ParseRVal (ref token, typeof (TokenKwParClose));
				if (rValCall.args == null) return null;
				rValCall.nArgs = SplitCommaRVals (rValCall.args, out rValCall.args, ref rValCall.sideEffects);
			}

			/*
			 * If calling a function (not a method), rememeber the called function.
			 */
			if (meth is TokenLValName) {
				TokenLValName methName = (TokenLValName)meth;
				if (!currentDeclFunc.calledFuncs.ContainsKey (methName.name.val)) {
					currentDeclFunc.calledFuncs[methName.name.val] = methName.name;
				}
			}

			return rValCall;
		}

		/**
		 * @brief same as GetOperand() except doesn't check for a function call
		 */
		private TokenRVal GetOperandNoCall (ref Token token)
		{
			TokenLVal lVal;

			/*
			 * Simple unary operators.
			 */
			if ((token is TokenKwSub) || 
			    (token is TokenKwTilde) ||
			    (token is TokenKwExclam)) {
				Token uop = token;
				token = token.nextToken;
				TokenRVal rVal = GetOperand (ref token);
				if (rVal == null) return null;
				return new TokenRValOpUn (uop, rVal);
			}

			/*
			 * Prefix unary operators requiring an L-value.
			 */
			if ((token is TokenKwIncr) || (token is TokenKwDecr)) {
				TokenRValAsnPre asnPre = new TokenRValAsnPre (token);
				asnPre.prefix = token;
				token = token.nextToken;
				asnPre.lVal = ParseLVal (ref token);
				if (asnPre.lVal == null) return null;
				return asnPre;
			}

			/*
			 * Type casting.
			 */
			if ((token is TokenKwParOpen) &&
			    (token.nextToken is TokenType) &&
			    (token.nextToken.nextToken is TokenKwParClose)) {
				TokenType type = (TokenType)token.nextToken;
				token = token.nextToken.nextToken.nextToken;
				TokenRVal rVal = GetOperand (ref token);
				if (rVal == null) return null;
				return new TokenRValCast (type, rVal);
			}

			/*
			 * Parenthesized expression.
			 */
			if (token is TokenKwParOpen) {
				return ParseRValParen (ref token);
			}

			/*
			 * Constants.
			 */
			if (token is TokenFloat) {
				TokenRValFloat rValFloat = new TokenRValFloat ((TokenFloat)token);
				token = token.nextToken;
				return rValFloat;
			}
			if (token is TokenInt) {
				TokenRValInt rValInt = new TokenRValInt ((TokenInt)token);
				token = token.nextToken;
				return rValInt;
			}
			if (token is TokenStr) {
				TokenRValStr rValStr = new TokenRValStr ((TokenStr)token);
				token = token.nextToken;
				return rValStr;
			}
			if (token is TokenKwUndef) {
				TokenRValUndef rValUndef = new TokenRValUndef ((TokenKwUndef)token);
				token = token.nextToken;
				return rValUndef;
			}

			/*
			 * '<'value,...'>', ie, rotation or vector
			 */
			if (token is TokenKwCmpLT) {
				Token openBkt = token;
				token = token.nextToken;
				TokenRVal rValAll = ParseRVal (ref token, typeof (TokenKwCmpGT));
				if (rValAll == null) return null;
				TokenRVal rVals;
				bool sideEffects = false;
				int nVals = SplitCommaRVals (rValAll, out rVals, ref sideEffects);
				switch (nVals) {
					case 3: {
						TokenRValVec rValVec = new TokenRValVec (openBkt);
						rValVec.xRVal = rVals;
						rValVec.yRVal = (TokenRVal)rVals.nextToken;
						rValVec.zRVal = (TokenRVal)rVals.nextToken.nextToken;
						rValVec.sideEffects = sideEffects;
						return rValVec;
					}
					case 4: {
						TokenRValRot rValRot = new TokenRValRot (openBkt);
						rValRot.xRVal = rVals;
						rValRot.yRVal = (TokenRVal)rVals.nextToken;
						rValRot.zRVal = (TokenRVal)rVals.nextToken.nextToken;
						rValRot.wRVal = (TokenRVal)rVals.nextToken.nextToken.nextToken;
						rValRot.sideEffects = sideEffects;
						return rValRot;
					}
					default: {
						ErrorMsg (openBkt, "bad rotation/vector");
						token = SkipPastSemi (token);
						return null;
					}
				}
			}

			/*
			 * '['value,...']', ie, list
			 */
			if (token is TokenKwBrkOpen) {
				TokenRValList rValList = new TokenRValList (token);
				token = token.nextToken;
				if (token is TokenKwBrkClose) {
					token = token.nextToken;  // empty list
				} else {
					TokenRVal rValAll = ParseRVal (ref token, typeof (TokenKwBrkClose));
					if (rValAll == null) return null;
					rValList.nItems = SplitCommaRVals (rValAll, out rValList.rVal, ref rValList.sideEffects);
				}
				return rValList;
			}

			/*
			 * Built-in symbolic constants.
			 */
			if (token is TokenName) {
				ScriptConst scriptConst = ScriptConst.Lookup (((TokenName)token).val);
				if (scriptConst != null) {
					TokenRValConst rValConst = new TokenRValConst (token);
					rValConst.val = scriptConst;
					token = token.nextToken;
					return rValConst;
				}
			}

			/*
			 * L-value being used as an R-value is all we got left to try.
			 */
			lVal = ParseLVal (ref token);
			if (lVal == null) return null;

			/*
			 * Maybe the L-value is followed by a postfix operator.
			 */
			if ((token is TokenKwIncr) || (token is TokenKwDecr)) {
				TokenRValAsnPost asnPost = new TokenRValAsnPost (token);
				asnPost.lVal = lVal;
				asnPost.postfix = token;
				token = token.nextToken;
				return asnPost;
			}

			/*
			 * Just a simple L-value being used as an R-value.
			 */
			return new TokenRValLVal (lVal);
		}

		/**
		 * @brief decode binary operator token
		 * @param token = points to token to decode
		 * @returns null: invalid operator token
		 *          else: operator token and precedence
		 */
		private BinOp GetOperator (ref Token token)
		{
			BinOp binOp = new BinOp ();
			if (precedence.TryGetValue (token.GetType (), out binOp.preced)) {
				binOp.token = token;
				token = token.nextToken;
				return binOp;
			}

			if ((token is TokenKwSemi) || (token is TokenKwBrcOpen) || (token is TokenKwBrcClose)) {
				ErrorMsg (token, "premature expression end");
			} else {
				ErrorMsg (token, "invalid operator");
			}
			token = SkipPastSemi (token);
			return null;
		}

		private class BinOp {
			public BinOp pop;
			public Token token;
			public int preced;
		}

		/**
		 * @brief parse out a parenthesized expression.
		 * @param token = points to open parenthesis
		 * @returns null: invalid expression
		 *          else: parenthesized expression token
		 *          token = points past the close parenthesis
		 */
		private TokenRValParen ParseRValParen (ref Token token)
		{
			if (!(token is TokenKwParOpen)) {
				ErrorMsg (token, "expecting (");
				token = SkipPastSemi (token);
				return null;
			}
			TokenRValParen tokenRValParen = new TokenRValParen (token);
			token = token.nextToken;
			tokenRValParen.rVal = ParseRVal (ref token, typeof (TokenKwParClose));
			if (tokenRValParen.rVal == null) return null;
			tokenRValParen.sideEffects = tokenRValParen.rVal.sideEffects;
			return tokenRValParen;
		}

		/**
		 * @brief Split a comma'd RVal into separate expressions
		 * @param rValAll = expression containing commas
		 * @returns number of comma separated values
		 *          rVals = values in a null-terminated list linked by rVals.nextToken
		 *          sideEffects |= some of the values have side effects
		 */
		private int SplitCommaRVals (TokenRVal rValAll, out TokenRVal rVals, ref bool sideEffects)
		{
			if (!(rValAll is TokenRValOpBin) || !(((TokenRValOpBin)rValAll).opcode is TokenKwComma)) {
				rVals = rValAll;
				if (rVals.nextToken != null) throw new Exception ("expected null");
				sideEffects |= rValAll.sideEffects;
				return 1;
			}
			TokenRValOpBin opBin = (TokenRValOpBin)rValAll;
			TokenRVal rValLeft, rValRight;
			bool sel = false;
			bool ser = false;
			int leftCount  = SplitCommaRVals (opBin.rValLeft,  out rValLeft,  ref sel);
			int rightCount = SplitCommaRVals (opBin.rValRight, out rValRight, ref ser);
			rVals = rValLeft;
			while (rValLeft.nextToken != null) rValLeft = (TokenRVal)rValLeft.nextToken;
			rValLeft.nextToken = rValRight;
			sideEffects |= sel | ser;
			return leftCount + rightCount;
		}

		/**
		 * @brief parse a L-value, ie, something that can be used on left side of '='
		 * @param token = points to first token to check
		 * @returns encapsulation of L-value expression
		 *          token = advanced past L-value expression
		 */
		private TokenLVal ParseLVal (ref Token token)
		{
			/*
			 * L-values always start with a name
			 */
			if (!(token is TokenName)) {
				ErrorMsg (token, "invalid L-value");
				token = SkipPastSemi (token);
				return null;
			}
			TokenLVal tokenLVal = new TokenLValName ((TokenName)token);
			token = token.nextToken;

			while (true) {

				/*
				 * They may be followed by .fieldname
				 */
				if (token is TokenKwDot) {
					TokenLValField tokenLValField = new TokenLValField (token);
					token = token.nextToken;
					if (!(token is TokenName)) {
						ErrorMsg (token, "invalid field name");
						token = SkipPastSemi (token);
						return null;
					}
					tokenLValField.baseLVal = tokenLVal;
					tokenLValField.field = (TokenName)token;
					tokenLVal = tokenLValField;
					token = token.nextToken;
					continue;
				}

				/*
				 * They may be followed by [subscript]
				 */
				if (token is TokenKwBrkOpen) {
					TokenLValArEle tokenLValArEle = new TokenLValArEle (token);
					token = token.nextToken;
					tokenLValArEle.subRVal = ParseRVal (ref token, typeof (TokenKwBrkClose));
					if (tokenLValArEle.subRVal == null) {
						ErrorMsg (tokenLValArEle, "invalid subscript");
						return null;
					}
					tokenLValArEle.baseLVal = tokenLVal;
					tokenLVal = tokenLValArEle;
					continue;
				}

				/*
				 * No modifier we recognize, done parsing.
				 */
				return tokenLVal;
			}
		}

		/**
		 * @brief output error message and remember that there is an error.
		 * @param token = what token is associated with the error
		 * @param message = error message string
		 */
		private void ErrorMsg (Token token, string message)
		{
			errors = true;
			token.ErrorMsg (message);
		}

		/**
		 * @brief Skip past the next semicolon (or matched braces)
		 * @param token = points to token to skip over
		 * @returns token just after the semicolon or close brace
		 */
		private Token SkipPastSemi (Token token)
		{
			int braceLevel = 0;

			while (!(token is TokenEnd)) {
				if ((token is TokenKwSemi) && (braceLevel == 0)) {
					return token.nextToken;
				}
				if (token is TokenKwBrcOpen) {
					braceLevel ++;
				}
				if ((token is TokenKwBrcClose) && (-- braceLevel <= 0)) {
					return token.nextToken;
				}
				token = token.nextToken;
			}
			return token;
		}
	}


	/**
	 * @brief function argument list declaration
	 */
	public class TokenArgDecl : Token
	{
		public TokenType[] types;
		public TokenName[] names;

		public TokenArgDecl (Token original) : base (original) { }

		public override string ToString ()
		{
			System.Text.StringBuilder s = new System.Text.StringBuilder ("(");
			for (int i = 0; (i < types.Length) || (i < names.Length); i ++) {
				if (i > 0) s.Append (", ");
				s.Append (types[i].ToString ());
				s.Append (" ");
				s.Append (names[i].ToString ());
			}
			s.Append (")");
			return s.ToString ();
		}
	}

	/**
	 * @brief encapsulates a function definition
	 */
	public class TokenDeclFunc : Token {

		public TokenType retType;            // new TokenTypeVoid (token) if void; NEVER null
		public TokenName funcName;           // function name
		public TokenArgDecl argDecl;         // argument list prototypes
		public TokenStmtBlock body;          // statements
		public Dictionary<string, TokenStmtLabel> labels = new Dictionary<string, TokenStmtLabel> ();
		                                     // all labels defined in the function
		public bool changesState;            // contains a 'state' statement somewhere
		public Dictionary<string, TokenName> calledFuncs = new Dictionary<string, TokenName> ();
		                                     // all functions called by this function

		public ScriptMyILGen ilGen;          // codegen stores emitted code here

		public TokenDeclFunc (Token original) : base (original) { }

		public override string ToString ()
		{
			System.Text.StringBuilder s = new System.Text.StringBuilder ("");
			s.Append (retType.ToString ());
			s.Append (" ");
			s.Append (funcName.ToString ());
			s.Append (" ");
			s.Append (argDecl.ToString ());
			s.Append (" ");
			s.Append (body.ToString ());
			return s.ToString ();
		}
	}

	/**
	 * @brief encapsulate a state declaration in a single token
	 */
	public class TokenDeclState : Token {

		public TokenName name;  // null for default state
		public TokenStateBody body;

		public TokenDeclState (Token original) : base (original) { }
	}

	public class TokenDeclVar : Token {

		public TokenType type;
		public TokenName name;
		public TokenRVal init;  // null if none
		public bool preDefd;    // used by codegen
		                        // false: normal in-order definition
		                        //  true: var was defined at top of block
		                        //        just set initialization value

		public TokenDeclVar (Token original) : base (original) { }

		public override string ToString ()
		{
			return (init == null) ? type.ToString () + " " + name.ToString () + ";" :
			                        type.ToString () + " " + name.ToString () + " = " + init.ToString () + ";";
		}
	}


	/**
	 * @brief any expression that can go on left side of an "="
	 */
	public class TokenLVal : Token {

		public TokenLVal (Token original) : base (original) { }
	}

	/**
	 * @brief an element of an L-value array is an L-value
	 */
	public class TokenLValArEle : TokenLVal {
		public TokenLVal baseLVal;
		public TokenRVal subRVal;

		public TokenLValArEle (Token original) : base (original) { }

		public override string ToString ()
		{
			return base.ToString ();
		}
	}

	/**
	 * @brief a field within an L-value struct is an L-value
	 */
	public class TokenLValField : TokenLVal {
		public TokenLVal baseLVal;
		public TokenName field;

		public TokenLValField (Token original) : base (original) { }

		public override string ToString ()
		{
			return base.ToString ();
		}
	}

	/**
	 * @brief a name is being used as an L-value
	 */
	public class TokenLValName : TokenLVal {
		public TokenName name;

		public TokenLValName (TokenName original) : base (original)
		{
			this.name = original;
		}

		public override string ToString ()
		{
			return name.ToString ();
		}
	}

	/**
	 * @brief any expression that can go on right side of "="
	 */
	public class TokenRVal : Token {
		public bool sideEffects = false;  // the value (or some sub-value) has side effects
		                                  // - constants are always false
		                                  // - we assume calls always have side effects
		                                  // - post increment/decrement are always true
		                                  // - any assignment operator (=, +=, etc) always true
		                                  // - all others inherit from their operands
		public TokenRVal (Token original) : base (original) { }
	}

	/**
	 * @brief a postfix operator and corresponding L-value
	 */
	public class TokenRValAsnPost : TokenRVal {
		public TokenLVal lVal;
		public Token postfix;

		public TokenRValAsnPost (Token original) : base (original) {
			sideEffects = true;
		}
	}

	/**
	 * @brief a prefix operator and corresponding L-value
	 */
	public class TokenRValAsnPre : TokenRVal {
		public Token prefix;
		public TokenLVal lVal;

		public TokenRValAsnPre (Token original) : base (original) {
			sideEffects = true;
		}
	}

	/**
	 * @brief calling a function, ie, may have side-effects
	 */
	public class TokenRValCall : TokenRVal {

		public TokenLVal meth;  // TokenLValName or TokenLValField
		public TokenRVal args;  // null-terminated TokenRVal list
		public int nArgs;       // number of elements in args

		public TokenRValCall (Token original) : base (original) {
			sideEffects = true;
		}

		public override string ToString ()
		{
			if (args == null) {
				return meth.ToString () + " ()";
			}
			return meth.ToString () + " " + args.ToString ();
		}
	}

	/**
	 * @brief encapsulates a typecast, ie, (type)
	 */
	public class TokenRValCast : TokenRVal {
		public TokenType castTo;
		public TokenRVal rVal;

		public TokenRValCast (TokenType type, TokenRVal value) : base (type)
		{
			castTo = type;
			rVal   = value;
			sideEffects = value.sideEffects;
		}
	}

	/**
	 * @brief a floating-point number is being used as an R-value
	 */
	public class TokenRValFloat : TokenRVal {
		public TokenFloat flToken;
		public TokenRValFloat (Token original) : base (original)
		{
			this.flToken = (TokenFloat)original;
		}

		public override string ToString ()
		{
			return flToken.ToString ();
		}
	}

	/**
	 * @brief an integer number is being used as an R-value
	 */
	public class TokenRValInt : TokenRVal {
		public TokenInt inToken;
		public TokenRValInt (Token original) : base (original)
		{
			this.inToken = (TokenInt)original;
		}
	}

	/**
	 * @brief encapsulation of <rval> is <typeexp>
	 */
	public class TokenRValIsType : TokenRVal {
		public TokenRVal    rValExp;
		public TokenTypeExp typeExp;

		public TokenRValIsType (Token original) : base (original) { }
	}

	/**
	 * @brief an R-value enclosed in brackets is an LSLList
	 */
	public class TokenRValList : TokenRVal {

		public TokenRVal rVal;  // null-terminated list of TokenRVal objects
		public int nItems;

		public TokenRValList (Token original) : base (original) { }
	}

	public class TokenRValConst : TokenRVal {
		public ScriptConst val;

		public TokenRValConst (Token original) : base (original) { }
	}

	/**
	 * @brief an L-value is being used as an R-value
	 */
	public class TokenRValLVal : TokenRVal {
		public TokenLVal lvToken;
		public TokenRValLVal (Token original) : base (original)
		{
			this.lvToken = (TokenLVal)original;
		}
		public override string ToString ()
		{
			return lvToken.ToString ();
		}
	}

	/**
	 * @brief a binary operator and its two operands
	 */
	public class TokenRValOpBin : TokenRVal {
		public TokenRVal rValLeft;
		public Token opcode;
		public TokenRVal rValRight;

		public TokenRValOpBin (TokenRVal left, Token op, TokenRVal right) : base (op)
		{
			rValLeft  = left;
			opcode    = op;
			rValRight = right;

			sideEffects = left.sideEffects || right.sideEffects;
			if (!sideEffects) {
				string opStr = op.ToString ();
				sideEffects = opStr.EndsWith ("=") && (opStr != ">=") && 
				              (opStr != "<=") && (opStr != "!=") && (opStr != "==");
			}
		}

		public override string ToString ()
		{
			System.Text.StringBuilder s = new System.Text.StringBuilder ();
			s.Append ('(');
			s.Append (rValLeft.ToString ());
			s.Append (')');
			s.Append (opcode.ToString ());
			s.Append ('(');
			s.Append (rValRight.ToString ());
			s.Append (')');
			return s.ToString ();
		}
	}

	/**
	 * @brief an unary operator and its one operand
	 */
	public class TokenRValOpUn : TokenRVal {
		public Token opcode;
		public TokenRVal rVal;

		public TokenRValOpUn (Token op, TokenRVal right) : base (op)
		{
			opcode      = op;
			rVal        = right;
			sideEffects = right.sideEffects;
		}
	}

	/**
	 * @brief an R-value enclosed in parentheses
	 */
	public class TokenRValParen : TokenRVal {

		public TokenRVal rVal;

		public TokenRValParen (Token original) : base (original) { }

		public override string ToString ()
		{
			return "(" + rVal.ToString () + ")";
		}
	}

	public class TokenRValRot : TokenRVal {

		public TokenRVal xRVal;
		public TokenRVal yRVal;
		public TokenRVal zRVal;
		public TokenRVal wRVal;

		public TokenRValRot (Token original) : base (original) { }
	}

	/**
	 * @brief a string constant is being used as an R-value
	 */
	public class TokenRValStr : TokenRVal {
		public TokenStr strToken;
		public TokenRValStr (Token original) : base (original)
		{
			this.strToken = (TokenStr)original;
		}
	}

	/**
	 * @brief the 'undef' keyword is being used as a value in an expression.
	 */
	public class TokenRValUndef : TokenRVal {
		public TokenRValUndef (Token original) : base (original) { }
	}

	/**
	 * @brief put 3 RVals together as a Vector value.
	 */
	public class TokenRValVec : TokenRVal {

		public TokenRVal xRVal;
		public TokenRVal yRVal;
		public TokenRVal zRVal;

		public TokenRValVec (Token original) : base (original) { }

		public override string ToString ()
		{
			return "<" + xRVal.ToString () + "," + yRVal.ToString () + "," + zRVal.ToString () + ">";
		}
	}
	
	/**
	 * @brief encapsulates the whole script in a single token
	 */
	public class TokenScript : Token {
		public Dictionary<string, TokenDeclVar>   vars   = new Dictionary<string, TokenDeclVar>   ();
		public Dictionary<string, TokenDeclFunc>  funcs  = new Dictionary<string, TokenDeclFunc>  ();
		public TokenDeclState defaultState;
		public Dictionary<string, TokenDeclState> states = new Dictionary<string, TokenDeclState> ();

		public TokenScript (Token original) : base (original) { }

		// One big monster string for the whole script...
		public override string ToString ()
		{
			System.Text.StringBuilder s = new System.Text.StringBuilder ();
			foreach (KeyValuePair<string, TokenDeclVar> kvp in vars) {
				s.Append (kvp.Value.ToString ());
				s.Append ("\n");
			}
			foreach (KeyValuePair<string, TokenDeclFunc> kvp in funcs) {
				s.Append (kvp.Value.ToString ());
				s.Append ("\n");
			}
			s.Append (defaultState);
			s.Append ("\n");
			foreach (KeyValuePair<string, TokenDeclState> kvp in states) {
				s.Append (kvp.Value.ToString ());
				s.Append ("\n");
			}
			return s.ToString ();
		}
	}

	/**
	 * @brief state body declaration
	 */
	public class TokenStateBody : Token {

		public TokenDeclFunc eventFuncs;

		public int index = -1;  // row in ScriptHandlerEventTable (0=default)

		public TokenStateBody (Token original) : base (original) { }

		public override string ToString ()
		{
			System.Text.StringBuilder s = new System.Text.StringBuilder ("{");
			for (Token t = eventFuncs; t != null; t = t.nextToken) {
				s.Append (" ");
				s.Append (t.ToString ());
			}
			s.Append (" }");
			return s.ToString ();
		}
	}

	/**
	 * @brief a single statement, such as ending on a semicolon or enclosed in braces
	 * TokenStmt includes the terminating semicolon or the enclosing braces
	 * Also includes @label: for jump targets.
	 * Also includes stray ; null statements.
	 */
	public class TokenStmt : Token {

		public TokenStmt (Token original) : base (original) { }
	}

	/**
	 * @brief a group of statements enclosed in braces
	 */
	public class TokenStmtBlock : TokenStmt {

		public Token statements;               // null-terminated list of statements, can also have TokenDeclVar's in here
		public Token closebr;                  // close-brace token
		public TokenStmtBlock outerStmtBlock;  // next outer stmtBlock or null if top-level, ie, function definition
		public TokenDeclFunc function;         // function it is part of
		public Dictionary<string, TokenDeclVar> variables = new Dictionary<string, TokenDeclVar> ();  // variables declared herein

		public TokenStmtBlock (Token original) : base (original) { }

		public override string ToString ()
		{
			System.Text.StringBuilder s = new System.Text.StringBuilder ("{ ");
			for (Token t = statements; t != null; t = t.nextToken) {
				s.Append (t.ToString ());
				s.Append (" ");
			}
			s.Append ("}");
			return s.ToString ();
		}
	}

	/**
	 * @brief definition of branch target name
	 */
	public class TokenStmtLabel : TokenStmt {

		public TokenName name;        // the label's name
		public TokenStmtBlock block;  // which block it is defined in
		public bool hasBkwdRefs = false;

		public bool labelTagged;      // code gen: location of label
		public ScriptMyLabel labelStruct;

		public TokenStmtLabel (Token original) : base (original) { }
	}

	/**
	 * @brief those types of RVals with a semi-colon on the end
	 *        that are allowed to stand alone as statements
	 */
	public class TokenStmtRVal : TokenStmt {
		public TokenRVal rVal;

		public TokenStmtRVal (Token original) : base (original) { }

		public override string ToString ()
		{
			return rVal.ToString () + ";";
		}
	}

	/**
	 * @brief "do" statement
	 */
	public class TokenStmtDo : TokenStmt {

		public TokenStmt bodyStmt;
		public TokenRVal testRVal;

		public TokenStmtDo (Token original) : base (original) { }
	}

	/**
	 * @brief "for" statement
	 */
	public class TokenStmtFor : TokenStmt {

		public TokenStmt initStmt;  // there is always an init statement, though it may be a null statement
		public TokenRVal testRVal;  // there may or may not be a test (null if not)
		public TokenRVal incrRVal;  // there may or may not be an increment (null if not)
		public TokenStmt bodyStmt;  // there is always a body statement, though it may be a null statement

		public TokenStmtFor (Token original) : base (original) { }
	}

	/**
	 * @brief "foreach" statement
	 */
	public class TokenStmtForEach : TokenStmt {

		public TokenLVal keyLVal;
		public TokenLVal valLVal;
		public TokenLVal arrayLVal;
		public TokenStmt bodyStmt;  // there is always a body statement, though it may be a null statement

		public TokenStmtForEach (Token original) : base (original) { }
	}

	public class TokenStmtIf : TokenStmt {

		public TokenRVal testRVal;
		public TokenStmt trueStmt;
		public TokenStmt elseStmt;

		public TokenStmtIf (Token original) : base (original) { }

		public override string ToString ()
		{
			System.Text.StringBuilder s = new System.Text.StringBuilder ("if (");
			s.Append (testRVal.ToString ());
			s.Append (") ");
			s.Append (trueStmt.ToString ());
			if (elseStmt != null) {
				s.Append (" else ");
				s.Append (elseStmt.ToString ());
			}
			return s.ToString ();
		}
	}

	public class TokenStmtJump : TokenStmt {

		public TokenName label;

		public TokenStmtJump (Token original) : base (original) { }
	}

	public class TokenStmtNull : TokenStmt {

		public TokenStmtNull (Token original) : base (original) { }
	}

	public class TokenStmtRet : TokenStmt {

		public TokenRVal rVal;  // null if void

		public TokenStmtRet (Token original) : base (original) { }
	}

	/**
	 * @brief statement that changes the current state.
	 */
	public class TokenStmtState : TokenStmt {

		public TokenName state;  // null for default

		public TokenStmtState (Token original) : base (original) { }
	}

	public class TokenStmtWhile : TokenStmt {

		public TokenRVal testRVal;
		public TokenStmt bodyStmt;

		public TokenStmtWhile (Token original) : base (original) { }
	}

	/**
	 * @brief type expressions (right-hand of 'is' keyword).
	 */
	public class TokenTypeExp : Token {
		public TokenTypeExp (Token original) : base (original) { }
	}

	public class TokenTypeExpBinOp : TokenTypeExp {
		public TokenTypeExp leftOp;
		public Token        binOp;
		public TokenTypeExp rightOp;

		public TokenTypeExpBinOp (Token original) : base (original) { }
	}

	public class TokenTypeExpNot : TokenTypeExp {
		public TokenTypeExp typeExp;

		public TokenTypeExpNot (Token original) : base (original) { }
	}

	public class TokenTypeExpPar : TokenTypeExp {
		public TokenTypeExp typeExp;

		public TokenTypeExpPar (Token original) : base (original) { }
	}

	public class TokenTypeExpType : TokenTypeExp {
		public TokenType typeToken;

		public TokenTypeExpType (Token original) : base (original) { }
	}

	public class TokenTypeExpUndef : TokenTypeExp {
		public TokenTypeExpUndef (Token original) : base (original) { }
	}
}
