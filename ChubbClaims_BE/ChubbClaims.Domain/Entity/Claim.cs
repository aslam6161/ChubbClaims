using ChubbClaims.Domain.Constants;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Events;
using ChubbClaims.Domain.Exceptions;
using ChubbClaims.Domain.Rules;
using ChubbClaims.Domain.ValueObjects;

namespace ChubbClaims.Domain.Entity;

/// <summary>
/// The claim aggregate root. Every change goes through a method here, and each method checks,
/// in this order:
/// 1. the lifecycle transition is allowed from the current status (else 409),
/// 2. the actor may do it (else 403),
/// 3. the input is valid (else 422),
/// then changes state, writes audit rows and raises a domain event.
/// Transition first, so a second officer picking up an already picked-up claim gets 409 "conflict",
/// not 403. Time is passed in (from TimeProvider) so the domain never reads the clock.
/// All amounts are in the claim's <see cref="Currency"/>, which comes from the policy.
/// </summary>
public sealed class Claim : AggregateRoot
{
    private readonly List<InformationRequest> _informationRequests = [];
    private readonly List<ClaimStatusChange> _statusHistory = [];
    private readonly List<ReserveChange> _reserveHistory = [];
    private readonly List<ClaimNote> _notes = [];

    private Claim() { } // EF Core

    public string ClaimNumber { get; private set; } = null!;
    public Market Market { get; private set; }
    public string Currency { get; private set; } = null!;
    public ClaimType Type { get; private set; }
    public string PolicyNumber { get; private set; } = null!;
    public Guid ClaimantId { get; private set; }

