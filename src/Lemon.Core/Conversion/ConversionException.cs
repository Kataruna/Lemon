namespace Lemon.Core.Conversion;

/// <summary>A conversion failed for a reason worth showing to the user.</summary>
public sealed class ConversionException(string message) : Exception(message);
