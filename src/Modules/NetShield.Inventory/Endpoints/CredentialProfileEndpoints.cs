using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Credentials;
using NetShield.Inventory.Credentials.Handlers;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;
using NetShield.Platform.Validation;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The credential profile endpoints, under <c>/api/v1/credential-profiles</c>
/// (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// <para>
/// Every route on this group requires <see cref="Permission.CredentialsManage"/>, reads
/// included. WP-0.5 gave that permission to the Administrator alone, and there is no
/// <c>CredentialsRead</c> member to gate a read behind — inventing one would be a change to the
/// RBAC table that WP-1.2 was not told to make, while a profile's username is itself half of an
/// SSH credential and the list of names says exactly which accounts NetShield holds passwords
/// for.
/// </para>
/// <para>
/// There is no route here that returns material and no route that decrypts anything. The decrypt
/// path is internal to this module and has no HTTP surface at all in this package — WP-1.3 owns
/// the collector-job endpoint it will eventually be reached from (ARCHITECTURE.md §7).
/// </para>
/// <para>
/// There is also no rotation route. Re-wrapping every stored credential under a new
/// key-encryption key is key management rather than application traffic, and it runs as
/// <c>NetShield.Web.Host --rewrap</c> — a route would put the most privileged cryptographic
/// operation in the system permanently on the web attack surface.
/// </para>
/// </remarks>
public static class CredentialProfileEndpoints
{
    /// <summary>The group every route below hangs from.</summary>
    public const string RoutePrefix = "/api/v1/credential-profiles";

    /// <summary>What an audit row from these routes says it acted on.</summary>
    private const string TargetType = "credential-profile";

    /// <summary>Maps the credential profile endpoints. Called once, by the composition root.</summary>
    public static IEndpointRouteBuilder MapCredentialProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup(RoutePrefix).WithTags("Credential profiles");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permission.CredentialsManage)
            .WithName("ListCredentialProfiles")
            .Produces<CursorPage<CredentialProfileSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(Permission.CredentialsManage)
            .WithName("GetCredentialProfile")
            .Produces<CredentialProfileDetail>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationFilter<CreateCredentialProfileRequest>>()
            .RequirePermission(Permission.CredentialsManage)
            .Audits("inventory.credential-profile-create", TargetType)
            .WithName("CreateCredentialProfile")
            .Produces<CredentialProfileDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/{id:guid}", UpdateAsync)
            .AddEndpointFilter<ValidationFilter<UpdateCredentialProfileRequest>>()
            .RequirePermission(Permission.CredentialsManage)
            .Audits("inventory.credential-profile-update", TargetType)
            .WithName("UpdateCredentialProfile")
            .Produces<CredentialProfileDetail>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // The material has a route of its own because it can never be read back, so it cannot be
        // part of a whole-resource replacement. Audited under its own action name, which is what
        // makes "when was this credential last rotated" a query over the audit log.
        group.MapPut("/{id:guid}/material", ReplaceMaterialAsync)
            .AddEndpointFilter<ValidationFilter<ReplaceCredentialMaterialRequest>>()
            .RequirePermission(Permission.CredentialsManage)
            .Audits("inventory.credential-profile-rotate", TargetType)
            .WithName("ReplaceCredentialMaterial")
            .Produces<CredentialProfileDetail>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(Permission.CredentialsManage)
            .Audits("inventory.credential-profile-delete", TargetType)
            .WithName("DeleteCredentialProfile")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        GetCredentialProfileListHandler handler,
        CancellationToken cancellationToken,
        string? cursor = null,
        int? limit = null,
        string? sort = null,
        bool descending = false,
        CredentialKind? kind = null,
        string? search = null)
    {
        Result<PageRequest> page = PageRequest.Create(cursor, limit);

        if (!page.IsSuccess)
        {
            return Result<CursorPage<CredentialProfileSummary>>.Failure(page.Error).ToHttpResult();
        }

        Result<CredentialProfileSortField> field = ParseSort(sort);

        if (!field.IsSuccess)
        {
            return Result<CursorPage<CredentialProfileSummary>>.Failure(field.Error).ToHttpResult();
        }

        CredentialProfileListQuery query = new(page.Value, field.Value, descending, kind, search);

        return (await handler.HandleAsync(query, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        GetCredentialProfileHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();

    private static async Task<IResult> CreateAsync(
        CreateCredentialProfileRequest request,
        CreateCredentialProfileHandler handler,
        CancellationToken cancellationToken)
    {
        Result<CredentialProfileDetail> result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"{RoutePrefix}/{result.Value.Id}", result.Value)
            : result.ToHttpResult();
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateCredentialProfileRequest request,
        UpdateCredentialProfileHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult();

    private static async Task<IResult> ReplaceMaterialAsync(
        Guid id,
        ReplaceCredentialMaterialRequest request,
        ReplaceCredentialMaterialHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult();

    private static async Task<IResult> DeleteAsync(
        Guid id,
        DeleteCredentialProfileHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();

    /// <summary>
    /// Reads the <c>sort</c> query parameter. Unrecognised is a 400 naming what is available,
    /// rather than a silent fall back to the default (WP-1.1).
    /// </summary>
    private static Result<CredentialProfileSortField> ParseSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return CredentialProfileSortField.CreatedAt;
        }

        return Enum.TryParse(sort, ignoreCase: true, out CredentialProfileSortField field)
            ? field
            : CredentialErrors.UnknownSort(sort, Enum.GetNames<CredentialProfileSortField>());
    }
}
