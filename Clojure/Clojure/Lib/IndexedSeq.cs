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

namespace clojure.lang
{
    /// <summary>
    /// Indicates a sequence that has a current index.
    /// </summary>
    public interface IndexedSeq : ISeq, Counted
    {
        /// <summary>
        /// Gets the index associated with this sequence.
        /// </summary>
        /// <returns>The index associated with this sequence.</returns>
        int index();
    }
}
