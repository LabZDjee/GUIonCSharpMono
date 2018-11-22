using System;

namespace IntelHexUtilityNS {
  public class IntelHexUtility
  {
    // turn hexadecimal digit in an intel hex string into a byte array
    // parameter bExtendWithNullChecksum: if true extends byte array length by one and set this last byte to zero
    // can throw exception upon syntax error
    public static byte[] TurnHexLineIntoByteArray(string hexStr, bool bExtendWithNullChecksum = false)
    {
      if (hexStr[0] != ':') {
        throw new ArgumentException("No intial column character \":\" in hexString to convert");
      }
      if (hexStr.Length % 2 == 0) {
        throw new ArgumentException("Even number of characters in hexString to convert");
      }
      int byteLength = (hexStr.Length - 1) / 2;
      byte[] result = new byte[byteLength + (bExtendWithNullChecksum?1:0)];
      int i;
      for (i = 0; i < byteLength; i++) {
        Int32 v = Convert.ToInt32(hexStr.Substring(2 * i + 1, 2), 16);
        result[i] = (byte)v;
      }
      if(bExtendWithNullChecksum)
        result[i] = 0;
      return result;
    }
    // calculates the two-complement of sum of bytes, i.e. not(sum-of-bytes)+1
    public static byte CalculateChecksum(byte [] values)
    {
      byte result = 0;
      foreach (byte b in values) {
        result += b;
      }
      result = (byte)(~result + 1);
      return result;
    }
    public static string BuildIntelHexFromByteArray(byte[] byteArray)
    {;
      if(byteArray.Length<1)
        throw new ArgumentException("byteArray length cannot be less than 1");
      byteArray[byteArray.Length-1] = 0; 
      byte c = CalculateChecksum(byteArray);
      byteArray[byteArray.Length - 1] = c;
      string result = ":" + BitConverter.ToString(byteArray).Replace("-", string.Empty);
      return result;
    }
  }
}