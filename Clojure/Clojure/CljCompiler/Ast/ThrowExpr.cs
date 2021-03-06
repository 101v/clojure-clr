﻿/**
 *   Copyright (c) Rich Hickey. All rights reserved.
 *   The use and distribution terms for this software are covered by the
 *   Eclipse Public License 1.0 (http://opensource.org/licenses/eclipse-1.0.php)
 *   which can be found in the file epl-v10.html at the root of this distribution.
 *   By using this software in any fashion, you are agreeing to be bound by
 * 	 the terms of this license.
 *   You must not remove this notice, or any other, from this software.
 **/

/**
 *   Author: David Miller
 **/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
#if CLR2
using Microsoft.Scripting.Ast;
#else
using System.Linq.Expressions;
#endif


namespace clojure.lang.CljCompiler.Ast
{
    class ThrowExpr : UntypedExpr
    {
        #region Data

        readonly Expr _excExpr;

        #endregion

        #region Ctors

        public ThrowExpr(Expr excExpr)
        {
            _excExpr = excExpr;
        }

        #endregion

        #region Parsing

        public sealed class Parser : IParser
        {
            public Expr Parse(ParserContext pcon, object form)
            {
                if (pcon.Rhc == RHC.Eval)
                    return Compiler.Analyze(pcon, RT.list(RT.list(Compiler.FN, PersistentVector.EMPTY, form)), "throw__" + RT.nextID());

                return new ThrowExpr(Compiler.Analyze(pcon.SetRhc(RHC.Expression).SetAssign(false), RT.second(form)));
            }
        }

        #endregion

        #region eval

        public override object Eval()
        {
            throw new Exception("Can't eval throw");
        }

        #endregion

        #region Code generation

        public override Expression GenCode(RHC rhc, ObjExpr objx, GenContext context)
        {
            Expression exc = _excExpr.GenCode(RHC.Expression, objx, context);
            Expression exc2 = Expression.Convert(exc, typeof(Exception));

            return Expression.Throw(exc2,typeof(object));
        }

        #endregion
    }
}
