namespace Workit.Shared.Models;

public sealed class Job : ICreatedAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid CustomerId { get; set; }

    /// <summary>
    /// The employees put on this job. A job can exist before anyone is
    /// assigned, but until then no employee can see it or log against it —
    /// owners and admins always can. Stored as a Postgres uuid[]; order is
    /// not significant and ids are unique.
    /// </summary>
    public List<Guid> AssignedEmployeeIds { get; set; } = [];

    public string       Name          { get; set; } = string.Empty;
    public string       Code          { get; set; } = string.Empty;

    // What a crew member needs to start: where, who to ask for, what to do,
    // what to bring. Written by the owner, shown in the apps' job detail.
    public string Location            { get; set; } = string.Empty;
    public string ContactName         { get; set; } = string.Empty;
    public string ContactPhone        { get; set; } = string.Empty;
    public string Instructions        { get; set; } = string.Empty;
    public string ToolsSuggestion     { get; set; } = string.Empty;
    public string MaterialsSuggestion { get; set; } = string.Empty;
    // Where Location is on the map. Set from the address registry or by
    // dragging the pin; null when the owner only typed a location.
    public double? Latitude           { get; set; }
    public double? Longitude          { get; set; }
    // Staðfangaskrá land-parcel number of the picked address. Jobs on the
    // same parcel share it, which is how contact suggestions find "the
    // caretaker we called last time at this building".
    public int?    AddressLandNr      { get; set; }

    public BillingType  BillingType   { get; set; } = BillingType.Hourly;
    public JobCategory  Category      { get; set; } = JobCategory.NewInstallation;
    public int          JobNumber     { get; set; }
    public KanbanStatus      KanbanStatus      { get; set; } = KanbanStatus.Active;
    public string?           WaitingReason     { get; set; }
    public DateTimeOffset?   KanbanInProgressAt { get; set; }
    public DateTimeOffset?   KanbanWaitingAt    { get; set; }
    public DateTimeOffset?   KanbanDoneAt       { get; set; }

    /// <summary>Where this record came from. Defaults to Workit for manually created records.</summary>
    public DataSource Source { get; set; } = DataSource.Workit;

    /// <inheritdoc/>
    public DateTimeOffset? CreatedAt { get; set; }
    /// <inheritdoc/>
    public Guid? CreatedByUserId { get; set; }
    /// <inheritdoc/>
    public string? CreatedByName { get; set; }
}
