using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Yafex;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Aes128Ecb), "aes-128-ecb")]
[JsonDerivedType(typeof(Aes256Ecb), "aes-256-ecb")]
[JsonDerivedType(typeof(Aes128Cbc), "aes-128-cbc")]
[JsonDerivedType(typeof(Aes256Cbc), "aes-256-cbc")]
public class KeyDTO
{
    [JsonIgnore]
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

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

public class KeyCollectionDTO : KeySecretDTO
{
    [JsonPropertyName("keys")]
    public List<KeyDTO> Keys { get; set; } = new List<KeyDTO>();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeyCollectionDTO), "collection")]
public class KeySecretDTO
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    [JsonIgnore]
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
