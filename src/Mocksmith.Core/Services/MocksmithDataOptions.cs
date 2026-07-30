namespace Mocksmith.Core.Services;

/// <summary>Location of the data root holding the SQLite DB, sample files, and assets.</summary>
public class MocksmithDataOptions
{
    public required string RootPath { get; init; }
}
