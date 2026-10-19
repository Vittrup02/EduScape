namespace Eduscape.Domain.Rooms;

/// <summary>
/// School subject. Stored by name in the database and translated in the frontend,
/// so new values can be added without a migration.
/// </summary>
public enum Subject
{
    Danish,
    Mathematics,
    English,
    German,
    French,
    Spanish,
    NatureTechnology,
    Biology,
    PhysicsChemistry,
    Physics,
    Chemistry,
    Geography,
    History,
    SocialStudies,
    ReligiousStudies,
    VisualArts,
    Music,
    CraftAndDesign,
    FoodKnowledge,
    PhysicalEducation,
    Philosophy,
    Psychology,
    BusinessEconomics,
    Informatics,
    Interdisciplinary,
}
