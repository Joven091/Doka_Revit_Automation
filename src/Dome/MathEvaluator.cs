#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace EngineeringDashboard.Dome;

/// <summary>Bounded arithmetic parser. A failed expression is never a physical zero.</summary>
public static class MathEvaluator
{
    public const int MaxExpressionLength = 256;

    public static bool TryEvaluate(string? expression, out double value, out string error)
    {
        value = 0;
        error = "";
        if (expression is null || string.IsNullOrWhiteSpace(expression)) { error = "Enter a value."; return false; }
        if (expression.Length > MaxExpressionLength) { error = "Expression is too long (maximum 256 characters)."; return false; }
        if (expression.Contains(',')) { error = "Use a dot for decimals and no thousands separators."; return false; }
        try { value = new Parser(expression).Parse(); return true; }
        catch (FormatException ex) { error = ex.Message; return false; }
    }

    public static double Evaluate(string expression) => TryEvaluate(expression, out var value, out var error)
        ? value : throw new FormatException(error);

    private sealed class Parser(string text)
    {
        private int position;
        private int depth;
        public double Parse()
        {
            double value = Sum();
            SkipSpace();
            if (position != text.Length) throw new FormatException("Use numbers, +, -, *, / and parentheses only.");
            return Finite(value);
        }
        private double Sum()
        {
            double value = Product();
            while (true)
            {
                if (Take('+')) value = Finite(value + Product());
                else if (Take('-')) value = Finite(value - Product());
                else return value;
            }
        }
        private double Product()
        {
            double value = Factor();
            while (true)
            {
                if (Take('*')) value = Finite(value * Factor());
                else if (Take('/'))
                {
                    double divisor = Factor();
                    if (divisor == 0) throw new FormatException("Division by zero is not allowed.");
                    value = Finite(value / divisor);
                }
                else return value;
            }
        }
        private double Factor()
        {
            if (++depth > 32) throw new FormatException("Expression nesting is too deep (maximum 32).");
            try
            {
                if (Take('+')) return Factor();
                if (Take('-')) return -Factor();
                if (Take('('))
                {
                    double value = Sum();
                    if (!Take(')')) throw new FormatException("Missing closing parenthesis.");
                    return value;
                }
                SkipSpace();
                int start = position;
                while (position < text.Length && ((text[position] >= '0' && text[position] <= '9') || text[position] == '.')) position++;
                if (!double.TryParse(text.Substring(start, position - start), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out double number)) throw new FormatException("Enter a complete number or expression.");
                return Finite(number);
            }
            finally { depth--; }
        }
        private bool Take(char token)
        {
            SkipSpace();
            if (position >= text.Length || text[position] != token) return false;
            position++;
            return true;
        }
        private void SkipSpace() { while (position < text.Length && char.IsWhiteSpace(text[position])) position++; }
        private static double Finite(double value) => Numeric.IsFinite(value) ? value
            : throw new FormatException("The result must be finite.");
    }
}
