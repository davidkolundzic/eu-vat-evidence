using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VatEvidence.Application.Crypto
{
  public static class Hashing
  {

    public static string Sha256Hex(string input)
    {
      var bytes = Encoding.UTF8.GetBytes(input);
      var hash = SHA256.HashData(bytes);
      return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Minimal canonicalization: parse + write with stable options
    // (System.Text.Json writes properties in the order it receives them, so
    // canonicalization here is "good enough MVP" if you store JSON consistently.)
    // For stronger canonicalization, see note below.
    public static string CanonicalJsonOrEmpty(JsonDocument? doc)
    {
      if (doc is null) return "";
      return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
      {
        WriteIndented = false
      });
    }

  }
}
