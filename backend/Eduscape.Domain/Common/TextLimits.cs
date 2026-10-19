namespace Eduscape.Domain.Common;

/// <summary>Maximum lengths shared by domain validation and the database schema.</summary>
public static class TextLimits
{
    public const int Title = 200;
    public const int Name = 100;
    public const int StorageKey = 500;
    public const int Question = 1000;
    public const int Answer = 200;
    public const int Hint = 1000;
    public const int LetterText = 4000;
    public const int Nickname = 20;
    public const int DeviceToken = 64;
    public const int EnumKey = 32;
}