    public DateOnly IncidentDate { get; private set; }
    public DateTimeOffset ReportedAt { get; private set; }
    public string IncidentLocation { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public decimal? EstimatedLossAmount { get; private set; }
    public MotorDetails? MotorDetails { get; private set; }
    public PropertyDetails? PropertyDetails { get; private set; }

    public ClaimStatus Status { get; private set; }
    public Guid? AssignedOfficerId { get; private set; }
    public Priority Priority { get; private set; }

    public decimal ReserveAmount { get; private set; }
    public decimal? SettlementAmount { get; private set; }
    public decimal PaidAmount { get; private set; }

    public DateTimeOffset SlaDueAt { get; private set; }
    public DateTimeOffset? DecisionAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public DateTimeOffset LastStatusChangeAt { get; private set; }

    public IReadOnlyCollection<InformationRequest> InformationRequests => _informationRequests;
    public IReadOnlyCollection<ClaimStatusChange> StatusHistory => _statusHistory;
    public IReadOnlyCollection<ReserveChange> ReserveHistory => _reserveHistory;
    public IReadOnlyCollection<ClaimNote> Notes => _notes;

    public bool IsClosed => Status is ClaimStatus.Settled or ClaimStatus.Rejected;

    /// <summary>The liability still carried for this claim. Zero once it is closed.</summary>
    public decimal OutstandingReserve => IsClosed ? 0m : ReserveAmount - PaidAmount;

    public InformationRequest? OpenInformationRequest => _informationRequests.SingleOrDefault(r => r.IsOpen);

    // ---------------------------------------------------------------- FNOL

    /// <summary>Creates a claim from a first notice of loss, enforcing the FNOL business rules.</summary>
    public static Claim Submit(
        IncidentReport report, Policy policy, Actor claimant, ClaimNumber claimNumber,
        ClaimSubmissionRules rules, DateTimeOffset now)
    {
        if (claimant.Role != UserRole.Claimant)
            throw new ClaimAccessDeniedException("Only a claimant can report an incident.");
        if (policy.HolderUserId != claimant.UserId)
            throw new ClaimAccessDeniedException("You can only claim on your own policy.");

        if (report.Type != policy.ProductType)
            throw new DomainValidationException($"Policy {policy.PolicyNumber} covers {policy.ProductType}, not {report.Type}.");
        ValidateTypeSpecificDetails(report);

        var location = Guard.Required(report.IncidentLocation, "Incident location");
        var description = Guard.Required(report.Description, "Description");
        if (report.EstimatedLossAmount is <= 0)
            throw new DomainValidationException("Estimated loss must be greater than zero when provided.");

        var today = Markets.LocalDate(policy.Market, now);
        if (report.IncidentDate > today)
            throw new DomainValidationException("Incident date cannot be in the future.");
        if (report.IncidentDate < today.AddDays(-rules.MaxIncidentAgeDays))
            throw new DomainValidationException($"Incidents must be reported within {rules.MaxIncidentAgeDays} days.");
        if (!policy.IsActive)
            throw new DomainValidationException($"Policy {policy.PolicyNumber} is not active.");
        if (!policy.Covers(report.IncidentDate))
            throw new DomainValidationException($"Policy {policy.PolicyNumber} was not in force on {report.IncidentDate:yyyy-MM-dd}.");

        // Formula reserve: the claimant's estimate if given, otherwise the configured default,
        // so exposure is meaningful from the moment the claim is reported.
        var initialReserve = report.EstimatedLossAmount ?? rules.DefaultReserve;

        var claim = new Claim
        {
            ClaimNumber = claimNumber.Value,
            Market = policy.Market,
            Currency = policy.Currency,
            Type = report.Type,
            PolicyNumber = policy.PolicyNumber,
            ClaimantId = claimant.UserId,
            IncidentDate = report.IncidentDate,
            ReportedAt = now,
            IncidentLocation = location,
            Description = description,
            EstimatedLossAmount = report.EstimatedLossAmount,
            MotorDetails = report.MotorDetails,
            PropertyDetails = report.PropertyDetails,
            Status = ClaimStatus.Submitted,
            Priority = report.EstimatedLossAmount > rules.HighPriorityThreshold ? Priority.High : Priority.Normal,
            ReserveAmount = initialReserve,
            SlaDueAt = now.AddDays(rules.SlaDaysToDecision),
            LastStatusChangeAt = now,
        };

        claim._statusHistory.Add(new ClaimStatusChange(claim.Id, null, ClaimStatus.Submitted, claimant.UserId, claimant.Role, now, null));
        claim._reserveHistory.Add(new ReserveChange(claim.Id, 0m, initialReserve, claimant.UserId, now,
            report.EstimatedLossAmount is null ? "Initial reserve (default)" : "Initial reserve (claimant estimate)"));
        claim.Raise(new ClaimSubmitted(claim.Id, now, claimant.UserId, initialReserve, claim.SlaDueAt));
        return claim;
    }

    private static void ValidateTypeSpecificDetails(IncidentReport report)
    {
        switch (report.Type)
        {
            case ClaimType.Motor when report.MotorDetails is null:
                throw new DomainValidationException("Motor claims need vehicle details.");
            case ClaimType.Motor when report.PropertyDetails is not null:
                throw new DomainValidationException("Motor claims cannot have property details.");
            case ClaimType.Property when report.PropertyDetails is null:
                throw new DomainValidationException("Property claims need property details.");
            case ClaimType.Property when report.MotorDetails is not null:
                throw new DomainValidationException("Property claims cannot have vehicle details.");
        }
    }

    // ---------------------------------------------------------------- Officer actions

    public void PickUp(Actor officer, DateTimeOffset now)
    {
        var next = ClaimTransitions.Next(Status, ClaimAction.PickUp);
        EnsureStaffInMarket(officer);

        AssignedOfficerId = officer.UserId;
        MoveTo(next, officer, now, reason: null);
        Raise(new ClaimPickedUp(Id, now, officer.UserId));
    }

    public InformationRequest RequestInformation(Actor officer, string question, DateTimeOffset now)
    {
        var next = ClaimTransitions.Next(Status, ClaimAction.RequestInformation);
        EnsureAssignedOfficer(officer);
        var text = Guard.Required(question, "Question");
        if (OpenInformationRequest is not null)
            throw new DomainValidationException("There is already an open information request on this claim.");

        var request = new InformationRequest(Id, officer.UserId, text, now);
        _informationRequests.Add(request);
        MoveTo(next, officer, now, reason: null);
        Raise(new InformationRequested(Id, now, request.Id, text));
        return request;
    }

    public void AdjustReserve(Actor officer, decimal newAmount, string reason, DateTimeOffset now)
    {
        _ = ClaimTransitions.Next(Status, ClaimAction.AdjustReserve); // status does not change
        EnsureAssignedOfficer(officer);
        var why = Guard.Required(reason, "Reason");
        if (newAmount < 0)
            throw new DomainValidationException("Reserve cannot be negative.");
        if (newAmount == ReserveAmount)
            throw new DomainValidationException("New reserve is the same as the current reserve.");

        var old = ReserveAmount;
        ReserveAmount = newAmount;
        _reserveHistory.Add(new ReserveChange(Id, old, newAmount, officer.UserId, now, why));
        Raise(new ReserveAdjusted(Id, now, old, newAmount, why));
    }

    public void Approve(Actor officer, decimal settlementAmount, string? note, DateTimeOffset now)
    {
        var next = ClaimTransitions.Next(Status, ClaimAction.Approve);
        EnsureAssignedOfficer(officer);
        if (settlementAmount <= 0)
            throw new DomainValidationException("Settlement amount must be greater than zero.");

        // The reserve becomes the agreed settlement amount: that is now the exact liability.
        var oldReserve = ReserveAmount;
        SettlementAmount = settlementAmount;
        ReserveAmount = settlementAmount;
        DecisionAt = now;
        if (oldReserve != settlementAmount)
            _reserveHistory.Add(new ReserveChange(Id, oldReserve, settlementAmount, officer.UserId, now, "Approved: reserve set to settlement amount"));
        if (!string.IsNullOrWhiteSpace(note))
            _notes.Add(new ClaimNote(Id, officer.UserId, note.Trim(), now));

        MoveTo(next, officer, now, reason: null);
        Raise(new ClaimApproved(Id, now, settlementAmount, oldReserve, settlementAmount));
    }

    public void Settle(Actor officer, DateTimeOffset now)
    {
        var next = ClaimTransitions.Next(Status, ClaimAction.Settle);
        EnsureAssignedOfficer(officer);

        var released = OutstandingReserve; // captured before closing
        PaidAmount = SettlementAmount!.Value; // always set: Settle is only allowed from Approved
        ClosedAt = now;
        MoveTo(next, officer, now, reason: null);
        Raise(new ClaimSettled(Id, now, PaidAmount, released));
    }

    public void Reject(Actor officer, string reason, DateTimeOffset now)
    {
        var next = ClaimTransitions.Next(Status, ClaimAction.Reject);
        EnsureAssignedOfficer(officer);
        var why = Guard.Required(reason, "Reason");

        var released = OutstandingReserve; // captured before closing
        DecisionAt = now;
        ClosedAt = now;
        MoveTo(next, officer, now, why);
        Raise(new ClaimRejected(Id, now, why, released));
    }

    /// <summary>Internal note; any staff member in the claim's market may add one. Not a lifecycle action.</summary>
    public ClaimNote AddNote(Actor staff, string text, DateTimeOffset now)
    {
        EnsureStaffInMarket(staff);
        var note = new ClaimNote(Id, staff.UserId, Guard.Required(text, "Note"), now);
        _notes.Add(note);
        return note;
    }

    // ---------------------------------------------------------------- Claimant actions

    public void ProvideInformation(Actor claimant, Guid requestId, string response, DateTimeOffset now)
    {
        var next = ClaimTransitions.Next(Status, ClaimAction.ProvideInformation);
        EnsureOwner(claimant);
        var text = Guard.Required(response, "Response");
        var request = OpenInformationRequest;
        if (request is null || request.Id != requestId)
            throw new DomainValidationException("This information request is not open on the claim.");

        request.Answer(text, now);
        MoveTo(next, claimant, now, reason: null);
        Raise(new InformationProvided(Id, now, request.Id));
    }

    // ---------------------------------------------------------------- Helpers

    private void MoveTo(ClaimStatus to, Actor actor, DateTimeOffset now, string? reason)
    {
        _statusHistory.Add(new ClaimStatusChange(Id, Status, to, actor.UserId, actor.Role, now, reason));
        Status = to;
        LastStatusChangeAt = now;
    }

    private void EnsureStaffInMarket(Actor actor)
    {
        if (!actor.IsStaff)
            throw new ClaimAccessDeniedException("Only claims staff can do this.");
        if (actor.Market != Market)
            throw new ClaimAccessDeniedException($"This claim belongs to the {Market} market.");
    }

    private void EnsureAssignedOfficer(Actor actor)
    {
        EnsureStaffInMarket(actor);
        if (AssignedOfficerId != actor.UserId)
            throw new ClaimAccessDeniedException("Only the officer assigned to this claim can do this.");
    }

    private void EnsureOwner(Actor actor)
    {
        if (actor.Role != UserRole.Claimant || actor.UserId != ClaimantId)
            throw new ClaimAccessDeniedException("Only the claimant who reported this claim can do this.");
    }
}
