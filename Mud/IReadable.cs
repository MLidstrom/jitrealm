namespace JitRealm.Mud;

/// <summary>
/// Interface for objects that can be read (signs, books, scrolls, etc.).
/// </summary>
public interface IReadable : IMudObject
{
    /// <summary>
    /// The text content shown when this object is read.
    /// Can be static or dynamically generated.
    /// </summary>
    string ReadableText { get; }

    /// <summary>
    /// Short description for use in "You read the {ReadableLabel}."
    /// Example: "wooden sign", "worn scroll", "leather-bound book"
    /// </summary>
    string ReadableLabel { get; }
}
