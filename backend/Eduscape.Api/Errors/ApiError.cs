namespace Eduscape.Api.Errors;

/// <summary>Body of every error response: a code the frontend translates, never a sentence.</summary>
/// <param name="Code">A code from <see cref="ApiErrors"/> or <see cref="Domain.Common.DomainErrors"/>.</param>
public sealed record ApiError(string Code);
