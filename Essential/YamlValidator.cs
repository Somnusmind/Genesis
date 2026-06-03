using System;
using UnityEngine;
using YamlDotNet.Serialization;

public static class YamlValidator
{
    // Validates the YAML syntax and ensures it matches the expected Config schema.
    public static bool ValidateYamlSyntax(string yamlContent, out string errorMessage)
    {
        errorMessage = string.Empty;

        try
        {
            var deserializer = new DeserializerBuilder().Build();
            deserializer.Deserialize<Config>(yamlContent);
            return true;
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            errorMessage = $"YAML Syntax Error: {ex.Message}";
            Debug.LogError(errorMessage);
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Validation Error: {ex.Message}";
            Debug.LogError(errorMessage);
            return false;
        }
    }
}