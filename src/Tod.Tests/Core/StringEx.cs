using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Tod.Tests.Core;

internal static class StringEx
{
    private static readonly Regex s_ansiRegex = new(@"\x1b\[[0-9;]+m", RegexOptions.Compiled);

    public static string RemoveAnsiCodes(this string s)
    {
        return s_ansiRegex.Replace(s, "");
    }
}
