namespace Timberborn.TerrainGenerator.Core.Common;

// a generator could not produce terrain from its input (not a bug): the dialog tells the user instead of reporting
// an error. LocKey is the localization key of the message shown.
public sealed class GenerationFailedException : Exception
{
    public GenerationFailedException(string locKey, string message) : base(message)
    {
        LocKey = locKey;
    }

    public string LocKey { get; }
}
