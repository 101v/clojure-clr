﻿/**
 *   Copyright (c) David Miller. All rights reserved.
 *   The use and distribution terms for this software are covered by the
 *   Eclipse Public License 1.0 (http://opensource.org/licenses/eclipse-1.0.php)
 *   which can be found in the file epl-v10.html at the root of this distribution.
 *   By using this software in any fashion, you are agreeing to be bound by
 * 	 the terms of this license.
 *   You must not remove this notice, or any other, from this software.
 **/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace clojure.lang
{
    /// <summary>
    /// An implementation of Java-style printf/format
    /// </summary>
    /// <remarks>
    /// <para>Primary inspiration from the Java OpenJDK source.  In fact, this is a pretty straightforward translations
    /// of the relevant parts.  I make no claims to any originality in what follows.
    /// The idea was to get something working with the minimum of thought and effort.</para>
    /// </remarks>
    public static class Printf
    {
        // TODO: extend Printf to BigInteger
        // TODO: extend Printf to BigDecimal
        // TODO: extend Printf to decimal
        // TODO: implement DateTime
        // TODO: implement HexFloat
        // TODO: implmement grouping for GeneralFloat

        #region Data

        /// <summary>
        /// Detects % formatting codes:  %[argument_index$][flags][width][.precision][t]conversion
        /// </summary>
        static Regex FormatSpecifier =
            new Regex(@"%(\d+\$)?([-#+ 0,(\<]*)?(\d+)?(\.\d+)?([tT])?([a-zA-Z%])");

        #endregion

        #region Entry point

        public static string Format(string format, params object[] args)
        {
            List<FormatChunk> chunks = Parse(format);
            StringBuilder sb = new StringBuilder();

            int lastIndex =-1;
            int lastOrdinaryIndex = -1;

            foreach (FormatChunk chunk in chunks )
            {
                int index = chunk.Index;
                switch ( index)
                {
                    case FIXED_TEXT_INDEX:
                        chunk.Print(sb,null);
                        break;

                    case PREV_ARG_INDEX:
                           if ( args != null && lastIndex > args.Length -1 )
                            throw new MissingFormatArgumentException(chunk.ToString());
                        chunk.Print(sb,args==null ? null : args[lastIndex]);
                        break;

                    case REGULAR_ARG_INDEX:
                        lastOrdinaryIndex++;
                        lastIndex = lastOrdinaryIndex;
                       if ( args != null && lastOrdinaryIndex > args.Length -1 )
                            throw new MissingFormatArgumentException(chunk.ToString());
                        chunk.Print(sb,args==null ? null : args[lastOrdinaryIndex]);
                        break;

                    default:
                        lastIndex = index-1;
                        if ( args != null && lastIndex > args.Length -1 )
                            throw new MissingFormatArgumentException(chunk.ToString());
                        chunk.Print(sb,args==null ? null : args[lastIndex]);
                        break;

                }
            }
            return sb.ToString();
        }
        

        #endregion

        #region implementation

        private static List<FormatChunk> Parse(string format)
        {
            List<FormatChunk> chunks = new List<FormatChunk>();

            MatchCollection matches = FormatSpecifier.Matches(format);
            
            // Find the text chunks between the matches.
            if (matches.Count == 0)
            {
                // no specifiers, just treat as one big text chunk
                chunks.Add(new FixedTextChunk(format));
            }
            else
            {
                int nextStart = 0;
                foreach (Match m in matches)
                {
                    if (m.Index != nextStart)
                        // we have a plain text chunk in between
                        chunks.Add(new FixedTextChunk(format.Substring(nextStart, m.Index - nextStart)));
                    chunks.Add(new FormatSpecificierChunk(m.Groups));
                    nextStart = m.Index + m.Length;
                }

                // see if there is trailing plain text chunk
                if (nextStart < format.Length)
                    chunks.Add(new FixedTextChunk(format.Substring(nextStart)));
            }

            return chunks;
        }

        #endregion

        #region FormatChunk interface

        const int FIXED_TEXT_INDEX = -2;
        const int PREV_ARG_INDEX = -1;
        const int REGULAR_ARG_INDEX = 0;
        
        interface FormatChunk
        {
            int Index { get; }
            void Print(StringBuilder sb, object arg);
        }

        #endregion

        #region Fixed text chunks

        sealed class FixedTextChunk : FormatChunk
        {
            #region Data

            string _s;

            #endregion

            #region C-tors

            public FixedTextChunk(String s) 
            {
                CheckText(s);
                _s = s;
            }

            #endregion

            #region implementation

            void CheckText(string s)
            {
                // should be no bare naked %s
                int index = s.IndexOf('%');
                if (index != -1)
                {
                    char c = index > s.Length - 2 ? '%' : s[index + 1];
                    throw new UnknownFormatConversionException(new string(c, 1));
                }
            }

            public int Index
            {
                get { return FIXED_TEXT_INDEX; }
            }

            public void Print(StringBuilder sb, object arg)
            {
                sb.Append(_s);
            }

            public override string ToString()
            {
                return _s;
            }

            #endregion
        }

        #endregion

        #region Format specifier chunks

        class FormatSpecificierChunk : FormatChunk
        {
            #region Data

            int _index = PREV_ARG_INDEX;
            public int Index
            {
                get { return _index; }
            }

            FormatFlags _flags = FormatFlags.None;
            public FormatFlags Flags
            {
                get { return _flags; }
            }

            int _width;
            public int Width
            {
                get { return _width; }
            }

            int _precision;
            public int Precision
            {
                get { return _precision; }
            }

            bool _isDateTime;
            public bool IsDateTime
            {
                get { return _isDateTime; }
            }

            char _conversion;
            public char Conversion
            {
                get { return _conversion; }
            }

            enum FormatGroup
            {
                All = 0,
                Index = 1,
                Flags = 2,
                Width = 3,
                Precision = 4,
                DateTime = 5,
                Conversion = 6
            }

            #endregion

            #region C-tors

            public FormatSpecificierChunk(GroupCollection groups)
            {
                ComputeIndex(groups[(int)FormatGroup.Index]);
                ComputeFlags(groups[(int)FormatGroup.Flags]);            // must come after ComputeIndex
                ComputeWidth(groups[(int)FormatGroup.Width]);
                ComputePrecision(groups[(int)FormatGroup.Precision]);
                ComputeDt(groups[(int)FormatGroup.DateTime]);            // must come after ComputeFlags
                ComputeConversion(groups[(int)FormatGroup.Conversion]);  // must come after ComputeFlags, ComputeDT

                if (_isDateTime)
                    CheckDateTime();

                else if (ConversionAux.IsGeneral(_conversion))
                    CheckGeneral();
                else if (ConversionAux.IsCharacter(_conversion))
                    CheckCharacter();
                else if (ConversionAux.IsInteger(_conversion))
                    CheckInteger();
                else if (ConversionAux.IsFloat(_conversion))
                    CheckFloat();
                else if (ConversionAux.IsText(_conversion))
                    CheckText();
                else
                    throw new UnknownFormatConversionException(new string(_conversion, 1));
            }

            #endregion

            #region Specifier parsing

            private void ComputeIndex(Group group)
            {
                if (group.Success)
                {
                    string val = group.Value;
                    _index = Int32.Parse(val.Substring(0, val.Length - 1));  // skip trailing $
                }
                else
                    _index = REGULAR_ARG_INDEX;
            }

            private void ComputeFlags(Group group)
            {
                _flags = ParseFlags(group.Value);
                if ((_flags & FormatFlags.Previous) != 0)
                    _index = PREV_ARG_INDEX;
            }

            private void ComputeWidth(Group group)
            {
                _width = -1;
                if (group.Success)
                {
                    _width = Int32.Parse(group.Value);
                    if (_width < 0)
                        throw new IllegalFormatWidthException(_width);
                }
            }

            private void ComputePrecision(Group group)
            {
                _precision = -1;
                if (group.Success)
                {
                    string val = group.Value;
                    _precision = Int32.Parse(val.Substring(1));  // skip leading .
                    if (_precision < 0)
                        throw new IllegalFormatPrecisionException(_precision);
                }
            }

            private void ComputeDt(Group group)
            {
                _isDateTime = false;
                if (group.Success)
                {
                    _isDateTime = true;
                    if (group.Value.Equals("T"))
                        _flags |= FormatFlags.UpperCase;
                }
            }

            private void ComputeConversion(Group group)
            {
                _conversion = group.Value[0];
                if (!_isDateTime)
                {
                    if (!ConversionAux.IsValid(_conversion))
                        throw new UnknownFormatConversionException(group.Value);
                    if (Char.IsUpper(_conversion))
                        _flags |= FormatFlags.UpperCase;
                    _conversion = Char.ToLower(_conversion);
                    if (ConversionAux.IsText(_conversion))
                        _index = FIXED_TEXT_INDEX;
                }
            }

            #endregion

            #region Validity checks

            private void CheckDateTime()
            {
                CheckNoPrecision();
                if (!DateTimeConv.IsValid(_conversion))
                    throw new UnknownFormatConversionException("t" + _conversion);
                CheckBadFlags(FormatFlags.Alternate| FormatFlags.Plus| FormatFlags.LeadingSpace| FormatFlags.ZeroPad| FormatFlags.Group| FormatFlags.Parentheses);
                CheckLeftJustifyWidth();
            }

            private void CheckGeneral()
            {
                if ((_conversion == ConversionAux.Boolean || _conversion == ConversionAux.HashCode)
                    && (_flags & FormatFlags.Alternate) != 0)
                    FailMismatch(FormatFlags.Alternate, _conversion);
                CheckLeftJustifyWidth();
                CheckBadFlags(FormatFlags.Plus|FormatFlags.LeadingSpace| FormatFlags.ZeroPad|FormatFlags.Group|FormatFlags.Parentheses);
            }


            private void CheckCharacter()
            {
                CheckNoPrecision();
                CheckBadFlags(FormatFlags.Alternate| FormatFlags.Plus| FormatFlags.LeadingSpace| FormatFlags.ZeroPad| FormatFlags.Group| FormatFlags.Parentheses);
                CheckLeftJustifyWidth();
            }

            private void CheckInteger()
            {
                CheckNumeric();
                CheckNoPrecision();
                if (_conversion == ConversionAux.DecimalInteger)
                    CheckBadFlags(FormatFlags.Alternate);
                else if (_conversion == ConversionAux.OctalInteger)
                    CheckBadFlags(FormatFlags.Group);
                else
                    CheckBadFlags(FormatFlags.Group);
            }

            private void CheckFloat()
            {
                CheckNumeric();
                if (_conversion == ConversionAux.HexFloat)
                    CheckBadFlags(FormatFlags.Parentheses | FormatFlags.Group);
                else if (_conversion == ConversionAux.Scientific)
                    CheckBadFlags(FormatFlags.Group);
                else if (_conversion == ConversionAux.General)
                    CheckBadFlags(FormatFlags.Alternate);
            }

            private void CheckNumeric()
            {
                const FormatFlags BadCombo1 = FormatFlags.Plus | FormatFlags.LeadingSpace;
                const FormatFlags BadCombo2 = FormatFlags.LeftJustify | FormatFlags.ZeroPad;

                if ( _width < -1 )
                    throw new IllegalFormatWidthException(_width);

                if ( _precision < -1 )
                    throw new IllegalFormatPrecisionException(_precision);

                // '-' and '0' require a width
                if ( _width == -1 &&
                    ( (_flags & (FormatFlags.LeftJustify | FormatFlags.ZeroPad )) != 0 ))
                    throw new MissingFormatWidthException(ToString());

                // bad combination
                if ( (_flags & BadCombo1) ==BadCombo1 || (_flags & BadCombo2 ) == BadCombo2 )
                    throw new IllegalFormatFlagsException(FormatFlagsToString(_flags));
            }

            private void CheckText()
            {
                CheckNoPrecision();
                switch (_conversion)
                {
                    case ConversionAux.PercentSign:
                        if (_flags != FormatFlags.None && _flags != FormatFlags.LeftJustify)
                            throw new IllegalFormatFlagsException(FormatFlagsToString(_flags));
                        CheckLeftJustifyWidth();
                        break;
                    case ConversionAux.LineSeparator:
                        if (_width != -1)
                            throw new IllegalFormatWidthException(_width);
                        if (_flags != FormatFlags.None)
                            throw new IllegalFormatFlagsException(FormatFlagsToString(_flags));
                        break;
                    default:
                        throw Util.UnreachableCode();
                }
            }


            // '-' requires a width
            private void CheckLeftJustifyWidth()
            {
                if (_width == -1 && (_flags & FormatFlags.LeftJustify) != 0)
                    throw new MissingFormatWidthException(ToString());
            }

            private void CheckNoPrecision()
            {
                if (_precision != -1)
                    throw new IllegalFormatPrecisionException(_precision);
            }

            private void CheckBadFlags(FormatFlags bad)
            {
                  foreach ( FormatFlags f in Enum.GetValues(typeof(FormatFlags)))

                    if ((bad & f & _flags) != 0 )
                        FailMismatch(f,_conversion);
            }

            private void FailMismatch(FormatFlags flags,char conv)
            {
 	            throw new FormatFlagsConversionMismatchException(FormatFlagsToString(flags),conv);
            }

            #endregion

            #region  Print dispatch

            public void Print(StringBuilder sb, object arg)
            {
                if (_isDateTime)
                {
                    PrintDateTime(sb, arg);
                    return;
                }

                switch (_conversion)
                {
                    case ConversionAux.DecimalInteger:
                    case ConversionAux.OctalInteger:
                    case ConversionAux.HexInteger:
                        PrintInteger(sb, arg);
                        break;
                    case ConversionAux.Scientific:
                    case ConversionAux.General:
                    case ConversionAux.DecimalFloat:
                    case ConversionAux.HexFloat:
                        PrintFloat(sb, arg);
                        break;
                    case ConversionAux.Character:
                    case ConversionAux.CharacterUpper:
                        PrintCharacter(sb, arg);
                        break;
                    case ConversionAux.Boolean:
                        PrintBoolean(sb, arg);
                        break;
                    case ConversionAux.String:
                        PrintString(sb, arg);
                        break;
                    case ConversionAux.HashCode:
                        PrintHashCode(sb, arg);
                        break;
                    case ConversionAux.LineSeparator:
                        PrintLineSeparator(sb, arg);
                        break;
                    case ConversionAux.PercentSign:
                        PrintPercentSign(sb, arg);
                        break;
                    default:
                        throw Util.UnreachableCode();
                }

            }

            #endregion

            #region Text spec printing

            private void PrintPercentSign(StringBuilder sb, object arg)
            {
                PrintWithJustification(sb, "%");
            }

            private void PrintLineSeparator(StringBuilder sb, object arg)
            {
                sb.Append('\n');
            }

            #endregion

            #region String, boolean, hashcode spec printing

            private void PrintString(StringBuilder sb, object arg)
            {
                if (arg == null)
                    PrintString(sb, "null");
                else if (arg is IFormattable)
                {
                    IFormattable f = (IFormattable)arg;
                    string s = f.ToString();
                    // TODO: figure out what formatting spec to use for strings)
                    PrintString(sb, s);
                }
                else
                    PrintString(sb,arg.ToString());
            }

            private void PrintHashCode(StringBuilder sb, object arg)
            {
                string s = arg == null ? "null" : Convert.ToString(arg.GetHashCode(),16);
                PrintString(sb,s);
            }

            private void PrintBoolean(StringBuilder sb, object arg)
            {
                string s;
                if (arg == null)
                    s = false.ToString();
                else
                    s = arg is Boolean ? ((Boolean)arg).ToString() : true.ToString();
                PrintString(sb, s);
            }

            #endregion

            #region Character printing

            private void PrintCharacter(StringBuilder sb, object arg)
            {
                if (arg == null)
                {
                    PrintString(sb, "null");
                    return;
                }
                String s = null;
                switch (Type.GetTypeCode(arg.GetType()))
                {
                    case TypeCode.Char:
                        s = ((Char)arg).ToString();
                        break;
                    case TypeCode.Byte:
                        s = Encoding.Unicode.GetString(new byte[] {(byte)arg, 0 });
                        break;
                    case TypeCode.Int16:
                        s = Encoding.Unicode.GetString(System.BitConverter.GetBytes((Int16)arg));
                        break;
                    case TypeCode.UInt16:
                        s = Encoding.Unicode.GetString(System.BitConverter.GetBytes((UInt16)arg));
                        break;
                    case TypeCode.Int32:
                        s = Encoding.UTF32.GetString(System.BitConverter.GetBytes((Int32)arg));
                        break;
                    case TypeCode.UInt32:
                        s = Encoding.UTF32.GetString(System.BitConverter.GetBytes((UInt32)arg));
                        break;
                    default:
                        FailConversion(_conversion, arg);
                        break;
                }
                PrintString(sb, s);
            }

            #endregion

            #region Float spec printing

            private void PrintFloat(StringBuilder sb, object arg)
            {
                if (arg == null)
                {
                    PrintString(sb, "null");
                    return;
                }

                switch (Type.GetTypeCode(arg.GetType()))
                {
                    case TypeCode.Single:
                        PrintDouble(sb,(double)(float)arg);
                        break;
                    case TypeCode.Double:
                        PrintDouble(sb,(double)arg);
                        break;
                    case TypeCode.Decimal:
                        PrintDecimal(sb,(decimal)arg);
                        break;
                    default:
                        FailConversion(_conversion, arg);
                        break;
                }

            }

            private void PrintDecimal(StringBuilder sb, decimal p)
            {
                throw new NotImplementedException();
            }

            private void PrintDouble(StringBuilder sb, double d)
            {
                char bclFormatFlag;
                int precision;

                switch ( _conversion )
                {
                    case ConversionAux.Scientific:
                        // TODO: Decide if we want to make this match the number of digits in the exponent.
                        // The java version uses two digits.  This flag uses three.
                        // We could get rid of the extra leading 0 in the exponent if necessary
                        bclFormatFlag = 'e';
                        precision = _precision == -1 ? 6 : _precision;
                        break;
                    case ConversionAux.DecimalFloat:
                        bclFormatFlag = 'f';
                        precision = _precision == -1 ? 6 : _precision;
                        if ((_flags & FormatFlags.Group) != 0)
                            bclFormatFlag = 'n';
                        break;
                    case ConversionAux.General:
                        // TODO: Implement grouping for General Float.
                        // This would require detecting the Fixed/Float split and
                        // switching to N format if fixed range and grouping.
                        bclFormatFlag = 'g';
                        precision = _precision == -1 ? 6 : _precision == 0 ? 1 : _precision;
                        break;
                    case ConversionAux.HexFloat:
                        // TODO: Implement HexFloat.
                        throw new NotSupportedException("HexFloat conversion not supported (yet)");
                    default:
                        throw Util.UnreachableCode();
                }

                if ((_flags & FormatFlags.UpperCase) != 0)
                    bclFormatFlag = Char.ToUpper(bclFormatFlag);


                string bclFormat = String.Format("{0}{1}", bclFormatFlag, precision);
                string val = Math.Abs(d).ToString(bclFormat);
                bool isNeg = d < 0.0;

                StringBuilder sb1 = new StringBuilder();

                PrintLeadingSign(sb1,isNeg);
                PrintMagnitude(sb1, isNeg, val);
                PrintTrailingSign(sb1, isNeg);

                PrintWithJustification(sb, sb1.ToString());
            }

            #endregion

            #region Integer spec printing

            private void PrintInteger(StringBuilder sb, object arg)
            {
                if (arg == null)
                {
                    PrintString(sb, "null");
                    return;
                }

                switch (Type.GetTypeCode(arg.GetType()))
                {
                    case TypeCode.Byte:
                        PrintInteger(sb,(ulong)(byte)arg);
                        break;

                    case TypeCode.UInt16:
                        PrintInteger(sb,(ulong)(UInt16)arg);
                        break;

                    case TypeCode.UInt32:
                        PrintInteger(sb,(ulong)(UInt32)arg);
                        break;

                    case TypeCode.UInt64:
                        PrintInteger(sb,(ulong)arg);
                        break;
                       
                    case TypeCode.SByte:

                        PrintInteger(sb,ConvertToLongWithSignExtension((sbyte)arg));
                        break;

                    case TypeCode.Int16:
                        PrintInteger(sb,ConvertToLongWithSignExtension((Int16)arg));
                        break;

                    case TypeCode.Int32:
                        PrintInteger(sb,ConvertToLongWithSignExtension((Int32)arg));
                        break;

                    case TypeCode.Int64:
                        PrintInteger(sb,(Int64)arg);
                        break;

                    default:
                        if (arg is BigInteger)
                            PrintInteger(sb, (BigInteger)arg);
                        else
                            FailConversion(_conversion, arg);
                        break;
                }
            }

            long ConvertToLongWithSignExtension(sbyte arg)
            {
                long v = arg;
                if (arg < 0 &&
                    (_conversion == ConversionAux.OctalInteger
                    || _conversion == ConversionAux.HexInteger))
                    v += (1L << 8);
                return v;            
            }

            long ConvertToLongWithSignExtension(short arg)
            {
                long v = arg;
                if (arg < 0 &&
                    (_conversion == ConversionAux.OctalInteger
                    || _conversion == ConversionAux.HexInteger))
                    v += (1L << 16);
                return v;  
            }

            long ConvertToLongWithSignExtension(int arg)
            {
                long v = arg;
                if (arg < 0 &&
                    (_conversion == ConversionAux.OctalInteger
                    || _conversion == ConversionAux.HexInteger))
                    v += (1L << 32);
                return v;  
            }

            void PrintInteger(StringBuilder sb, long val)
            {
                StringBuilder sb1 = new StringBuilder();

                if (_conversion == ConversionAux.DecimalInteger)
                {
                    bool doGrouping = (_flags & FormatFlags.Group) != 0;

                    string s = val.ToString(doGrouping ? "N0" : "G");
                    int index = s.LastIndexOf('.');
                    if (index != -1)
                        s = s.Substring(0, index);
                    bool isNeg = val < 0;
                    if (isNeg)
                        s = s.Substring(1);

                    PrintInteger(sb1, isNeg, s);
                }
                else if (_conversion == ConversionAux.OctalInteger)
                {
                    PrintIntOctHex(sb1, Convert.ToString(val,8));
                }
                else if (_conversion == ConversionAux.HexInteger)
                {
                    PrintIntOctHex(sb1, Convert.ToString(val, 16));
                }

                PrintWithJustification(sb, sb1.ToString());
            }

            void PrintInteger(StringBuilder sb, ulong val)
            {
                StringBuilder sb1 = new StringBuilder();

                if (_conversion == ConversionAux.DecimalInteger)
                {
                    bool doGrouping = (_flags & FormatFlags.Group) != 0;
                    string s = val.ToString(doGrouping ? "N0" : "G");
                    int index = s.LastIndexOf('.');
                    if (index != -1)
                        s = s.Substring(0, index);
                    PrintInteger(sb1, false, s);
                }
                else if (_conversion == ConversionAux.OctalInteger)
                {
                     PrintIntOctHex(sb1, Convert.ToString((long)val,8));  // huh.  ConvertToString doesnn't take base with long.
               }
                else if (_conversion == ConversionAux.HexInteger)
                {
                     PrintIntOctHex(sb1, Convert.ToString((long)val, 16));
               }

                PrintWithJustification(sb, sb1.ToString());
            }

            void PrintInteger(StringBuilder sb, BigInteger val)
            {
                StringBuilder sb1 = new StringBuilder();
                bool neg = val.IsNegative;
                BigInteger v = val.Abs();

                PrintLeadingSign(sb1,neg);

                if ( _conversion ==  ConversionAux.DecimalInteger )
                        PrintMagnitude(sb1,neg,v.ToString());
                else
                {
                    string s = v.ToString( _conversion == ConversionAux.OctalInteger ? 8u : 16u );
                    PrintIntOctHex(sb1,s,neg,true);
                }

                PrintTrailingSign(sb1,neg);
                PrintWithJustification(sb,sb1.ToString());
            }

 

            void PrintIntOctHex(StringBuilder sb, string val)
            {
                PrintIntOctHex(sb,val,false, false);
            }

           void PrintIntOctHex(StringBuilder sb, string val, bool isNeg, bool isBigInt)
           {
                if ( ! isBigInt )
                    CheckBadFlags(FormatFlags.Parentheses| FormatFlags.LeadingSpace | FormatFlags.Plus);

                int len = val.Length; 
                    
                if ( (_flags & FormatFlags.Alternate ) != 0 )
                {
                    if(  _conversion == ConversionAux.OctalInteger )
                    {
                        sb.Append('0');
                        len += 1;
                    }
                    else // hex
                    {
                        sb.Append( (_flags & FormatFlags.UpperCase )!= 0 ? "0X" : "0x");
                        len += 2;
                    }
                }

               // Duplicates some code in PrintInteger(StringBuilder,bool,string)
                int newW = _width;
                if (newW != -1 && isNeg)
                {
                    newW--;
                    if ((_flags & FormatFlags.Parentheses) != 0)
                        newW--;
                }

                int padSize = newW - len;

                if ( (_flags & FormatFlags.ZeroPad) != 0 && padSize > 0 )
                    sb.Append('0',padSize);

                if ( (_flags & FormatFlags.UpperCase )!= 0 )
                    val = val.ToUpper();

                sb.Append(val);
            }

            private void PrintInteger(StringBuilder sb, bool isNeg, string val)
            {
                PrintLeadingSign(sb, isNeg);
                PrintMagnitude(sb, isNeg, val);
                PrintTrailingSign(sb, isNeg);
            }

            void PrintLeadingSign(StringBuilder sb, bool isNeg)
            {
                if (isNeg)
                    sb.Append((_flags & FormatFlags.Parentheses) != 0 ? '(' : '-');
                else
                {
                    if ((_flags & FormatFlags.Plus) != 0)
                        sb.Append('+');
                    else if ((_flags & FormatFlags.LeadingSpace) != 0)
                        sb.Append(' ');
                }
            }

            void PrintTrailingSign(StringBuilder sb, bool isNeg)
            {
                if (isNeg && (_flags & FormatFlags.Parentheses) != 0)
                    sb.Append(')');
            }

            void PrintMagnitude(StringBuilder sb, bool isNeg, string val)
            {
                int newW = _width;
                if (newW != -1 && isNeg )
                { 
                    newW--;
                    if ((_flags & FormatFlags.Parentheses ) != 0 )
                        newW--;
                }
                int padSize = newW - val.Length;
                
                if ( newW != -1 && (_flags & FormatFlags.ZeroPad )!= 0 && padSize > 0 )
                    sb.Append('0',padSize);
                sb.Append(val);
            }

            #endregion

            #region DataTime spec printing

            private void PrintDateTime(StringBuilder sb, object arg)
            {
                throw new NotImplementedException();
            }

            #endregion

            #region Printing support

            private void PrintString(StringBuilder sb, string s)
            {
                if (_precision != -1 && _precision < s.Length)
                    s = s.Substring(0, _precision);
                if ((_flags & FormatFlags.UpperCase) != 0)
                    s = s.ToUpper();
                PrintWithJustification(sb,s);
            }

            private void PrintWithJustification(StringBuilder sb, string s)
            {
                if (_width == -1)
                {
                    sb.Append(s);
                    return;
                }
                
                bool addRight = (Flags & FormatFlags.LeftJustify) != 0;
                int padSpace = _width - s.Length;
                if (!addRight && padSpace > 0)
                    sb.Append(' ', padSpace);
                sb.Append(s);
                if (addRight && padSpace > 0)
                    sb.Append(' ', padSpace);
            }

            private void FailConversion(char conv, object arg)
            {
                throw new IllegalFormatConversionException(conv, arg.GetType());
            }

            #endregion

            #region Object overrides

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder("%");

                bool isUpper = (_flags & FormatFlags.UpperCase) != 0;

                if (_index > 0)
                    sb.Append(_index).Append('$');
                sb.Append(FormatFlagsToString(_flags & ~FormatFlags.UpperCase));
                if (_width != -1)
                    sb.Append(Width);
                if (_precision != -1)
                    sb.Append('.').Append(_precision);
                if (_isDateTime)
                    sb.Append(isUpper ? 'T' : 't');
                sb.Append(isUpper ? Char.ToUpper(_conversion) : _conversion);

                return sb.ToString();
            }

            #endregion
        }

        #endregion

        #region Format flags

        [Flags]
        enum FormatFlags
        {
            None = 0,                 // ''

            // misc
            LeftJustify = 1 << 0,     // '-'
            UpperCase = 1 << 1,       // '^'
            Alternate = 1 << 2,       // '#'

            // numerics
            Plus = 1 << 3,             // '+'
            LeadingSpace = 1 << 4,     // ' '
            ZeroPad = 1 << 5,          // '0'
            Group = 1 << 6,            // ','
            Parentheses = 1 << 7,      // '('

            // indexing
            Previous = 1 << 8          // '<'
        }

            
        static FormatFlags ParseFlags(string s)
        {
            FormatFlags flags = FormatFlags.None;

            foreach (char c in s)
            {
                FormatFlags flag = TranslateFlag(c);
                if ((flags & flag) != 0)
                    throw new DuplicateFormatFlagsException(FormatFlagsToString(flag));
                flags |= flag;
            }
            return flags;
        }

        static FormatFlags TranslateFlag(char c)
        {
            switch (c)
            {
                case '-': return FormatFlags.LeftJustify;
                case '#': return FormatFlags.Alternate;
                case '+': return FormatFlags.Plus;
                case ' ': return FormatFlags.LeadingSpace;
                case '0': return FormatFlags.ZeroPad;
                case ',': return FormatFlags.Group;
                case '(': return FormatFlags.Parentheses;
                case '<': return FormatFlags.Previous;
                default:
                    throw new UnknownFormatFlagsException(new String(c, 1));
            }
        }

        static string FormatFlagsToString(FormatFlags flags)
        {
            StringBuilder sb = new StringBuilder();

            if ((flags & FormatFlags.LeftJustify) != 0)
                sb.Append('-');
            if ((flags & FormatFlags.UpperCase) != 0)
                sb.Append('^');
            if ((flags & FormatFlags.Alternate) != 0)
                sb.Append('#');
            if ((flags & FormatFlags.Plus) != 0)
                sb.Append('+');
            if ((flags & FormatFlags.LeadingSpace) != 0)
                sb.Append(' ');
            if ((flags & FormatFlags.ZeroPad) != 0)
                sb.Append('0');
            if ((flags & FormatFlags.Group) != 0)
                sb.Append(',');
            if ((flags & FormatFlags.Parentheses) != 0)
                sb.Append('(');
            if ((flags & FormatFlags.Previous) != 0)
                sb.Append('<');

            return sb.ToString();
        }


        #endregion

        #region Support classes

        public static class ConversionAux
        {
            public const char DecimalInteger = 'd';
            public const char OctalInteger = 'o';
            public const char HexInteger = 'x';
            public const char HexIntegerUpper = 'X';

            public const char Scientific = 'e';
            public const char ScientificUpper = 'E';
            public const char General = 'g';
            public const char GeneralUpper = 'G';
            public const char DecimalFloat = 'f';
            public const char HexFloat = 'a';
            public const char HexFloatUpper = 'A';

            public const char Character = 'c';
            public const char CharacterUpper = 'C';

            public const char DateTime = 't';
            public const char DateTimeUpper = 'T';

            public const char Boolean = 'b';
            public const char BooleanUpper = 'B';

            public const char String = 's';
            public const char StringUpper = 'S';

            public const char HashCode = 'h';
            public const char HashCodeUpper = 'H';

            public const char LineSeparator = 'n';
            public const char PercentSign = '%';

            public static bool IsGeneral(char c)
            {
                switch (c)
                {
                    case Boolean:
                    case BooleanUpper:
                    case String:
                    case StringUpper:
                    case HashCode:
                    case HashCodeUpper:
                        return true;
                    default:
                        return false;
                }
            }

            public static bool IsCharacter(char c)
            {
                switch (c)
                {
                    case Character:
                    case CharacterUpper:
                        return true;
                    default:
                        return false;
                }
            }

            public static bool IsInteger(char c)
            {
                switch (c)
                {
                    case DecimalInteger:
                    case OctalInteger:
                    case HexInteger:
                    case HexIntegerUpper:
                        return true;
                    default:
                        return false;
                }
            }

            public static bool IsFloat(char c)
            {
                switch (c)
                {
                    case Scientific:
                    case ScientificUpper:
                    case General:
                    case GeneralUpper:
                    case DecimalFloat:
                    case HexFloat:
                    case HexFloatUpper:
                        return true;
                    default:
                        return false;
                }
            }

            public static bool IsText(char c)
            {
                switch (c)
                {
                    case LineSeparator:
                    case PercentSign:
                        return true;
                    default:
                        return false;
                }
            }

            public static bool IsValid(char c)
            {
                return IsGeneral(c) || IsInteger(c) || IsFloat(c) || IsText(c) || c == 't' || IsCharacter(c);
            }
        }

        public static class DateTimeConv
        {
            public static bool IsValid(char c)
            {
                throw new NotImplementedException("Feeling lazy");
            }

        }

        #endregion
    }

    #region Exceptions

    public class UnknownFormatFlagsException : ArgumentException
    {
        public UnknownFormatFlagsException(string message)
            : base(message)
        {
        }
    }

    public class IllegalFormatFlagsException : ArgumentException
    {
        public IllegalFormatFlagsException(string message)
            : base(message)
        {
        }
    }


    public class IllegalFormatConversionException : ArgumentException
    {
        public IllegalFormatConversionException(string message)
            : base(message)
        {
        }

        public IllegalFormatConversionException(char c, Type type)
            : base(String.Format("Type {0} invalid for conversion {1}",type,c))
        {
        }
    }

    public class FormatFlagsConversionMismatchException : ArgumentException
    {
        public FormatFlagsConversionMismatchException(string message)
            : base(message)
        {
        }

        public FormatFlagsConversionMismatchException(string flags, char c)
            : base(String.Format("Mismatch between flags {0} and conversion character {1}",flags,c))
        {
        }
    }


    public class DuplicateFormatFlagsException : ArgumentException
    {
        public DuplicateFormatFlagsException(string message)
            : base(message)
        {
        }
    }

    public class UnknownFormatConversionException : ArgumentException
    {
        public UnknownFormatConversionException(string message)
            : base(message)
        {
        }
    }

    public class IllegalFormatWidthException : ArgumentException
    {
        public IllegalFormatWidthException(string message)
            : base(message)
        {
        }

        public IllegalFormatWidthException(int width)
            : this (String.Format("Bad format width: {0}",width))
        {
        }
    }     
    
    public class MissingFormatWidthException : ArgumentException
    {
        public MissingFormatWidthException(string message)
            : base(message)
        {
        }
    }

    public class IllegalFormatPrecisionException : ArgumentException
    {
        public IllegalFormatPrecisionException(string message)
            : base(message)
        {
        }

        public IllegalFormatPrecisionException(int precision)
            : this (String.Format("Bad format precision: {0}",precision))
        {
        }
    }


        public class MissingFormatArgumentException : ArgumentException
    {
 
        public MissingFormatArgumentException(string format)
            : base (String.Format("Missing argument for formation specificer: {0}",format))
        {
        }
    }
    

    #endregion
}