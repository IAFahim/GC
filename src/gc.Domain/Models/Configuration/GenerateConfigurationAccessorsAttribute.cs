namespace System.ComponentModel;

/// <summary>
/// Marks configuration records for strongly-typed accessor generation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
internal sealed class GenerateConfigurationAccessorsAttribute : Attribute
{
}
