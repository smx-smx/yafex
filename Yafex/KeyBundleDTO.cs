using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Yafex;

public class Aes128Ecb : BasicKeyDTO { }
public class Aes256Ecb : BasicKeyDTO { }
public class Aes128Cbc : KeyWithIVDTO { }
public class Aes256Cbc : KeyWithIVDTO { }

public class BasicKeyDTO : KeyDTO
{
    [JsonPropertyName("key")]
    public required string KeyMaterial { get; set; }
}

public class KeyWithIVDTO : BasicKeyDTO
{
    [JsonPropertyName("iv")]
    public required string KeyIV { get; set; }
}

public class KeyCollectionDTO : KeyDTO
{
    [JsonPropertyName("keys")]
    public List<KeyDTO> Keys { get; set; } = new List<KeyDTO>();
}

/// <summary>Arbitrary non-AES key material (hex), with optional hex IV.
/// For ciphers like the MTK PKG old Feistel whose keys are not valid AES
/// key lengths and must bypass the AES-only <see cref="KeyEntry"/> path.</summary>
public class RawKeyDTO : KeyDTO
{
    [JsonPropertyName("key")]
    public required string KeyMaterial { get; set; }

    [JsonPropertyName("iv")]
    public string? KeyIV { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeyCollectionDTO), "collection")]
[JsonDerivedType(typeof(Aes128Ecb), "aes-128-ecb")]
[JsonDerivedType(typeof(Aes256Ecb), "aes-256-ecb")]
[JsonDerivedType(typeof(Aes128Cbc), "aes-128-cbc")]
[JsonDerivedType(typeof(Aes256Cbc), "aes-256-cbc")]
[JsonDerivedType(typeof(RawKeyDTO), "raw")]
public class KeyDTO
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    [JsonIgnore]
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
