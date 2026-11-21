using Microsoft.AspNetCore.Mvc;

namespace ChubbClaims.Presentation.Controllers;

/// <summary>
/// Base for all API controllers. <see cref="ApiControllerAttribute"/> gives automatic
/// DataAnnotations validation (400 ValidationProblemDetails) and binding-source inference.
/// Holds shared helpers only (e.g. the current user id, added with auth); never business logic.
/// Each controller declares its own explicit kebab-case <c>[Route]</c> so URLs match the spec.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class BaseApiController : ControllerBase;
