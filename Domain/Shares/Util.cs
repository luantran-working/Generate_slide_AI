using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Shares
{
    public static class Util
    {
        public static byte[] ConvertHexStringToByteArray(string hex)
        {
            int len = hex.Length;
            byte[] result = new byte[len / 2];
            for (int i = 0; i < len; i += 2)
            {
                result[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return result;
        }

        public static string NormalizeVietnameseText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            return input
                .Replace("...", ".")
                .Replace("..", ".")
                .Replace("  ", " ")
                .Replace(",", ", ")
                .Replace(".", ". ")
                .Replace("!", "! ")
                .Replace("?", "? ")
                .Replace(":", ": ")
                .Replace(";", "; ")
                .Trim();
        }

    }
}
